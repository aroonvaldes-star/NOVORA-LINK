using System;
using System.Collections.Generic;

namespace NOVORA.Control;

/// <summary>
/// Proveedor de GPU normalizado. No consulta hardware: el llamador obtiene el
/// valor de Windows, Linux/macOS o de la telemetría de la PC.
/// </summary>
public enum NLAccelerationVendor
{
    Unknown,
    Nvidia,
    Intel,
    Amd,
    Apple
}

/// <summary>
/// Modos visibles para el usuario. Automático y Software son los perfiles
/// simplificados; Custom permite el perfil personalizado del usuario.
/// </summary>
public enum NLAccelerationMode
{
    Software,
    Automatic,
    Custom
}

/// <summary>
/// Perfil personalizado: proveedor y backend elegidos por el usuario. Sigue
/// limitado a los backends implementados en la plataforma correspondiente.
/// </summary>
public sealed record NLAccelerationCustomProfile(
    string Platform,
    NLAccelerationVendor Vendor,
    string Backend,
    string Name)
{
    public bool UsesHardware =>
        !string.Equals(Backend, "software", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Opción única ofrecida por un perfil. El backend es el nombre que debe usar
/// cada plataforma: cuda/qsv/vaapi en Linux, NvidiaNvdec/qsv/amf en Windows,
/// videotoolbox en macOS y software como fallback.
/// </summary>
public sealed record NLAccelerationChoice(
    NLAccelerationMode Mode,
    string Backend,
    string Platform,
    string Profile,
    string Reason)
{
    public bool UsesHardware =>
        Mode != NLAccelerationMode.Software &&
        !string.Equals(Backend, "software", StringComparison.OrdinalIgnoreCase);

    public string BackendLabel => Backend switch
    {
        "NvidiaNvdec" or "nvdec" or "cuda" => "NVDEC/CUDA",
        "qsv" or "Intel" => "QSV",
        "amf" or "Amd" => "AMF/VAAPI",
        "vaapi" => "VAAPI",
        "videotoolbox" => "VideoToolbox",
        _ => "Software"
    };

    public string Label => Mode switch
    {
        NLAccelerationMode.Custom => $"Personalizado · {BackendLabel}",
        NLAccelerationMode.Automatic => $"Automático · {BackendLabel}",
        _ => "Software"
    };
}

/// <summary>
/// Reduce la aceleración a una o dos opciones por perfil, en lugar de exponer
/// todas las APIs de FFmpeg disponibles. Los perfiles de ahorro/software y los
/// equipos sin proveedor conocido ofrecen una sola opción: software.
/// </summary>
public static class NLAccelerationProfile
{
    public static NLAccelerationVendor NormalizeVendor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return NLAccelerationVendor.Unknown;
        if (value.Contains("nvidia", StringComparison.OrdinalIgnoreCase)) return NLAccelerationVendor.Nvidia;
        if (value.Contains("intel", StringComparison.OrdinalIgnoreCase)) return NLAccelerationVendor.Intel;
        if (value.Contains("amd", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("ati", StringComparison.OrdinalIgnoreCase)) return NLAccelerationVendor.Amd;
        if (value.Contains("apple", StringComparison.OrdinalIgnoreCase)) return NLAccelerationVendor.Apple;
        return NLAccelerationVendor.Unknown;
    }

    public static string VendorName(NLAccelerationVendor vendor) => vendor switch
    {
        NLAccelerationVendor.Nvidia => "Nvidia",
        NLAccelerationVendor.Intel => "Intel",
        NLAccelerationVendor.Amd => "Amd",
        NLAccelerationVendor.Apple => "Apple",
        _ => "Unknown"
    };

    /// <summary>
    /// Backend hardware implementado para una combinación de proveedor y
    /// plataforma. Cadena vacía significa que no hay decodificación hardware
    /// implementada y el perfil debe usar software.
    /// </summary>
    public static string HardwareBackend(NLAccelerationVendor vendor, string platform)
    {
        bool windows = string.Equals(platform, "windows", StringComparison.OrdinalIgnoreCase);
        bool mac = string.Equals(platform, "mac", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(platform, "macos", StringComparison.OrdinalIgnoreCase);

        if (mac) return vendor == NLAccelerationVendor.Apple ? "videotoolbox" : string.Empty;

        if (windows)
        {
            return vendor switch
            {
                NLAccelerationVendor.Nvidia => "NvidiaNvdec",
                NLAccelerationVendor.Intel => "qsv",
                NLAccelerationVendor.Amd => "amf",
                _ => string.Empty
            };
        }

        return vendor switch
        {
            NLAccelerationVendor.Nvidia => "cuda",
            NLAccelerationVendor.Intel => "qsv",
            NLAccelerationVendor.Amd => "vaapi",
            _ => string.Empty
        };
    }

    public static IReadOnlyList<NLAccelerationVendor> VendorsFor(string platform)
    {
        bool mac = string.Equals(platform, "mac", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(platform, "macos", StringComparison.OrdinalIgnoreCase);
        return mac
            ? [NLAccelerationVendor.Apple]
            : [NLAccelerationVendor.Nvidia, NLAccelerationVendor.Intel, NLAccelerationVendor.Amd];
    }

    public static IReadOnlyList<string> BackendsFor(NLAccelerationVendor vendor, string platform)
    {
        string hardware = HardwareBackend(vendor, platform);
        return string.IsNullOrEmpty(hardware)
            ? ["software"]
            : [hardware, "software"];
    }

    /// <summary>Normaliza alias del usuario al backend canónico de la plataforma.</summary>
    public static string NormalizeBackend(string platform, NLAccelerationVendor vendor, string? backend)
    {
        string hardware = HardwareBackend(vendor, platform);
        string value = backend?.Trim() ?? string.Empty;
        if (value.Length == 0) return hardware;
        if (value.Equals("software", StringComparison.OrdinalIgnoreCase)) return "software";
        if (hardware.Length == 0) return string.Empty;
        if (value.Equals(hardware, StringComparison.OrdinalIgnoreCase)) return hardware;

        bool sameVendor = hardware switch
        {
            "NvidiaNvdec" => value.Equals("cuda", StringComparison.OrdinalIgnoreCase) ||
                             value.Equals("nvdec", StringComparison.OrdinalIgnoreCase),
            "qsv" => value.Equals("intel", StringComparison.OrdinalIgnoreCase) ||
                     value.Equals("qsv", StringComparison.OrdinalIgnoreCase),
            "amf" => value.Equals("amd", StringComparison.OrdinalIgnoreCase) ||
                     value.Equals("amf", StringComparison.OrdinalIgnoreCase),
            "vaapi" => value.Equals("amd", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("vaapi", StringComparison.OrdinalIgnoreCase),
            "videotoolbox" => value.Equals("apple", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("videotoolbox", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        return sameVendor ? hardware : string.Empty;
    }

    public static NLAccelerationCustomProfile? CreateCustom(
        string platform,
        string? vendor,
        string? backend)
    {
        NLAccelerationVendor parsed = NormalizeVendor(vendor);
        if (parsed == NLAccelerationVendor.Unknown) return null;
        string resolved = NormalizeBackend(platform, parsed, backend);
        if (resolved.Length == 0) return null;
        return new NLAccelerationCustomProfile(
            platform,
            parsed,
            resolved,
            $"Personalizado · {VendorName(parsed)}");
    }

    /// <summary>
    /// Los perfiles de ahorro y el perfil Software nunca solicitan hardware.
    /// El resto puede solicitar el backend del proveedor cuando existe.
    /// </summary>
    public static bool AllowsHardware(string profile)
    {
        string value = string.IsNullOrWhiteSpace(profile) ? "Automatic" : profile.Trim();
        return value is not ("Software" or "MobileExtreme" or "MobileEconomy");
    }

    public static IReadOnlyList<NLAccelerationChoice> OptionsFor(
        string platform,
        string vendor,
        string profile,
        NLAccelerationCustomProfile? custom = null)
    {
        string normalizedProfile = string.IsNullOrWhiteSpace(profile) ? "Automatic" : profile.Trim();

        if (custom is not null)
        {
            var customOptions = new List<NLAccelerationChoice>(2);
            if (custom.UsesHardware)
            {
                customOptions.Add(new NLAccelerationChoice(
                    NLAccelerationMode.Custom,
                    custom.Backend,
                    platform,
                    normalizedProfile,
                    $"Perfil personalizado: {VendorName(custom.Vendor)} en {platform} con {custom.Backend}."));
            }
            customOptions.Add(new NLAccelerationChoice(
                NLAccelerationMode.Software,
                "software",
                platform,
                normalizedProfile,
                "Perfil personalizado: decodificación por software."));
            return customOptions;
        }

        NLAccelerationVendor parsedVendor = NormalizeVendor(vendor);
        string backend = HardwareBackend(parsedVendor, platform);
        bool softwareOnly = !AllowsHardware(normalizedProfile) || string.IsNullOrEmpty(backend);

        var options = new List<NLAccelerationChoice>(2);
        if (!softwareOnly)
        {
            options.Add(new NLAccelerationChoice(
                NLAccelerationMode.Automatic,
                backend,
                platform,
                normalizedProfile,
                $"Hardware {VendorName(parsedVendor)} en {platform}: {backend}."));
        }

        options.Add(new NLAccelerationChoice(
            NLAccelerationMode.Software,
            "software",
            platform,
            normalizedProfile,
            softwareOnly && !string.IsNullOrEmpty(backend)
                ? "Perfil de ahorro o software: se usa decodificación por software."
                : $"Sin backend hardware implementado para {VendorName(parsedVendor)} en {platform}; se usa software."));

        return options;
    }

    /// <summary>
    /// Selección que deben aplicar los clientes al iniciar VisionEngine.
    /// Automático elige hardware solo cuando el perfil lo permite y el
    /// proveedor tiene un backend implementado; si no, elige software.
    /// </summary>
    public static NLAccelerationChoice Select(
        string platform,
        string vendor,
        string profile,
        NLAccelerationMode requestedMode,
        NLAccelerationCustomProfile? custom = null)
    {
        IReadOnlyList<NLAccelerationChoice> options =
            OptionsFor(platform, vendor, profile, custom);
        // Custom without a valid user profile has nothing to apply; never reuse
        // the automatic hardware option in that case.
        if (requestedMode == NLAccelerationMode.Custom && custom is null)
            return options[^1];
        if (requestedMode != NLAccelerationMode.Software && options.Count == 2)
            return options[0];
        return options[^1];
    }
}
