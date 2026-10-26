namespace NOVORA.ExInEngine;

internal static class ExInPointerSensitivity
{
    internal const double MinimumVE = 0.1;
    internal const double MaximumVE = 0.75;
    internal const double DefaultVE = 0.5;

    internal static void ValidateVE(double value)
    {
        if (!double.IsFinite(value) || value is < MinimumVE or > MaximumVE)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"La sensibilidad ExIn debe estar entre {MinimumVE:0.##} y {MaximumVE:0.##}.");
    }
}
