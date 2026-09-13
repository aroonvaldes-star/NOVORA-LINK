namespace NOVORA.Models;

/// <summary>
/// Perfil técnico de compatibilidad de un Android con NOVORA.
///
/// IMPORTANTE:
///
/// Este modelo contiene capacidades técnicas.
/// No contiene contraseñas, cuentas, correos, contactos ni
/// información personal del usuario.
///
/// Puede ser generado mediante un escaneo real o reconstruido
/// utilizando un perfil cacheado de modelo/build.
/// </summary>
public sealed record CompatibilityProfileDevice
{
    public static CompatibilityProfileDevice Unknown { get; } =
        new();

    // ============================================================
    // IDENTIDAD TÉCNICA
    // ============================================================

    public string Manufacturer { get; init; } =
        string.Empty;

    public string Model { get; init; } =
        string.Empty;

    public string AndroidVersion { get; init; } =
        string.Empty;

    public int AndroidSdk { get; init; }

    public string PrimaryAbi { get; init; } =
        string.Empty;

    public IReadOnlyList<string> SupportedAbis { get; init; } =
        Array.Empty<string>();


    // ============================================================
    // ARQUITECTURA
    // ============================================================

    public bool IsArm64 =>
        SupportedAbis.Any(
            abi =>
                string.Equals(
                    abi,
                    "arm64-v8a",
                    StringComparison.OrdinalIgnoreCase));

    public bool IsArm32 =>
        SupportedAbis.Any(
            abi =>
                string.Equals(
                    abi,
                    "armeabi-v7a",
                    StringComparison.OrdinalIgnoreCase));

    public bool IsX64 =>
        SupportedAbis.Any(
            abi =>
                string.Equals(
                    abi,
                    "x86_64",
                    StringComparison.OrdinalIgnoreCase));


    // ============================================================
    // LINKENGINE
    // ============================================================

    /// <summary>
    /// El cliente Android actual de LinkEngine parte de API 26.
    /// </summary>
    public bool MeetsLinkEngineAndroidVersion =>
        AndroidSdk >= 26;

    /// <summary>
    /// Para nuestra versión soportada de Android, VpnService forma
    /// parte de la plataforma.
    /// </summary>
    public bool SupportsVpnService =>
        AndroidSdk >= 26;


    // ============================================================
    // VISIONENGINE / CODECS
    // ============================================================

    public bool SupportsH264 { get; init; }

    public bool SupportsH265 { get; init; }

    public bool SupportsAv1 { get; init; }

    public bool SupportsOpus { get; init; }


    // ============================================================
    // DISPLAY
    // ============================================================

    public bool DisplayDetected { get; init; }

    public int NativeWidth { get; init; }

    public int NativeHeight { get; init; }

    public double MaxRefreshRateHz { get; init; }


    // ============================================================
    // COMPATIBILIDAD POR MOTOR
    // ============================================================

    public CompatibilityLevelDevice LinkEngine { get; init; } =
        CompatibilityLevelDevice.Unknown;

    public CompatibilityLevelDevice VisionEngine { get; init; } =
        CompatibilityLevelDevice.Unknown;


    // ============================================================
    // OVERALL
    // ============================================================

    public CompatibilityLevelDevice Overall
    {
        get
        {
            /*
             * LinkEngine es un requisito fundamental para esta
             * evaluación general.
             */
            if (LinkEngine ==
                CompatibilityLevelDevice.Unsupported)
            {
                return CompatibilityLevelDevice.Unsupported;
            }

            if (LinkEngine ==
                    CompatibilityLevelDevice.Unknown ||
                VisionEngine ==
                    CompatibilityLevelDevice.Unknown)
            {
                return CompatibilityLevelDevice.Unknown;
            }

            if (LinkEngine ==
                    CompatibilityLevelDevice.Full &&
                VisionEngine ==
                    CompatibilityLevelDevice.Full)
            {
                return CompatibilityLevelDevice.Full;
            }

            return CompatibilityLevelDevice.Partial;
        }
    }


    // ============================================================
    // LABELS
    // ============================================================

    public string LinkEngineLabel =>
        ToLabel(
            LinkEngine);

    public string VisionEngineLabel =>
        ToLabel(
            VisionEngine);

    public string OverallLabel =>
        ToLabel(
            Overall);


    // ============================================================
    // DISPLAY NAME
    // ============================================================

    public string DisplayName
    {
        get
        {
            string manufacturer =
                Manufacturer.Trim();

            string model =
                Model.Trim();

            if (string.IsNullOrWhiteSpace(
                manufacturer))
            {
                return string.IsNullOrWhiteSpace(
                    model)
                        ? "Android"
                        : model;
            }

            if (string.IsNullOrWhiteSpace(
                model))
            {
                return manufacturer;
            }

            /*
             * Evita:
             *
             * Samsung Samsung Galaxy...
             */
            if (model.StartsWith(
                manufacturer,
                StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }

            return $"{manufacturer} {model}";
        }
    }


    // ============================================================
    // CAPACIDADES FALTANTES
    // ============================================================

    public IReadOnlyList<string> MissingVisionCapabilities
    {
        get
        {
            List<string> missing =
                [];

            if (!DisplayDetected)
            {
                missing.Add(
                    "Display");
            }

            if (!SupportsH264)
            {
                missing.Add(
                    "H.264");
            }

            /*
             * H.265 no bloquea funcionamiento básico.
             */
            if (!SupportsH265)
            {
                missing.Add(
                    "H.265");
            }

            /*
             * AV1 es totalmente opcional.
             *
             * No lo utilizamos para declarar incompatible a un
             * dispositivo.
             */
            if (!SupportsAv1)
            {
                missing.Add(
                    "AV1");
            }

            if (!SupportsOpus)
            {
                missing.Add(
                    "Opus");
            }

            return missing;
        }
    }


    // ============================================================
    // RESUMEN
    // ============================================================

    public string Summary
    {
        get
        {
            string android =
                string.IsNullOrWhiteSpace(
                    AndroidVersion)
                    ? $"API {AndroidSdk}"
                    : $"Android {AndroidVersion} / API {AndroidSdk}";

            return
                $"{DisplayName} · " +
                $"{android} · " +
                $"LinkEngine {LinkEngineLabel} · " +
                $"VisionEngine {VisionEngineLabel}";
        }
    }


    private static string ToLabel(
        CompatibilityLevelDevice level)
    {
        return level switch
        {
            CompatibilityLevelDevice.Full =>
                "FULL",

            CompatibilityLevelDevice.Partial =>
                "PARTIAL",

            CompatibilityLevelDevice.Unsupported =>
                "UNSUPPORTED",

            _ =>
                "UNKNOWN"
        };
    }
}
