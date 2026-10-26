namespace NOVORA.LinkEngine.Device;

/// <summary>
/// Capacidades técnicas reutilizables entre unidades del mismo
/// modelo/build Android.
///
/// Este objeto NO contiene identidad del dispositivo.
/// </summary>
internal sealed record LEDeviceProfileModel
{
    public const int CurrentSchemaVersionLE =
        1;

    public int SchemaVersion { get; init; } =
        CurrentSchemaVersionLE;

    /// <summary>
    /// Clave técnica:
    ///
    /// manufacturer + model + build fingerprint + SDK + schema.
    ///
    /// No contiene serial ADB.
    /// </summary>
    public required string ModelKey { get; init; }

    public string Manufacturer { get; init; } =
        string.Empty;

    public string Model { get; init; } =
        string.Empty;

    public string BuildFingerprint { get; init; } =
        string.Empty;

    public string AndroidVersion { get; init; } =
        string.Empty;

    public int AndroidSdk { get; init; }

    public string PrimaryAbi { get; init; } =
        string.Empty;

    public IReadOnlyList<string> SupportedAbis { get; init; } =
        Array.Empty<string>();

    public bool SupportsH264 { get; init; }

    public bool SupportsH265 { get; init; }

    public bool SupportsAv1 { get; init; }

    public bool SupportsOpus { get; init; }

    public bool DisplayDetected { get; init; }

    public int NativeWidth { get; init; }

    public int NativeHeight { get; init; }

    public double MaxRefreshRateHz { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset LastUsedUtc { get; init; } =
        DateTimeOffset.UtcNow;

    /// <summary>
    /// Número de unidades/sesiones que reutilizaron este perfil.
    ///
    /// Sólo es una estadística local del perfil técnico.
    /// No permite saber qué dispositivos fueron.
    /// </summary>
    public long HitCount { get; init; }
}
