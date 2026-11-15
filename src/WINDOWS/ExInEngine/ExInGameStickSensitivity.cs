namespace NOVORA.ExInEngine;

/// <summary>
/// Sensibilidad de los sticks en modo juego. Escala sólo los ejes enviados al mando
/// virtual; el puntero de modo juego conserva su propia sensibilidad.
/// 100 % equivale a la salida original.
/// </summary>
internal static class ExInGameStickSensitivity
{
    internal const double MinimumVE = 0.25;
    internal const double MaximumVE = 1.50;
    internal const double DefaultVE = 1.00;

    internal static void ValidateVE(double value)
    {
        if (!double.IsFinite(value) || value is < MinimumVE or > MaximumVE)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"La sensibilidad de sticks en modo juego debe estar entre {MinimumVE:0.##} y {MaximumVE:0.##}.");
    }

    internal static ExInState ScaleVE(ExInState state, double sensitivity)
    {
        if (sensitivity == DefaultVE) return state;

        return state with
        {
            LeftX = ScaleAxisVE(state.LeftX, sensitivity),
            LeftY = ScaleAxisVE(state.LeftY, sensitivity),
            RightX = ScaleAxisVE(state.RightX, sensitivity),
            RightY = ScaleAxisVE(state.RightY, sensitivity)
        };
    }

    private static short ScaleAxisVE(short value, double sensitivity) =>
        (short)Math.Clamp(Math.Round(value * sensitivity), short.MinValue, short.MaxValue);
}
