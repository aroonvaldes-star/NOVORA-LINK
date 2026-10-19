namespace NOVORA.ExInEngine;

public sealed record ExInAxisDeadzone(
    double LeftX,
    double LeftY,
    double RightX,
    double RightY)
{
    public static ExInAxisDeadzone DefaultVE { get; } = new(0.05, 0.05, 0.05, 0.05);
}

public sealed record ExInCalibrationProfile(
    int SchemaVersion,
    string ProfileKey,
    ExInState Center,
    ExInState Minimum,
    ExInState Maximum,
    ExInAxisDeadzone Deadzone,
    DateTimeOffset CalibratedAtUtc)
{
    public const int CurrentSchemaVersionVE = 1;
    private const int MinimumStickTravelVE = 16384;
    private const int MinimumStickSideTravelVE = 8192;
    private const int MinimumTriggerTravelVE = 8192;

    public static ExInCalibrationProfile CreateVE(
        string profileKey,
        ExInState center,
        ExInState minimum,
        ExInState maximum,
        ExInAxisDeadzone? deadzone = null)
        => new(
            CurrentSchemaVersionVE,
            profileKey,
            center,
            minimum,
            maximum,
            deadzone ?? ExInAxisDeadzone.DefaultVE,
            DateTimeOffset.UtcNow);

    public bool IsValidVE(string expectedProfileKey)
        => SchemaVersion == CurrentSchemaVersionVE &&
           string.Equals(ProfileKey, expectedProfileKey, StringComparison.Ordinal) &&
           IsDeadzoneVE(Deadzone.LeftX) && IsDeadzoneVE(Deadzone.LeftY) &&
           IsDeadzoneVE(Deadzone.RightX) && IsDeadzoneVE(Deadzone.RightY) &&
           HasTravelVE(Minimum.LeftX, Maximum.LeftX, MinimumStickTravelVE) &&
           HasTravelVE(Minimum.LeftY, Maximum.LeftY, MinimumStickTravelVE) &&
           HasTravelVE(Minimum.RightX, Maximum.RightX, MinimumStickTravelVE) &&
           HasTravelVE(Minimum.RightY, Maximum.RightY, MinimumStickTravelVE) &&
           IsTriggerRangeVE(Center.LeftTrigger, Minimum.LeftTrigger, Maximum.LeftTrigger) &&
           IsTriggerRangeVE(Center.RightTrigger, Minimum.RightTrigger, Maximum.RightTrigger) &&
           HasCenteredTravelVE(Center.LeftX, Minimum.LeftX, Maximum.LeftX) &&
           HasCenteredTravelVE(Center.LeftY, Minimum.LeftY, Maximum.LeftY) &&
           HasCenteredTravelVE(Center.RightX, Minimum.RightX, Maximum.RightX) &&
           HasCenteredTravelVE(Center.RightY, Minimum.RightY, Maximum.RightY);

    public ExInState CorrectVE(ExInState state)
        => new(
            CorrectAxisVE(state.LeftX, Center.LeftX, Minimum.LeftX, Maximum.LeftX, Deadzone.LeftX),
            CorrectAxisVE(state.LeftY, Center.LeftY, Minimum.LeftY, Maximum.LeftY, Deadzone.LeftY),
            CorrectAxisVE(state.RightX, Center.RightX, Minimum.RightX, Maximum.RightX, Deadzone.RightX),
            CorrectAxisVE(state.RightY, Center.RightY, Minimum.RightY, Maximum.RightY, Deadzone.RightY),
            CorrectTriggerVE(state.LeftTrigger, Minimum.LeftTrigger, Maximum.LeftTrigger),
            CorrectTriggerVE(state.RightTrigger, Minimum.RightTrigger, Maximum.RightTrigger),
            state.Buttons);

    private static bool HasTravelVE(short minimum, short maximum, int required)
        => maximum - minimum >= required;

    private static bool HasCenteredTravelVE(short center, short minimum, short maximum)
        => center - minimum >= MinimumStickSideTravelVE && maximum - center >= MinimumStickSideTravelVE;

    private static bool IsTriggerRangeVE(short center, short minimum, short maximum)
        => minimum >= 0 && center >= minimum && center <= maximum &&
           HasTravelVE(minimum, maximum, MinimumTriggerTravelVE);

    private static bool IsDeadzoneVE(double value)
        => double.IsFinite(value) && value >= 0 && value < 0.5;

    /// <summary>
    /// Corrige un eje analógico con deadzone y normalización.
    /// ⭐ FIJO: Ahora valida rangos mínimo/máximo antes de procesamiento
    /// </summary>
    private static short CorrectAxisVE(short value, short center, short minimum, short maximum, double deadzone)
    {
        // ⭐ NUEVO: Validar que minimum < maximum
        if (minimum >= maximum)
        {
            // Controlador severamente dañado: devolver valor clampeado neutral
            return Math.Clamp(value, short.MinValue, short.MaxValue);
        }

        // ⭐ NUEVO: Usar spans negativos y positivos por separado
        double negativeSpan = Math.Max(1.0, center - minimum); // Rango hacia abajo
        double positiveSpan = Math.Max(1.0, maximum - center); // Rango hacia arriba

        double span = value >= center ? positiveSpan : negativeSpan;
        double normalized = Math.Clamp((value - center) / span, -1d, 1d);

        // Aplicar deadzone
        if (Math.Abs(normalized) <= deadzone) 
            return 0;

        // Expandir después de deadzone
        normalized = Math.CopySign((Math.Abs(normalized) - deadzone) / (1 - deadzone), normalized);

        // Clampear y convertir a rango final
        if (normalized <= -1d) return short.MinValue;
        if (normalized >= 1d) return short.MaxValue;
        
        return (short)Math.Clamp((int)Math.Round(normalized * 32767), short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// Corrige un gatillo con normalización lineal.
    /// ⭐ FIJO: Ahora valida rangos antes de procesamiento
    /// </summary>
    private static short CorrectTriggerVE(short value, short minimum, short maximum)
    {
        // ⭐ NUEVO: Validar que minimum <= maximum
        if (minimum > maximum)
        {
            // Controlador dañado: devolver valor neutral
            return 0;
        }

        // ⭐ NUEVO: Evitar división imprecisa
        int span = Math.Max(1, maximum - minimum);
        double normalized = Math.Clamp((double)(value - minimum) / span, 0d, 1d);
        
        return (short)Math.Round(normalized * 32767);
    }
}
