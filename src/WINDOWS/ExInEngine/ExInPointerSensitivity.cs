namespace NOVORA.ExInEngine;

internal static class ExInPointerSensitivity
{
    // Modo UI: el puntero es más lento que antes (antes 10–75 %, por defecto 50 %).
    internal const double MinimumVE = 0.05;
    internal const double MaximumVE = 0.40;
    internal const double DefaultVE = 0.20;

    internal static void ValidateVE(double value)
    {
        if (!double.IsFinite(value) || value is < MinimumVE or > MaximumVE)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"La sensibilidad ExIn debe estar entre {MinimumVE:0.##} y {MaximumVE:0.##}.");
    }
}
