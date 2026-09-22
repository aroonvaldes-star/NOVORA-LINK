namespace NOVORA.ExInEngine;

public sealed class ExInDiagnosticSession
{
    private const int MaximumSamplesPerPhaseVE = 256;
    private const int MinimumTravelSamplesVE = 8;
    private static readonly TimeSpan MinimumRestDurationVE = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumTravelDurationVE = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumSessionDurationVE = TimeSpan.FromSeconds(45);
    private readonly string _profileKeyVE;
    private readonly AxisWindowVE[] _restVE = CreateWindowsVE();
    private readonly AxisWindowVE[] _travelVE = CreateWindowsVE();
    private ExInButtons _buttonsAtStartVE;
    private ExInButtons _buttonsReleasedVE;
    private ExInState _lastRawVE;
    private ExInState _lastCorrectedVE;
    private TimeSpan? _startedAtVE;
    private TimeSpan? _travelStartedAtVE;
    private TimeSpan? _travelEndedAtVE;

    public ExInDiagnosticSession(string profileKey)
    {
        if (string.IsNullOrWhiteSpace(profileKey)) throw new ArgumentException("Se requiere una identidad de control.", nameof(profileKey));
        _profileKeyVE = profileKey;
    }

    public ExInDiagnosticPhase PhaseVE { get; private set; } = ExInDiagnosticPhase.Rest;
    public int RestSamplesVE { get; private set; }
    public int TravelSamplesVE { get; private set; }

    public void BeginTravelVE(TimeSpan timestamp)
    {
        if (PhaseVE != ExInDiagnosticPhase.Rest)
            throw new InvalidOperationException("La fase de reposo ya terminó.");
        PhaseVE = ExInDiagnosticPhase.Travel;
        _travelStartedAtVE = timestamp;
    }

    public bool IsExpiredVE(TimeSpan timestamp)
        => _startedAtVE is { } startedAt && timestamp - startedAt >= MaximumSessionDurationVE;

    public void ObserveVE(ExInState raw, ExInState corrected, TimeSpan timestamp)
    {
        if (PhaseVE == ExInDiagnosticPhase.Complete) return;
        _startedAtVE ??= timestamp;
        if (IsExpiredVE(timestamp)) return;
        _lastRawVE = raw;
        _lastCorrectedVE = corrected;

        AxisWindowVE[] windows = PhaseVE == ExInDiagnosticPhase.Rest ? _restVE : _travelVE;
        int count = PhaseVE == ExInDiagnosticPhase.Rest ? RestSamplesVE : TravelSamplesVE;
        if (count < MaximumSamplesPerPhaseVE)
        {
            windows[0].ObserveVE(raw.LeftX);
            windows[1].ObserveVE(raw.LeftY);
            windows[2].ObserveVE(raw.RightX);
            windows[3].ObserveVE(raw.RightY);
            windows[4].ObserveVE(raw.LeftTrigger);
            windows[5].ObserveVE(raw.RightTrigger);
            if (PhaseVE == ExInDiagnosticPhase.Rest) RestSamplesVE++;
            else
            {
                TravelSamplesVE++;
                _travelEndedAtVE = timestamp;
            }
        }

        ObserveButtonsVE(raw.Buttons);
    }

    public ExInDiagnosticStatus CompleteVE(TimeSpan timestamp)
    {
        if (PhaseVE == ExInDiagnosticPhase.Complete)
            throw new InvalidOperationException("La sesión de diagnóstico ya terminó.");
        PhaseVE = ExInDiagnosticPhase.Complete;

        List<string> affected = [];
        ExInHealth classification = ExInHealth.Healthy;
        string explanation = "Control dentro de parámetros normales.";
        bool correctable = false;

        TimeSpan restDuration = (_travelStartedAtVE ?? timestamp) - (_startedAtVE ?? timestamp);
        TimeSpan travelDuration = (_travelEndedAtVE ?? _travelStartedAtVE ?? timestamp) - (_travelStartedAtVE ?? timestamp);
        if (IsExpiredVE(timestamp) || RestSamplesVE == 0 || restDuration < MinimumRestDurationVE ||
            TravelSamplesVE < MinimumTravelSamplesVE || travelDuration < MinimumTravelDurationVE)
        {
            classification = ExInHealth.CalibrationIncomplete;
            explanation = "Faltan muestras de reposo o recorrido para completar el diagnóstico.";
        }
        else
        {
            string[] axisNames = ["LeftX", "LeftY", "RightX", "RightY"];
            for (int index = 0; index < 4; index++)
            {
                AxisWindowVE rest = _restVE[index];
                AxisWindowVE travel = _travelVE[index];
                if (rest.StandardDeviation >= 2600 && rest.JumpCount > Math.Max(2, rest.Count / 8))
                    PromoteVE(ExInHealth.ProbableHardwareFault, axisNames[index], "Ruido o saltos persistentes fuera del rango corregible.", false,
                        ref classification, affected, ref explanation, ref correctable);
                else if (Math.Abs(rest.Mean) >= 7000)
                    PromoteVE(ExInHealth.ProbableHardwareFault, axisNames[index], "Drift de reposo demasiado alto para una corrección confiable.", false,
                        ref classification, affected, ref explanation, ref correctable);
                else if (Math.Abs(rest.Mean) >= 1800)
                    PromoteVE(ExInHealth.CorrectableByCalibration, axisNames[index], "Drift estable corregible mediante calibración.", true,
                        ref classification, affected, ref explanation, ref correctable);

                int negative = (int)Math.Round(rest.Mean) - travel.Minimum;
                int positive = travel.Maximum - (int)Math.Round(rest.Mean);
                if (negative < 8192 || positive < 8192)
                    PromoteVE(ExInHealth.ReviewRecommended, axisNames[index], "Recorrido insuficiente o severamente asimétrico.", false,
                        ref classification, affected, ref explanation, ref correctable);
            }

            string[] triggerNames = ["LeftTrigger", "RightTrigger"];
            for (int index = 4; index < 6; index++)
            {
                AxisWindowVE rest = _restVE[index];
                AxisWindowVE travel = _travelVE[index];
                if (rest.Mean > 1000)
                    PromoteVE(ExInHealth.CorrectableByCalibration, triggerNames[index - 4], "El gatillo presenta offset estable en reposo.", true,
                        ref classification, affected, ref explanation, ref correctable);
                if (travel.Maximum - rest.Mean < 8192)
                    PromoteVE(ExInHealth.ReviewRecommended, triggerNames[index - 4], "El gatillo no alcanza un recorrido utilizable.", false,
                        ref classification, affected, ref explanation, ref correctable);
            }

            ExInButtons stuckButtons = _buttonsAtStartVE & ~_buttonsReleasedVE & _lastRawVE.Buttons;
            for (int index = 0; index < 15; index++)
            {
                ExInButtons flag = (ExInButtons)(1 << index);
                if (!stuckButtons.HasFlag(flag)) continue;
                PromoteVE(ExInHealth.ProbableHardwareFault, ((ExInButtons)(1 << index)).ToString(), "El botón no se liberó durante la prueba guiada.", false,
                    ref classification, affected, ref explanation, ref correctable);
            }
        }

        int severity = SeverityVE(classification);
        if (classification is ExInHealth.ProbableHardwareFault or ExInHealth.ReviewRecommended) correctable = false;
        return new(_profileKeyVE, classification, affected.Distinct().ToArray(), _lastRawVE, _lastCorrectedVE,
            severity, explanation, correctable, DateTimeOffset.UtcNow);
    }

    private void ObserveButtonsVE(ExInButtons buttons)
    {
        if (RestSamplesVE == 1) _buttonsAtStartVE = buttons;
        _buttonsReleasedVE |= _buttonsAtStartVE & ~buttons;
    }

    private static void PromoteVE(ExInHealth candidate, string control, string message, bool canCorrect,
        ref ExInHealth current, List<string> affected, ref string explanation, ref bool correctable)
    {
        affected.Add(control);
        if (SeverityVE(candidate) < SeverityVE(current)) return;
        if (SeverityVE(candidate) > SeverityVE(current)) correctable = canCorrect;
        else correctable |= canCorrect;
        current = candidate;
        explanation = message;
    }

    private static int SeverityVE(ExInHealth health) => health switch
    {
        ExInHealth.Healthy => 0,
        ExInHealth.CorrectableByCalibration => 1,
        ExInHealth.CalibrationIncomplete => 1,
        ExInHealth.ReviewRecommended => 2,
        ExInHealth.ProbableHardwareFault => 3,
        _ => 0
    };

    private static AxisWindowVE[] CreateWindowsVE()
        => [new(), new(), new(), new(), new(), new()];

    private sealed class AxisWindowVE
    {
        private short? _previousVE;
        public int Count { get; private set; }
        public double Mean { get; private set; }
        public double M2 { get; private set; }
        public short Minimum { get; private set; } = short.MaxValue;
        public short Maximum { get; private set; } = short.MinValue;
        public int JumpCount { get; private set; }
        public double StandardDeviation => Count > 1 ? Math.Sqrt(M2 / (Count - 1)) : 0;

        public void ObserveVE(short value)
        {
            Count++;
            double delta = value - Mean;
            Mean += delta / Count;
            M2 += delta * (value - Mean);
            Minimum = Math.Min(Minimum, value);
            Maximum = Math.Max(Maximum, value);
            if (_previousVE is short previous && Math.Abs(value - (int)previous) > 6000) JumpCount++;
            _previousVE = value;
        }
    }
}
