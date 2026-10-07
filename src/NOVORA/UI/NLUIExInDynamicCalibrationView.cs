using NOVORA.ExInEngine;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace NOVORA;

/// <summary>Visual vectorial en vivo para calibrar mandos Xbox y Sony.</summary>
public sealed class NLUIExInDynamicCalibrationView : FrameworkElement
{
    private ExInLiveSnapshot? _snapshot;

    public NLUIExInDynamicCalibrationView()
    {
        MinHeight = 292;
        AutomationProperties.SetName(this, "Mando y calibración dinámica de ExInEngine");
    }

    public void UpdateVE(ExInLiveSnapshot? snapshot)
    {
        _snapshot = snapshot;
        AutomationProperties.SetHelpText(this, AccessibleDescriptionVE(snapshot));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 120 || ActualHeight < 160) return;
        PaletteVE p = PaletteVE.Create(this);
        bool sony = IsSonyVE();
        ExInState state = _snapshot?.CorrectedState ?? default;
        ExInCalibrationProgress progress = ProgressVE();
        DrawHeaderVE(dc, p, sony, progress);
        Rect stage = new(8, 35, ActualWidth - 16, Math.Max(190, ActualHeight - 78));
        if (sony) DrawSonyVE(dc, stage, state, progress, p);
        else DrawXboxVE(dc, stage, state, progress, p);
        DrawOverallProgressVE(dc, p, progress);
    }

    private void DrawXboxVE(DrawingContext dc, Rect stage, ExInState state, ExInCalibrationProgress progress, PaletteVE p)
    {
        Rect body = FitVE(stage, 600, 235);
        double s = body.Width / 600;
        dc.DrawGeometry(p.Surface, new Pen(p.Border, Math.Max(1, 2 * s)), XboxShellVE(body));
        DrawTriggerVE(dc, body, .12, "LT", state.LeftTrigger, progress.LeftTrigger, p, s);
        DrawTriggerVE(dc, body, .68, "RT", state.RightTrigger, progress.RightTrigger, p, s);
        DrawStickVE(dc, AtVE(body, .27, .40), state.LeftX, state.LeftY, progress.LeftStick, p, s);
        DrawDPadVE(dc, AtVE(body, .34, .68), state.Buttons, p, s);
        DrawStickVE(dc, AtVE(body, .62, .66), state.RightX, state.RightY, progress.RightStick, p, s);
        DrawFaceVE(dc, AtVE(body, .77, .40), state.Buttons, false, p, s);
        DrawSmallButtonVE(dc, AtVE(body, .46, .47), state.Buttons.HasFlag(ExInButtons.Back), p, s);
        DrawSmallButtonVE(dc, AtVE(body, .55, .47), state.Buttons.HasFlag(ExInButtons.Start), p, s);
        DrawGuideVE(dc, AtVE(body, .50, .31), state.Buttons.HasFlag(ExInButtons.Guide), "X", p, s);
    }

    private void DrawSonyVE(DrawingContext dc, Rect stage, ExInState state, ExInCalibrationProgress progress, PaletteVE p)
    {
        Rect body = FitVE(stage, 600, 235);
        double s = body.Width / 600;
        dc.DrawGeometry(p.Surface, new Pen(p.Border, Math.Max(1, 2 * s)), SonyShellVE(body));
        DrawTriggerVE(dc, body, .11, "L2", state.LeftTrigger, progress.LeftTrigger, p, s);
        DrawTriggerVE(dc, body, .69, "R2", state.RightTrigger, progress.RightTrigger, p, s);
        DrawDPadVE(dc, AtVE(body, .27, .42), state.Buttons, p, s);
        DrawFaceVE(dc, AtVE(body, .74, .42), state.Buttons, true, p, s);
        DrawStickVE(dc, AtVE(body, .40, .68), state.LeftX, state.LeftY, progress.LeftStick, p, s);
        DrawStickVE(dc, AtVE(body, .60, .68), state.RightX, state.RightY, progress.RightStick, p, s);
        Rect touchpad = new(body.Left + body.Width * .39, body.Top + body.Height * .16, body.Width * .22, body.Height * .24);
        dc.DrawRoundedRectangle(state.Buttons.HasFlag(ExInButtons.Touchpad) ? p.AccentSoft : p.SurfaceRaised,
            new Pen(state.Buttons.HasFlag(ExInButtons.Touchpad) ? p.Accent : p.Border, Math.Max(1, 1.4 * s)),
            touchpad, 7 * s, 7 * s);
        DrawTextVE(dc, "TOUCHPAD", touchpad.Left + touchpad.Width / 2, touchpad.Top + 7 * s,
            8 * s, p.Muted, FontWeights.SemiBold, TextAlignment.Center);
        DrawSmallButtonVE(dc, AtVE(body, .35, .38), state.Buttons.HasFlag(ExInButtons.Back), p, s);
        DrawSmallButtonVE(dc, AtVE(body, .65, .38), state.Buttons.HasFlag(ExInButtons.Start), p, s);
        DrawGuideVE(dc, AtVE(body, .50, .53), state.Buttons.HasFlag(ExInButtons.Guide), "PS", p, s);
    }

    private void DrawHeaderVE(DrawingContext dc, PaletteVE p, bool sony, ExInCalibrationProgress progress)
    {
        string family = _snapshot?.Device is null ? "ESPERANDO MANDO" : ControllerLabelVE(sony);
        DrawTextVE(dc, family, 0, 2, 11, p.Text, FontWeights.Bold);
        string state = _snapshot?.Calibrating == true ? $"CALIBRANDO · {progress.Overall}%" :
            _snapshot?.Calibrated == true ? "PERFIL ACTIVO" : "LISTO PARA CALIBRAR";
        DrawTextVE(dc, state, ActualWidth, 2, 11, _snapshot?.Calibrating == true ? p.Warning : p.Muted,
            FontWeights.SemiBold, TextAlignment.Right);
    }

    private void DrawOverallProgressVE(DrawingContext dc, PaletteVE p, ExInCalibrationProgress progress)
    {
        double y = ActualHeight - 21;
        DrawTextVE(dc, "RECORRIDO TOTAL", 0, y - 13, 9, p.Muted, FontWeights.SemiBold);
        DrawTextVE(dc, $"{progress.Overall}%", ActualWidth, y - 13, 10, p.Text, FontWeights.Bold, TextAlignment.Right);
        DrawTrackVE(dc, new Rect(0, y, ActualWidth, 8), progress.Overall / 100d,
            p.Border, _snapshot?.Calibrating == true ? p.Warning : p.Accent);
    }

    private void DrawStickVE(DrawingContext dc, Point center, short x, short y, int coverage, PaletteVE p, double s)
    {
        double outer = 31 * s, inner = 19 * s;
        dc.DrawEllipse(p.SurfaceRaised, new Pen(p.Border, Math.Max(1, 2 * s)), center, outer, outer);
        DrawCoverageArcVE(dc, center, outer + 4 * s, coverage, p, s);
        double dx = Math.Clamp(x / 32767d, -1, 1) * (outer - inner) * .9;
        double dy = Math.Clamp(y / 32767d, -1, 1) * (outer - inner) * .9;
        Point knob = new(center.X + dx, center.Y + dy);
        dc.DrawEllipse(p.AccentSoft, new Pen(p.Accent, Math.Max(1, 1.5 * s)), knob, inner, inner);
        dc.DrawEllipse(p.Accent, null, knob, 4 * s, 4 * s);
    }

    private static void DrawCoverageArcVE(DrawingContext dc, Point center, double radius, int coverage, PaletteVE p, double s)
    {
        double fraction = Math.Clamp(coverage / 100d, 0, 1);
        dc.DrawEllipse(null, new Pen(p.Border, Math.Max(1, 2 * s)), center, radius, radius);
        if (fraction <= 0) return;
        Point startPoint = PolarVE(center, radius, -90);
        Point endPoint = PolarVE(center, radius, -90 + 359.9 * fraction);
        StreamGeometry arc = new();
        using (StreamGeometryContext c = arc.Open())
        {
            c.BeginFigure(startPoint, false, false);
            c.ArcTo(endPoint, new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(p.Warning, Math.Max(2, 3 * s)), arc);
    }

    private void DrawTriggerVE(DrawingContext dc, Rect body, double px, string label, short raw, int coverage, PaletteVE p, double s)
    {
        double x = body.Left + body.Width * px, y = body.Top + 9 * s, width = body.Width * .19;
        double value = Math.Clamp(Math.Max(0, (int)raw) / 32767d, 0, 1);
        DrawTextVE(dc, $"{label}  {value:P0}", x, y, 9 * s, p.Text, FontWeights.Bold);
        DrawTrackVE(dc, new Rect(x, y + 17 * s, width, 9 * s), value, p.Border, p.Accent);
        DrawTextVE(dc, $"REC {Math.Clamp(coverage, 0, 100)}%", x + width, y,
            8 * s, _snapshot?.Calibrating == true ? p.Warning : p.Muted, FontWeights.SemiBold, TextAlignment.Right);
    }

    private static void DrawDPadVE(DrawingContext dc, Point c, ExInButtons buttons, PaletteVE p, double s)
    {
        double a = 13 * s;
        DrawDPadKeyVE(dc, new(c.X - a / 2, c.Y - a * 1.55, a, a), buttons.HasFlag(ExInButtons.DPadUp), p, s);
        DrawDPadKeyVE(dc, new(c.X - a / 2, c.Y + a * .55, a, a), buttons.HasFlag(ExInButtons.DPadDown), p, s);
        DrawDPadKeyVE(dc, new(c.X - a * 1.55, c.Y - a / 2, a, a), buttons.HasFlag(ExInButtons.DPadLeft), p, s);
        DrawDPadKeyVE(dc, new(c.X + a * .55, c.Y - a / 2, a, a), buttons.HasFlag(ExInButtons.DPadRight), p, s);
        dc.DrawEllipse(p.SurfaceRaised, null, c, 7 * s, 7 * s);
    }

    private static void DrawDPadKeyVE(DrawingContext dc, Rect r, bool pressed, PaletteVE p, double s)
        => dc.DrawRoundedRectangle(pressed ? p.Accent : p.SurfaceRaised,
            new Pen(pressed ? p.Accent : p.Border, Math.Max(1, s)), r, 3 * s, 3 * s);

    private void DrawFaceVE(DrawingContext dc, Point c, ExInButtons buttons, bool sony, PaletteVE p, double s)
    {
        DrawFaceKeyVE(dc, new(c.X, c.Y + 23 * s), sony ? "×" : "A", buttons.HasFlag(ExInButtons.South), p, s);
        DrawFaceKeyVE(dc, new(c.X + 23 * s, c.Y), sony ? "○" : "B", buttons.HasFlag(ExInButtons.East), p, s);
        DrawFaceKeyVE(dc, new(c.X - 23 * s, c.Y), sony ? "□" : "X", buttons.HasFlag(ExInButtons.West), p, s);
        DrawFaceKeyVE(dc, new(c.X, c.Y - 23 * s), sony ? "△" : "Y", buttons.HasFlag(ExInButtons.North), p, s);
    }

    private void DrawFaceKeyVE(DrawingContext dc, Point c, string label, bool pressed, PaletteVE p, double s)
    {
        dc.DrawEllipse(pressed ? p.Accent : p.SurfaceRaised,
            new Pen(pressed ? p.Accent : p.Border, Math.Max(1, 1.5 * s)), c, 11 * s, 11 * s);
        DrawTextVE(dc, label, c.X, c.Y - 7 * s, 10 * s, p.Text, FontWeights.Bold, TextAlignment.Center);
    }

    private static void DrawSmallButtonVE(DrawingContext dc, Point c, bool pressed, PaletteVE p, double s)
        => dc.DrawRoundedRectangle(pressed ? p.Accent : p.SurfaceRaised,
            new Pen(pressed ? p.Accent : p.Border, Math.Max(1, s)),
            new Rect(c.X - 8 * s, c.Y - 5 * s, 16 * s, 10 * s), 5 * s, 5 * s);

    private void DrawGuideVE(DrawingContext dc, Point c, bool pressed, string label, PaletteVE p, double s)
    {
        dc.DrawEllipse(pressed ? p.Accent : p.SurfaceRaised,
            new Pen(pressed ? p.Accent : p.Border, Math.Max(1, s)), c, 10 * s, 10 * s);
        DrawTextVE(dc, label, c.X, c.Y - 6 * s, 7 * s, p.Text, FontWeights.Bold, TextAlignment.Center);
    }

    private static void DrawTrackVE(DrawingContext dc, Rect r, double fraction, Brush back, Brush fill)
    {
        double radius = r.Height / 2;
        dc.DrawRoundedRectangle(back, null, r, radius, radius);
        double width = r.Width * Math.Clamp(fraction, 0, 1);
        if (width > 0) dc.DrawRoundedRectangle(fill, null, new Rect(r.X, r.Y, width, r.Height), radius, radius);
    }

    private static Rect FitVE(Rect stage, double ratioWidth, double ratioHeight)
    {
        double scale = Math.Min(stage.Width / ratioWidth, stage.Height / ratioHeight);
        double width = ratioWidth * scale, height = ratioHeight * scale;
        return new(stage.Left + (stage.Width - width) / 2, stage.Top + (stage.Height - height) / 2, width, height);
    }

    private static Point AtVE(Rect r, double x, double y) => new(r.Left + r.Width * x, r.Top + r.Height * y);
    private static Point PolarVE(Point c, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new(c.X + radius * Math.Cos(radians), c.Y + radius * Math.Sin(radians));
    }

    private static Geometry XboxShellVE(Rect r) => ShellVE(r, .18, .10, .09, .96, .25, .72);
    private static Geometry SonyShellVE(Rect r) => ShellVE(r, .16, .13, .07, .91, .30, .74);
    private static Geometry ShellVE(Rect r, double shoulder, double crown, double gripX, double gripY, double innerX, double innerY)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        StreamGeometry g = new();
        using StreamGeometryContext c = g.Open();
        c.BeginFigure(new(x + w * shoulder, y + h * crown), true, true);
        c.BezierTo(new(x + w * .08, y + h * .13), new(x + w * .01, y + h * .65), new(x + w * gripX, y + h * gripY), true, false);
        c.BezierTo(new(x + w * .14, y + h), new(x + w * innerX, y + h * .77), new(x + w * .34, y + h * innerY), true, false);
        c.BezierTo(new(x + w * .44, y + h * .70), new(x + w * .56, y + h * .70), new(x + w * .66, y + h * innerY), true, false);
        c.BezierTo(new(x + w * (1 - innerX), y + h * .77), new(x + w * .86, y + h), new(x + w * (1 - gripX), y + h * gripY), true, false);
        c.BezierTo(new(x + w * .99, y + h * .65), new(x + w * .92, y + h * .13), new(x + w * (1 - shoulder), y + h * crown), true, false);
        c.BezierTo(new(x + w * .64, y), new(x + w * .36, y), new(x + w * shoulder, y + h * crown), true, false);
        g.Freeze();
        return g;
    }

    private bool IsSonyVE() => _snapshot?.Device?.Identity?.Family == ExInControllerFamily.DualShock4 || _snapshot?.Device?.VendorId == 0x054C;
    private string ControllerLabelVE(bool sony)
    {
        string family = sony ? _snapshot?.Device?.ProductId == 0x0CE6 ? "DUALSENSE" : "DUALSHOCK 4" :
            _snapshot?.Device?.Identity?.Family == ExInControllerFamily.Xbox ? "XBOX" : "MANDO GENÉRICO";
        return $"{family} · {_snapshot?.DeviceName}";
    }
    private ExInCalibrationProgress ProgressVE() => _snapshot?.CalibrationProgress ??
        (_snapshot?.Calibrated == true ? new(100, 100, 100, 100) : new(0, 0, 0, 0));
    private static string AccessibleDescriptionVE(ExInLiveSnapshot? snapshot)
    {
        if (snapshot?.Device is null) return "No hay un mando detectado.";
        return snapshot.Calibrating ? $"Calibración dinámica al {snapshot.CalibrationProgress?.Overall ?? 0} por ciento." :
            snapshot.Calibrated ? "Perfil de calibración activo." : "Mando listo para calibrar.";
    }

    private void DrawTextVE(DrawingContext dc, string value, double x, double y, double size,
        Brush brush, FontWeight weight, TextAlignment alignment = TextAlignment.Left)
    {
        FormattedText text = new(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            Math.Max(7, size), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment = alignment };
        dc.DrawText(text, new Point(x, y));
    }

    private sealed record PaletteVE(Brush Text, Brush Muted, Brush Border, Brush Surface,
        Brush SurfaceRaised, Brush Accent, Brush AccentSoft, Brush Warning)
    {
        internal static PaletteVE Create(FrameworkElement owner)
        {
            Brush Get(string key, Color fallback) => owner.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
            Brush accent = Get("BlueBrush", Color.FromRgb(13, 142, 157));
            return new(Get("TextBrush", Colors.White), Get("MutedBrush", Color.FromRgb(144, 157, 174)),
                Get("BorderBrush", Color.FromRgb(53, 66, 84)), Get("PanelBrush2", Color.FromRgb(20, 27, 38)),
                Get("PanelBrush", Color.FromRgb(25, 34, 47)), accent,
                new SolidColorBrush(Color.FromArgb(72, 13, 142, 157)),
                Get("WarningBrush", Color.FromRgb(245, 158, 11)));
        }
    }
}
