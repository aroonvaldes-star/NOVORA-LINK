using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using NOVORA.Control;

namespace NOVORA.AndroidUI;

/// <summary>Animated, read-only trigger calibration guide driven by the live PC snapshot.</summary>
internal sealed class NLAndroidUITriggerCalibrationView : View
{
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private readonly float _density;
    private NLControlExIn? _snapshot;
    private float _leftValue, _rightValue;
    private int _leftProgress, _rightProgress, _overallProgress;
    private string _phase = string.Empty;

    internal NLAndroidUITriggerCalibrationView(Context context) : base(context)
    {
        _density = context.Resources!.DisplayMetrics!.Density;
        SetMinimumHeight((int)Dp(232));
        ContentDescription = "Guía animada de calibración de gatillos; espera datos de NOVORA PC.";
        ImportantForAccessibility = ImportantForAccessibility.Yes;
    }

    internal void UpdateVE(NLControlExIn? value)
    {
        _snapshot = value;
        var progress = value?.CalibrationProgress;
        _leftProgress = ClampPercent(progress?.LeftTrigger ?? 0);
        _rightProgress = ClampPercent(progress?.RightTrigger ?? 0);
        _overallProgress = ClampPercent(progress?.Overall ?? 0);
        _phase = progress?.Phase ?? string.Empty;
        ContentDescription = value?.Calibrating == true
            ? $"Calibración de gatillos en curso. Izquierdo {_leftProgress} por ciento; derecho {_rightProgress} por ciento; progreso total {_overallProgress} por ciento. {PhaseDescription(_phase)}"
            : value?.Calibrated == true
                ? $"Calibración completada. Gatillo izquierdo {_leftProgress} por ciento; derecho {_rightProgress} por ciento."
                : "Guía de calibración de gatillos. Inicia la calibración desde NOVORA PC para ver el avance en vivo.";
        PostInvalidateOnAnimation();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float width = Width;
        float height = Height;
        if (width <= 0 || height <= 0) return;

        var palette = NLAndroidUITheme.Current(Context!);
        Color background = Color.ParseColor(palette.SurfaceRaised);
        Color border = Color.ParseColor(palette.Border);
        Color text = Color.ParseColor(palette.Text);
        Color muted = Color.ParseColor(palette.Muted);
        Color accent = Color.ParseColor(palette.Accent);
        Color warning = Color.ParseColor(palette.Warning);
        Color success = Color.ParseColor(palette.Success);

        float pad = Dp(12);
        float gap = Dp(10);
        float cardTop = Dp(44);
        float cardBottom = height - Dp(46);
        float cardWidth = (width - 2 * pad - gap) / 2;
        bool calibrating = _snapshot?.Calibrating == true;
        bool ready = _snapshot?.Calibrated == true || _overallProgress >= 90;

        DrawText(canvas, "GATILLOS", pad, Dp(17), Dp(12), muted, bold: true);
        string instruction = calibrating
            ? PhaseInstruction(_phase)
            : ready ? "Recorrido capturado" : "Inicia la calibración desde PC";
        DrawText(canvas, instruction, pad, Dp(35), Dp(13), calibrating ? warning : text, bold: true);

        DrawTriggerCard(canvas, pad, cardTop, cardWidth, cardBottom - cardTop,
            "LT", "IZQUIERDO", _leftValue,
            _leftProgress, IsActivePhase(_phase, "LeftTrigger", calibrating),
            calibrating, background, border, text, muted, accent, warning);
        DrawTriggerCard(canvas, pad + cardWidth + gap, cardTop, cardWidth, cardBottom - cardTop,
            "RT", "DERECHO", _rightValue,
            _rightProgress, IsActivePhase(_phase, "RightTrigger", calibrating),
            calibrating, background, border, text, muted, accent, warning);

        DrawText(canvas, "PROGRESO TOTAL", pad, height - Dp(25), Dp(10), muted, bold: true);
        float progressX = pad + Dp(106);
        float progressW = Math.Max(Dp(30), width - progressX - pad - Dp(42));
        DrawTrack(canvas, progressX, height - Dp(31), progressW, Dp(7), border, accent, _overallProgress / 100f);
        DrawText(canvas, $"{_overallProgress}%", width - pad, height - Dp(24), Dp(11), text, bold: true, alignRight: true);

        float targetLeft = ClampPercent(_snapshot?.CorrectedLeftTrigger ?? 0);
        float targetRight = ClampPercent(_snapshot?.CorrectedRightTrigger ?? 0);
        bool valueMoving = Math.Abs(_leftValue - targetLeft) > .5f || Math.Abs(_rightValue - targetRight) > .5f;
        _leftValue += (targetLeft - _leftValue) * .34f;
        _rightValue += (targetRight - _rightValue) * .34f;
        if (valueMoving || calibrating) PostInvalidateDelayed(calibrating ? 32 : 16);
    }

    private void DrawTriggerCard(Canvas canvas, float x, float y, float width, float height,
        string shortName, string fullName, float animatedValue, int progress,
        bool active, bool calibrating, Color background, Color border, Color text, Color muted,
        Color accent, Color warning)
    {
        float radius = Dp(10);
        SetPaint(background);
        canvas.DrawRoundRect(new RectF(x, y, x + width, y + height), radius, radius, _paint);
        SetPaint(active ? warning : border, Paint.Style.Stroke, Dp(active ? 2 : 1));
        canvas.DrawRoundRect(new RectF(x, y, x + width, y + height), radius, radius, _paint);

        float left = x + Dp(12);
        DrawText(canvas, shortName, left, y + Dp(23), Dp(16), text, bold: true);
        DrawText(canvas, fullName, left + Dp(30), y + Dp(22), Dp(10), muted, bold: true);

        float trackTop = y + Dp(38);
        float trackBottom = y + height - Dp(37);
        float trackX = x + Dp(18);
        float trackWidth = Dp(18);
        float trackHeight = Math.Max(Dp(34), trackBottom - trackTop);
        SetPaint(border);
        canvas.DrawRoundRect(new RectF(trackX, trackTop, trackX + trackWidth, trackBottom), Dp(9), Dp(9), _paint);

        float fillHeight = trackHeight * Math.Clamp(animatedValue / 100f, 0f, 1f);
        SetPaint(active ? warning : accent);
        if (fillHeight > 0)
            canvas.DrawRoundRect(new RectF(trackX, trackBottom - fillHeight, trackX + trackWidth, trackBottom), Dp(9), Dp(9), _paint);

        float markerY = trackBottom - fillHeight;
        SetPaint(text);
        canvas.DrawCircle(trackX + trackWidth / 2, markerY, Dp(5), _paint);

        float valueX = trackX + trackWidth + Dp(12);
        DrawText(canvas, $"{Math.Clamp((int)Math.Round(animatedValue), 0, 100)}%", valueX, trackTop + Dp(13), Dp(20), text, bold: true);
        DrawText(canvas, "ACTUAL", valueX, trackTop + Dp(27), Dp(9), muted, bold: true);
        DrawText(canvas, $"Recorrido · {progress}%", valueX, trackBottom - Dp(2), Dp(10), muted);

        if (active && calibrating)
        {
            float pulse = .5f + .5f * (float)Math.Sin(SystemClock.UptimeMillis() / 220d);
            _paint.Color = Color.Argb((int)(25 + 55 * pulse), warning.R, warning.G, warning.B);
            _paint.SetStyle(Paint.Style.Fill);
            canvas.DrawCircle(x + width - Dp(17), y + Dp(18), Dp(5 + 2 * pulse), _paint);
            DrawText(canvas, "PRESIONA", valueX, y + height - Dp(9), Dp(9), warning, bold: true);
        }
        else
        {
            DrawText(canvas, calibrating ? "EN ESPERA" : "EN VIVO", valueX, y + height - Dp(9), Dp(9), muted, bold: true);
        }
    }

    private void DrawTrack(Canvas canvas, float x, float y, float width, float height,
        Color background, Color foreground, float fraction)
    {
        SetPaint(background);
        canvas.DrawRoundRect(new RectF(x, y, x + width, y + height), height / 2, height / 2, _paint);
        if (fraction <= 0) return;
        SetPaint(foreground);
        canvas.DrawRoundRect(new RectF(x, y, x + width * Math.Clamp(fraction, 0f, 1f), y + height), height / 2, height / 2, _paint);
    }

    private void DrawText(Canvas canvas, string value, float x, float baseline, float size,
        Color color, bool bold = false, bool alignRight = false)
    {
        _paint.Reset();
        _paint.AntiAlias = true;
        _paint.Color = color;
        _paint.TextSize = size;
        _paint.TextAlign = alignRight ? Paint.Align.Right : Paint.Align.Left;
        _paint.SetTypeface(Typeface.Create(bold ? "sans-serif-medium" : "sans-serif", TypefaceStyle.Normal));
        canvas.DrawText(value, x, baseline, _paint);
    }

    private void SetPaint(Color color, Paint.Style? style = null, float strokeWidth = 1)
    {
        _paint.Reset();
        _paint.AntiAlias = true;
        _paint.Color = color;
        _paint.SetStyle(style ?? Paint.Style.Fill);
        _paint.StrokeWidth = strokeWidth;
    }

    private float Dp(float value) => value * _density;
    private static int ClampPercent(int value) => Math.Clamp(value, 0, 100);
    private static bool IsActivePhase(string phase, string trigger, bool calibrating) => calibrating && phase == trigger;
    private static string PhaseInstruction(string phase) => phase switch
    {
        "LeftStick" => "Mueve el stick izquierdo por todo su recorrido",
        "RightStick" => "Mueve el stick derecho por todo su recorrido",
        "LeftTrigger" => "Presiona LT hasta el fondo y suéltalo",
        "RightTrigger" => "Presiona RT hasta el fondo y suéltalo",
        "Complete" => "Recorrido capturado",
        _ => "Mueve cada gatillo a su recorrido completo"
    };
    private static string PhaseDescription(string phase) => phase switch
    {
        "LeftStick" => "Mueve el stick izquierdo por todo su recorrido.",
        "RightStick" => "Mueve el stick derecho por todo su recorrido.",
        "LeftTrigger" => "Presiona el gatillo izquierdo hasta el fondo y suéltalo.",
        "RightTrigger" => "Presiona el gatillo derecho hasta el fondo y suéltalo.",
        "Complete" => "Recorrido de ambos gatillos capturado.",
        _ => "Mueve ambos gatillos a su recorrido completo."
    };
}
