using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NOVORA.ExInEngine;
using AvaloniaControl = Avalonia.Controls.Control;
using System.Globalization;

namespace NOVORA.Linux.ExIn;

/// <summary>
/// Avalonia port of Windows NLUIExInDynamicCalibrationView: live vector view for calibrating
/// Xbox and Sony controllers. Geometry, crops, colors and texts are the Windows values.
/// </summary>
internal sealed class LinuxExInCalibrationView : AvaloniaControl
{
    private static readonly Lazy<ControllerArtworkVE?> XboxCalibrationImageVE =
        new(() => LoadCalibrationImageVE("NLAssetExInXboxFrontSideMatte.png", 1045, 40, 755, 1100, 185, 745));
    private static readonly Lazy<ControllerArtworkVE?> DualShock4CalibrationImageVE =
        new(() => LoadCalibrationImageVE("NLAssetExInDualShock4FrontSideMatte.png", 960, 150, 750, 975, 300, 690));
    private ExInLiveSnapshot? _snapshot;
    private ExInBatteryStatus? _battery;

    public LinuxExInCalibrationView()
    {
        MinHeight = 420;
        AutomationProperties.SetName(this, "Mando y calibración dinámica de ExInEngine");
        LinuxTheme.Changed += (_, _) => InvalidateVisual();
    }

    public void UpdateVE(ExInLiveSnapshot? snapshot)
    {
        _snapshot = snapshot;
        UpdateAccessibleDescriptionVE();
        InvalidateVisual();
    }

    public void UpdateBatteryVE(ExInBatteryStatus? battery)
    {
        _battery = battery;
        UpdateAccessibleDescriptionVE();
        InvalidateVisual();
    }

    private void UpdateAccessibleDescriptionVE()
    {
        string battery = _battery is null ? string.Empty : _battery.State switch
        {
            ExInBatteryState.Charging => _battery.Percent is int percent
                ? $" Batería cargando al {percent} por ciento."
                : " Batería cargando; porcentaje no reportado.",
            ExInBatteryState.Charged => " Batería cargada.",
            _ => _battery.Percent is int percent ? $" Batería al {percent} por ciento." : string.Empty
        };
        int leftTrigger = Math.Clamp((int)(_snapshot?.CorrectedState.LeftTrigger ?? 0), 0, 32767);
        string trigger = _snapshot?.Calibrating == true
            ? $" Recorrido del gatillo izquierdo al {Math.Round(leftTrigger * 100d / 32767)} por ciento."
            : string.Empty;
        AutomationProperties.SetHelpText(this, AccessibleDescriptionVE(_snapshot) + trigger + battery);
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        double width = Bounds.Width, height = Bounds.Height;
        if (width < 120 || height < 160) return;
        bool sony = IsSonyVE();
        bool dualShock4 = UsesDualShock4VisualVE();
        PaletteVE p = PaletteVE.Create(this, !sony || dualShock4);
        ExInState state = _snapshot?.CorrectedState ?? default;
        ExInCalibrationProgress progress = ProgressVE();
        DrawHeaderVE(dc, p, sony, progress, width);
        const double stripHeight = 26;
        Rect stage = new(8, 29, width - 16, Math.Max(190, height - 48 - stripHeight));
        if (sony) DrawSonyVE(dc, stage, state, progress, p);
        else DrawXboxVE(dc, stage, state, progress, p);
        DrawPressedStripVE(dc, new Rect(8, stage.Bottom + 4, stage.Width, stripHeight), state.Buttons, sony, p);
    }

    private void DrawXboxVE(DrawingContext dc, Rect stage, ExInState state, ExInCalibrationProgress progress, PaletteVE p)
    {
        string target = NextTargetVE(progress);
        if (XboxCalibrationImageVE.Value is { } xboxImage)
        {
            Rect frontBounds = DrawControllerArtworkVE(dc, stage, xboxImage, state, p, "LT", "RT");
            DrawXboxImageTelemetryVE(dc, frontBounds, state, progress, p);
            return;
        }

        Rect body = FitVE(stage, 600, 235);
        double s = body.Width / 600;
        dc.DrawGeometry(p.Surface, new Pen(p.Border, Math.Max(1, 2 * s)), XboxShellVE(body));
        DrawTriggerVE(dc, body, .12, "LT", state.LeftTrigger, progress.LeftTrigger, target == "LeftTrigger", p, s);
        DrawTriggerVE(dc, body, .68, "RT", state.RightTrigger, progress.RightTrigger, target == "RightTrigger", p, s);
        DrawStickVE(dc, AtVE(body, .27, .40), state.LeftX, state.LeftY, progress.LeftStick, target == "LeftStick", p, s);
        DrawDPadVE(dc, AtVE(body, .34, .68), state.Buttons, p, s);
        DrawStickVE(dc, AtVE(body, .62, .66), state.RightX, state.RightY, progress.RightStick, target == "RightStick", p, s);
        DrawFaceVE(dc, AtVE(body, .77, .40), state.Buttons, false, p, s);
        DrawSmallButtonVE(dc, AtVE(body, .46, .47), state.Buttons.HasFlag(ExInButtons.Back), p, s);
        DrawSmallButtonVE(dc, AtVE(body, .55, .47), state.Buttons.HasFlag(ExInButtons.Start), p, s);
        DrawGuideVE(dc, AtVE(body, .50, .31), state.Buttons.HasFlag(ExInButtons.Guide), "X", p, s);
        DrawTriggerTravelCVE(dc, AtVE(body, .12, .96), state.LeftTrigger, p, 20 * s);
    }

    private sealed record ControllerArtworkVE(IImage Front, PixelSize FrontSize, IImage Side, PixelSize SideSize);

    private static readonly (ExInButtons Flag, string Xbox, string Sony)[] PressedLabelsVE =
    {
        (ExInButtons.South, "A", "×"),
        (ExInButtons.East, "B", "○"),
        (ExInButtons.West, "X", "□"),
        (ExInButtons.North, "Y", "△"),
        (ExInButtons.LeftShoulder, "LB", "L1"),
        (ExInButtons.RightShoulder, "RB", "R1"),
        (ExInButtons.LeftStick, "L3", "L3"),
        (ExInButtons.RightStick, "R3", "R3"),
        (ExInButtons.Back, "Back", "Share"),
        (ExInButtons.Start, "Start", "Options"),
        (ExInButtons.Guide, "Guide", "PS"),
        (ExInButtons.Touchpad, "Touchpad", "Touchpad"),
        (ExInButtons.DPadUp, "↑", "↑"),
        (ExInButtons.DPadDown, "↓", "↓"),
        (ExInButtons.DPadLeft, "←", "←"),
        (ExInButtons.DPadRight, "→", "→"),
        (ExInButtons.P1, "P1", "P1"),
        (ExInButtons.P2, "P2", "P2"),
        (ExInButtons.P3, "P3", "P3"),
        (ExInButtons.P4, "P4", "P4"),
    };

    private void DrawPressedStripVE(DrawingContext dc, Rect area, ExInButtons buttons, bool sony, PaletteVE p)
    {
        var names = new List<string>();
        foreach (var entry in PressedLabelsVE)
            if (buttons.HasFlag(entry.Flag))
                names.Add(sony ? entry.Sony : entry.Xbox);

        bool none = names.Count == 0;
        string text = none ? "Ningún botón pulsado" : "Pulsados: " + string.Join(" · ", names);
        DrawTextVE(dc, text, area.Left + area.Width / 2, area.Top + 4, 12,
            none ? p.Muted : p.Accent, FontWeight.SemiBold, TextAlignment.Center);
    }

    private static ControllerArtworkVE? LoadCalibrationImageVE(string fileName, int frontWidth,
        int frontTop, int frontBottom, int sideLeft, int sideTop, int sideBottom)
    {
        try
        {
            var image = new Bitmap(LinuxTheme.GetAssetPath(fileName));
            var frontRect = new PixelRect(0, frontTop, frontWidth, frontBottom - frontTop);
            var sideRect = new PixelRect(sideLeft, sideTop, image.PixelSize.Width - sideLeft, sideBottom - sideTop);
            return new(new CroppedBitmap(image, frontRect), frontRect.Size,
                new CroppedBitmap(image, sideRect), sideRect.Size);
        }
        catch
        {
            return null;
        }
    }

    private static void DrawImageVE(DrawingContext dc, IImage image, PixelSize size, Rect destination)
        => dc.DrawImage(image, new Rect(0, 0, size.Width, size.Height), destination);

    private Rect DrawControllerArtworkVE(DrawingContext dc, Rect stage, ControllerArtworkVE artwork,
        ExInState state, PaletteVE p, string leftTrigger, string rightTrigger)
    {
        const double gap = 8;
        double frontHeight = stage.Height * .61;
        Rect frontSlot = new(stage.Left, stage.Top, stage.Width, frontHeight);
        Rect frontBounds = FitVE(frontSlot, artwork.FrontSize.Width, artwork.FrontSize.Height);
        DrawImageVE(dc, artwork.Front, artwork.FrontSize, frontBounds);

        double sideTop = frontSlot.Bottom + gap;
        double sideWidth = (stage.Width - gap) / 2;
        double imageTop = sideTop + 18;
        double imageHeight = Math.Max(1, stage.Bottom - imageTop);
        Rect leftSlot = new(stage.Left, imageTop, sideWidth, imageHeight);
        Rect rightSlot = new(stage.Left + sideWidth + gap, imageTop, sideWidth, imageHeight);
        Rect leftBounds = FitVE(leftSlot, artwork.SideSize.Width, artwork.SideSize.Height);
        Rect rightBounds = FitVE(rightSlot, artwork.SideSize.Width, artwork.SideSize.Height);

        DrawTextVE(dc, $"LATERAL IZQUIERDO · {leftTrigger}", leftSlot.Left + sideWidth / 2,
            sideTop, 10, p.Muted, FontWeight.SemiBold, TextAlignment.Center);
        DrawTextVE(dc, $"LATERAL DERECHO · {rightTrigger}", rightSlot.Left + sideWidth / 2,
            sideTop, 10, p.Muted, FontWeight.SemiBold, TextAlignment.Center);

        double mirrorX = leftBounds.Left + leftBounds.Width / 2;
        using (dc.PushTransform(Matrix.CreateTranslation(-mirrorX, 0) * Matrix.CreateScale(-1, 1) *
                                Matrix.CreateTranslation(mirrorX, 0)))
        {
            DrawImageVE(dc, artwork.Side, artwork.SideSize, leftBounds);
            DrawTriggerTravelArcVE(dc, leftBounds, state.LeftTrigger, p, .32, .49, .32, .39);
        }

        DrawImageVE(dc, artwork.Side, artwork.SideSize, rightBounds);
        DrawTriggerTravelArcVE(dc, rightBounds, state.RightTrigger, p, .32, .49, .32, .39);
        return frontBounds;
    }

    private void DrawXboxImageTelemetryVE(DrawingContext dc, Rect imageBounds, ExInState state,
        ExInCalibrationProgress progress, PaletteVE p)
    {
        string target = NextTargetVE(progress);
        bool calibrating = _snapshot?.Calibrating == true;
        double markerRadius = Math.Max(18, Math.Min(imageBounds.Width, imageBounds.Height) * .075);
        DrawImageStickVE(dc, AtVE(imageBounds, .233, .300), state.LeftX, state.LeftY, progress.LeftStick,
            target == "LeftStick", string.Empty, markerRadius, calibrating, p);
        DrawImageStickVE(dc, AtVE(imageBounds, .606, .483), state.RightX, state.RightY, progress.RightStick,
            target == "RightStick", string.Empty, markerRadius, calibrating, p);
    }

    private void DrawImageStickVE(DrawingContext dc, Point center, short x, short y, int coverage,
        bool active, string label, double radius, bool calibrating, PaletteVE p)
    {
        IBrush indicator = active && calibrating ? p.Warning : p.Accent;
        double ring = radius * 1.18;
        dc.DrawEllipse(null, new Pen(indicator, Math.Max(2, radius * .10)), center, ring, ring);
        DrawCoverageArcVE(dc, center, ring + radius * .18, coverage, indicator, p, Math.Max(.75, radius / 31));
        double dx = Math.Clamp(x / 32767d, -1, 1) * radius * .57;
        double dy = Math.Clamp(y / 32767d, -1, 1) * radius * .57;
        Point position = new(center.X + dx, center.Y + dy);
        dc.DrawEllipse(indicator, null, position, Math.Max(3, radius * .16), Math.Max(3, radius * .16));
        if (!string.IsNullOrEmpty(label))
            DrawTextVE(dc, $"{label} · {Math.Clamp(coverage, 0, 100)}%", center.X, center.Y + ring + radius * .26,
                Math.Max(8, radius * .27), indicator, FontWeight.Bold, TextAlignment.Center);
    }

    private void DrawSonyVE(DrawingContext dc, Rect stage, ExInState state, ExInCalibrationProgress progress, PaletteVE p)
    {
        if (UsesDualShock4VisualVE() && DualShock4CalibrationImageVE.Value is { } sonyImage)
        {
            Rect imageBounds = DrawControllerArtworkVE(dc, stage, sonyImage, state, p, "L2", "R2");
            double radius = Math.Max(18, Math.Min(imageBounds.Width, imageBounds.Height) * .075);
            DrawImageStickVE(dc, AtVE(imageBounds, .338, .535), state.LeftX, state.LeftY,
                progress.LeftStick, NextTargetVE(progress) == "LeftStick", string.Empty, radius,
                _snapshot?.Calibrating == true, p);
            DrawImageStickVE(dc, AtVE(imageBounds, .643, .535), state.RightX, state.RightY,
                progress.RightStick, NextTargetVE(progress) == "RightStick", string.Empty, radius,
                _snapshot?.Calibrating == true, p);
            return;
        }

        Rect body = FitVE(stage, 600, 235);
        double s = body.Width / 600;
        string target = NextTargetVE(progress);
        dc.DrawGeometry(p.Surface, new Pen(p.Border, Math.Max(1, 2 * s)), SonyShellVE(body));
        DrawTriggerVE(dc, body, .11, "L2", state.LeftTrigger, progress.LeftTrigger, target == "LeftTrigger", p, s);
        DrawTriggerVE(dc, body, .69, "R2", state.RightTrigger, progress.RightTrigger, target == "RightTrigger", p, s);
        DrawDPadVE(dc, AtVE(body, .27, .42), state.Buttons, p, s);
        DrawFaceVE(dc, AtVE(body, .74, .42), state.Buttons, true, p, s);
        DrawStickVE(dc, AtVE(body, .40, .68), state.LeftX, state.LeftY, progress.LeftStick, target == "LeftStick", p, s);
        DrawStickVE(dc, AtVE(body, .60, .68), state.RightX, state.RightY, progress.RightStick, target == "RightStick", p, s);
        Rect touchpad = new(body.Left + body.Width * .39, body.Top + body.Height * .16, body.Width * .22, body.Height * .24);
        dc.DrawRectangle(state.Buttons.HasFlag(ExInButtons.Touchpad) ? p.AccentSoft : p.SurfaceRaised,
            new Pen(state.Buttons.HasFlag(ExInButtons.Touchpad) ? p.Accent : p.Border, Math.Max(1, 1.4 * s)),
            touchpad, 7 * s, 7 * s);
        DrawTextVE(dc, "TOUCHPAD", touchpad.Left + touchpad.Width / 2, touchpad.Top + 7 * s,
            8 * s, p.Muted, FontWeight.SemiBold, TextAlignment.Center);
        DrawSmallButtonVE(dc, AtVE(body, .35, .38), state.Buttons.HasFlag(ExInButtons.Back), p, s);
        DrawSmallButtonVE(dc, AtVE(body, .65, .38), state.Buttons.HasFlag(ExInButtons.Start), p, s);
        DrawGuideVE(dc, AtVE(body, .50, .53), state.Buttons.HasFlag(ExInButtons.Guide), "PS", p, s);
        if (UsesDualShock4VisualVE())
            DrawTriggerTravelCVE(dc, AtVE(body, .11, .96), state.LeftTrigger, p, 20 * s);
    }

    private static void DrawTriggerTravelCVE(DrawingContext dc, Point center, short raw, PaletteVE p, double radius)
    {
        const double startAngle = 50;
        const double sweepAngle = 260;
        double stroke = Math.Clamp(radius * .18, 3, 5);
        DrawOpenArcVE(dc, center, radius, startAngle, sweepAngle, p.Border, stroke);
        double fraction = Math.Clamp(Math.Max(0, (int)raw) / 32767d, 0, 1);
        if (fraction > 0)
        {
            DrawOpenArcVE(dc, center, radius, startAngle, sweepAngle * fraction, p.Accent, stroke);
            dc.DrawEllipse(p.Accent, null, PolarVE(center, radius, startAngle + sweepAngle * fraction),
                stroke * .7, stroke * .7);
        }
    }

    private static void DrawTriggerTravelArcVE(DrawingContext dc, Rect imageBounds, short raw, PaletteVE p,
        double centerX, double centerY, double radiusX, double radiusY)
    {
        Point center = AtVE(imageBounds, centerX, centerY);
        double rx = imageBounds.Width * radiusX;
        double ry = imageBounds.Height * radiusY;
        const double curve = .5522847498;
        Point start = new(center.X - rx, center.Y);
        Point control1 = new(center.X - rx, center.Y + ry * curve);
        Point control2 = new(center.X - rx * curve, center.Y + ry);
        Point end = new(center.X, center.Y + ry);
        double stroke = Math.Clamp(imageBounds.Width * .016, 6, 28);

        DrawTriggerCurveVE(dc, start, control1, control2, end, p.Border, stroke);
        double fraction = Math.Clamp(Math.Max(0, (int)raw) / 32767d, 0, 1);
        if (fraction <= 0) return;

        Point first = LerpVE(start, control1, fraction);
        Point second = LerpVE(control1, control2, fraction);
        Point third = LerpVE(control2, end, fraction);
        Point fourth = LerpVE(first, second, fraction);
        Point fifth = LerpVE(second, third, fraction);
        Point tip = LerpVE(fourth, fifth, fraction);
        DrawTriggerCurveVE(dc, start, first, fourth, tip, p.Accent, stroke);
        dc.DrawEllipse(p.Accent, null, tip, stroke * .55, stroke * .55);
    }

    private static Point LerpVE(Point from, Point to, double fraction) =>
        new(from.X + (to.X - from.X) * fraction, from.Y + (to.Y - from.Y) * fraction);

    private static void DrawTriggerCurveVE(DrawingContext dc, Point start, Point control1,
        Point control2, Point end, IBrush brush, double stroke)
    {
        var path = new StreamGeometry();
        using (StreamGeometryContext context = path.Open())
        {
            context.BeginFigure(start, false);
            context.CubicBezierTo(control1, control2, end);
            context.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(brush, stroke, lineCap: PenLineCap.Round), path);
    }

    private static void DrawOpenArcVE(DrawingContext dc, Point center, double radius,
        double startAngle, double sweepAngle, IBrush brush, double thickness)
    {
        if (sweepAngle <= 0) return;
        var arc = new StreamGeometry();
        using (StreamGeometryContext c = arc.Open())
        {
            c.BeginFigure(PolarVE(center, radius, startAngle), false);
            c.ArcTo(PolarVE(center, radius, startAngle + Math.Min(359.9, sweepAngle)),
                new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise);
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(brush, thickness, lineCap: PenLineCap.Round), arc);
    }

    private void DrawHeaderVE(DrawingContext dc, PaletteVE p, bool sony, ExInCalibrationProgress progress, double width)
    {
        string family = _snapshot?.Device is null ? "ESPERANDO MANDO" : ControllerLabelVE(sony);
        DrawTextVE(dc, family, 0, 2, 11, p.Text, FontWeight.Bold);
        string state = _snapshot?.Calibrating == true ? progress.Complete
            ? "RECORRIDO COMPLETO · GUARDA"
            : $"PASO {CalibrationStepVE(progress)}/4 · {TargetLabelVE(NextTargetVE(progress))}" :
            _snapshot?.Calibrated == true ? "PERFIL ACTIVO" : "LISTO PARA CALIBRAR";
        DrawTextVE(dc, state, width, 2, 11, _snapshot?.Calibrating == true ? p.Warning : p.Muted,
            FontWeight.SemiBold, TextAlignment.Right);
    }

    private void DrawStickVE(DrawingContext dc, Point center, short x, short y, int coverage, bool active, PaletteVE p, double s)
    {
        double outer = 31 * s, inner = 19 * s;
        dc.DrawEllipse(p.SurfaceRaised, new Pen(p.Border, Math.Max(1, 2 * s)), center, outer, outer);
        if (active && _snapshot?.Calibrating == true)
            dc.DrawEllipse(null, new Pen(p.Warning, Math.Max(2, 3 * s)), center, outer + 7 * s, outer + 7 * s);
        DrawCoverageArcVE(dc, center, outer + 4 * s, coverage, active ? p.Warning : p.Accent, p, s);
        double dx = Math.Clamp(x / 32767d, -1, 1) * (outer - inner) * .9;
        double dy = Math.Clamp(y / 32767d, -1, 1) * (outer - inner) * .9;
        Point knob = new(center.X + dx, center.Y + dy);
        dc.DrawEllipse(p.AccentSoft, new Pen(p.Accent, Math.Max(1, 1.5 * s)), knob, inner, inner);
        dc.DrawEllipse(p.Accent, null, knob, 4 * s, 4 * s);
    }

    private static void DrawCoverageArcVE(DrawingContext dc, Point center, double radius, int coverage, IBrush color, PaletteVE p, double s)
    {
        double fraction = Math.Clamp(coverage / 100d, 0, 1);
        dc.DrawEllipse(null, new Pen(p.Border, Math.Max(1, 2 * s)), center, radius, radius);
        if (fraction <= 0) return;
        Point startPoint = PolarVE(center, radius, -90);
        Point endPoint = PolarVE(center, radius, -90 + 359.9 * fraction);
        var arc = new StreamGeometry();
        using (StreamGeometryContext c = arc.Open())
        {
            c.BeginFigure(startPoint, false);
            c.ArcTo(endPoint, new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise);
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(color, Math.Max(2, 3 * s)), arc);
    }

    private void DrawTriggerVE(DrawingContext dc, Rect body, double px, string label, short raw, int coverage, bool active, PaletteVE p, double s)
    {
        double x = body.Left + body.Width * px, y = body.Top + 9 * s, width = body.Width * .19;
        double value = Math.Clamp(Math.Max(0, (int)raw) / 32767d, 0, 1);
        DrawTextVE(dc, $"{label}  {value.ToString("P0", CultureInfo.CurrentCulture)}", x, y, 9 * s, p.Text, FontWeight.Bold);
        DrawTrackVE(dc, new Rect(x, y + 17 * s, width, 9 * s), value, p.Border, active ? p.Warning : p.Accent);
        DrawTextVE(dc, $"{Math.Clamp(coverage, 0, 100)}% recorrido", x + width, y,
            8 * s, active && _snapshot?.Calibrating == true ? p.Warning : p.Muted, FontWeight.SemiBold, TextAlignment.Right);
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
        => dc.DrawRectangle(pressed ? p.Accent : p.SurfaceRaised,
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
        DrawTextVE(dc, label, c.X, c.Y - 7 * s, 10 * s, p.Text, FontWeight.Bold, TextAlignment.Center);
    }

    private static void DrawSmallButtonVE(DrawingContext dc, Point c, bool pressed, PaletteVE p, double s)
        => dc.DrawRectangle(pressed ? p.Accent : p.SurfaceRaised,
            new Pen(pressed ? p.Accent : p.Border, Math.Max(1, s)),
            new Rect(c.X - 8 * s, c.Y - 5 * s, 16 * s, 10 * s), 5 * s, 5 * s);

    private void DrawGuideVE(DrawingContext dc, Point c, bool pressed, string label, PaletteVE p, double s)
    {
        dc.DrawEllipse(pressed ? p.Accent : p.SurfaceRaised,
            new Pen(pressed ? p.Accent : p.Border, Math.Max(1, s)), c, 10 * s, 10 * s);
        DrawTextVE(dc, label, c.X, c.Y - 6 * s, 7 * s, p.Text, FontWeight.Bold, TextAlignment.Center);
    }

    private static void DrawTrackVE(DrawingContext dc, Rect r, double fraction, IBrush back, IBrush fill)
    {
        double radius = r.Height / 2;
        dc.DrawRectangle(back, null, r, radius, radius);
        double width = r.Width * Math.Clamp(fraction, 0, 1);
        if (width > 0) dc.DrawRectangle(fill, null, new Rect(r.X, r.Y, width, r.Height), radius, radius);
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
        var g = new StreamGeometry();
        using StreamGeometryContext c = g.Open();
        c.BeginFigure(new(x + w * shoulder, y + h * crown), true);
        c.CubicBezierTo(new(x + w * .08, y + h * .13), new(x + w * .01, y + h * .65), new(x + w * gripX, y + h * gripY));
        c.CubicBezierTo(new(x + w * .14, y + h), new(x + w * innerX, y + h * .77), new(x + w * .34, y + h * innerY));
        c.CubicBezierTo(new(x + w * .44, y + h * .70), new(x + w * .56, y + h * .70), new(x + w * .66, y + h * innerY));
        c.CubicBezierTo(new(x + w * (1 - innerX), y + h * .77), new(x + w * .86, y + h), new(x + w * (1 - gripX), y + h * gripY));
        c.CubicBezierTo(new(x + w * .99, y + h * .65), new(x + w * .92, y + h * .13), new(x + w * (1 - shoulder), y + h * crown));
        c.CubicBezierTo(new(x + w * .64, y), new(x + w * .36, y), new(x + w * shoulder, y + h * crown));
        c.EndFigure(true);
        return g;
    }

    private bool IsSonyVE() => _snapshot?.Device?.Identity?.Family == ExInControllerFamily.DualShock4 || _snapshot?.Device?.VendorId == 0x054C;
    private bool UsesDualShock4VisualVE() => IsSonyVE() && _snapshot?.Device?.ProductId != 0x0CE6;
    private string ControllerLabelVE(bool sony)
    {
        string family = sony ? _snapshot?.Device?.ProductId == 0x0CE6 ? "DUALSENSE" : "DUALSHOCK 4" :
            _snapshot?.Device?.Identity?.Family == ExInControllerFamily.Xbox ? "XBOX" : "MANDO GENÉRICO";
        return $"{family} · {_snapshot?.DeviceName}";
    }
    internal static string NextTargetVE(ExInCalibrationProgress progress)
        => progress.LeftStick < 90 ? "LeftStick" :
           progress.RightStick < 90 ? "RightStick" :
           progress.LeftTrigger < 90 ? "LeftTrigger" :
           progress.RightTrigger < 90 ? "RightTrigger" : "Complete";
    private static int CalibrationStepVE(ExInCalibrationProgress progress) => NextTargetVE(progress) switch
    {
        "LeftStick" => 1,
        "RightStick" => 2,
        "LeftTrigger" => 3,
        "RightTrigger" => 4,
        _ => 4
    };
    private static string TargetLabelVE(string target) => target switch
    {
        "LeftStick" => "STICK IZQUIERDO",
        "RightStick" => "STICK DERECHO",
        "LeftTrigger" => "GATILLO IZQUIERDO",
        "RightTrigger" => "GATILLO DERECHO",
        _ => "RECORRIDO COMPLETO"
    };
    private ExInCalibrationProgress ProgressVE() => _snapshot?.CalibrationProgress ??
        (_snapshot?.Calibrated == true ? new(100, 100, 100, 100) : new(0, 0, 0, 0));
    private static string AccessibleDescriptionVE(ExInLiveSnapshot? snapshot)
    {
        if (snapshot?.Device is null) return "No hay un mando detectado.";
        return snapshot.Calibrating ? $"Calibración dinámica al {snapshot.CalibrationProgress?.Overall ?? 0} por ciento." :
            snapshot.Calibrated ? "Perfil de calibración activo." : "Mando listo para calibrar.";
    }

    private void DrawTextVE(DrawingContext dc, string value, double x, double y, double size,
        IBrush brush, FontWeight weight, TextAlignment alignment = TextAlignment.Left)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle.Normal, weight), Math.Max(7, size), brush);
        // WPF anchors centered/right text at x without MaxTextWidth; place it explicitly here.
        double left = alignment switch
        {
            TextAlignment.Center => x - text.WidthIncludingTrailingWhitespace / 2,
            TextAlignment.Right => x - text.WidthIncludingTrailingWhitespace,
            _ => x
        };
        dc.DrawText(text, new Point(left, y));
    }

    private FontFamily FontFamily => (TopLevel.GetTopLevel(this) as Window)?.FontFamily ?? FontFamily.Default;

    private sealed record PaletteVE(IBrush Text, IBrush Muted, IBrush Border, IBrush Surface,
        IBrush SurfaceRaised, IBrush Accent, IBrush AccentSoft, IBrush Warning)
    {
        internal static PaletteVE Create(AvaloniaControl owner, bool sony)
        {
            IBrush Get(string key, Color fallback) =>
                owner.TryFindResource(key, owner.ActualThemeVariant, out object? value) && value is IBrush brush
                    ? brush
                    : new SolidColorBrush(fallback);
            if (!sony)
            {
                IBrush originalAccent = Get("BlueBrush", Color.FromRgb(13, 142, 157));
                return new(Get("TextBrush", Colors.White), Get("MutedBrush", Color.FromRgb(144, 157, 174)),
                    Get("BorderBrush", Color.FromRgb(53, 66, 84)), Get("PanelBrush2", Color.FromRgb(20, 27, 38)),
                    Get("PanelBrush", Color.FromRgb(25, 34, 47)), originalAccent,
                    new SolidColorBrush(Color.FromArgb(72, 13, 142, 157)),
                    Get("WarningBrush", Color.FromRgb(245, 158, 11)));
            }

            IBrush accent = new SolidColorBrush(Color.FromRgb(52, 85, 250));
            return new(Get("TextBrush", Colors.White), Get("MutedBrush", Color.FromRgb(144, 157, 174)),
                new SolidColorBrush(Color.FromRgb(114, 126, 148)), new SolidColorBrush(Color.FromRgb(24, 33, 52)),
                new SolidColorBrush(Color.FromRgb(42, 52, 71)), accent,
                new SolidColorBrush(Color.FromArgb(72, 52, 85, 250)),
                Get("WarningBrush", Color.FromRgb(245, 158, 11)));
        }
    }
}
