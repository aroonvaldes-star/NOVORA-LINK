namespace NOVORA.Models;

public sealed record MonitorInfo(
    string DeviceName,
    string FriendlyName,
    int Left,
    int Top,
    int Width,
    int Height,
    double RefreshRateHz,
    bool IsPrimary)
{
    // ============================================================
    // NOMBRE
    // ============================================================

    /// <summary>
    /// Nombre humano del monitor.
    ///
    /// Ejemplo:
    ///
    /// LG ULTRAGEAR
    /// Samsung Odyssey G5
    /// DELL U2723QE
    ///
    /// Si Windows no proporciona un nombre físico,
    /// MonitorService coloca aquí el mejor fallback disponible.
    /// </summary>
    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FriendlyName))
            {
                return FriendlyName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(DeviceName))
            {
                return DeviceName.Trim();
            }

            return "Monitor";
        }
    }

    // ============================================================
    // RESOLUCIÓN
    // ============================================================

    public string ResolutionLabel =>
        $"{Width}x{Height}";

    // ============================================================
    // FRECUENCIA
    // ============================================================

    public string RefreshRateLabel =>
        $"{RefreshRateHz:0.#} Hz";

    // ============================================================
    // ETIQUETA PRINCIPAL
    // ============================================================

    /// <summary>
    /// Texto mostrado en el ComboBox de MainWindow.
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            string primary =
                IsPrimary
                    ? " • Principal"
                    : string.Empty;

            return
                $"{Name} — " +
                $"{ResolutionLabel} @ {RefreshRateLabel}" +
                primary;
        }
    }

    // ============================================================
    // DESCRIPCIÓN DETALLADA
    // ============================================================

    public string IdentityDescription =>
        $"{Name} — " +
        $"{ResolutionLabel} @ {RefreshRateLabel}" +
        $" — {DeviceName}" +
        (IsPrimary
            ? " — Principal"
            : string.Empty);

    // ============================================================
    // STRING
    // ============================================================

    public override string ToString()
    {
        return DisplayLabel;
    }
}