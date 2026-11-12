namespace NOVORA.ExInEngine;

public static class ExInMouseReport
{
    public const int ReportSizeVE = 5;

    public static byte[] BuildVE(ExInPointerReport report)
        => [report.Buttons, unchecked((byte)report.X), unchecked((byte)report.Y),
            unchecked((byte)report.Wheel), unchecked((byte)report.HorizontalWheel)];
}
