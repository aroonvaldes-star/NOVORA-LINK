using NOVORA.Models;
using NOVORA.Services;
using System.Globalization;

namespace NOVORA.LinkEngine.Device;

/// <summary>
/// Resuelve capacidades utilizando cache por modelo/build.
///
/// Primera unidad:
///     identity scan -> full scan -> save model profile.
///
/// Otra unidad idéntica:
///     identity scan -> model cache hit.
///
/// No persiste DeviceSerial.
/// </summary>
internal sealed class ResolverDeviceLE
{
    private const int MinimumAndroidSdkLE =
        26;

    private readonly AdbService _adb;

    private readonly HistoryDeviceLE _history;

    internal ResolverDeviceLE(
        AdbService adb,
        HistoryDeviceLE? history = null)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));

        _history =
            history ??
            new HistoryDeviceLE();
    }

    internal async Task<ResultResolverDeviceLE> ResolveAsync(
        string serial,
        DeviceCapabilities knownDisplayCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        knownDisplayCapabilities ??=
            DeviceCapabilities.Unknown;

        /*
         * ===========================================================
         * LIGHT IDENTITY SCAN
         * ===========================================================
         *
         * Son las únicas propiedades necesarias para saber si podemos
         * reutilizar el perfil técnico de otra unidad.
         */

        Task<string> manufacturerTask =
            SafeShellAsync(
                serial,
                "getprop ro.product.manufacturer",
                cancellationToken);

        Task<string> modelTask =
            SafeShellAsync(
                serial,
                "getprop ro.product.model",
                cancellationToken);

        Task<string> fingerprintTask =
            SafeShellAsync(
                serial,
                "getprop ro.build.fingerprint",
                cancellationToken);

        Task<string> sdkTask =
            SafeShellAsync(
                serial,
                "getprop ro.build.version.sdk",
                cancellationToken);

        Task<string> versionTask =
            SafeShellAsync(
                serial,
                "getprop ro.build.version.release",
                cancellationToken);

        await Task.WhenAll(
                manufacturerTask,
                modelTask,
                fingerprintTask,
                sdkTask,
                versionTask)
            .ConfigureAwait(false);

        string manufacturer =
            (await manufacturerTask.ConfigureAwait(false))
                .Trim();

        string model =
            (await modelTask.ConfigureAwait(false))
                .Trim();

        string fingerprint =
            (await fingerprintTask.ConfigureAwait(false))
                .Trim();

        string androidVersion =
            (await versionTask.ConfigureAwait(false))
                .Trim();

        string sdkText =
            (await sdkTask.ConfigureAwait(false))
                .Trim();

        int sdk =
            int.TryParse(
                sdkText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedSdk)
                    ? parsedSdk
                    : 0;

        /*
         * Si ni siquiera conseguimos fingerprint, no compartimos
         * capacidades entre unidades.
         *
         * Es preferible escanear nuevamente que reutilizar un perfil
         * potencialmente incorrecto.
         */

        bool canUseSharedModelCache =
            !string.IsNullOrWhiteSpace(
                manufacturer) &&
            !string.IsNullOrWhiteSpace(
                model) &&
            !string.IsNullOrWhiteSpace(
                fingerprint) &&
            sdk > 0;

        string modelKey =
            canUseSharedModelCache
                ? HistoryDeviceLE.CreateModelKeyLE(
                    manufacturer,
                    model,
                    fingerprint,
                    sdk)
                : string.Empty;

        if (canUseSharedModelCache)
        {
            ProfileModelDeviceLE? cached =
                await _history.GetAsync(
                        modelKey,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (cached is not null)
            {
                /*
                 * ===================================================
                 * MODEL CACHE HIT
                 * ===================================================
                 *
                 * Este teléfono puede ser físicamente otra unidad.
                 *
                 * Eso no importa:
                 *
                 * las capacidades cacheadas pertenecen al
                 * MODELO + BUILD, no al serial.
                 */

                CompatibilityProfileDevice profile =
                    ToCompatibilityProfileLE(
                        cached);

                return new ResultResolverDeviceLE
                {
                    Profile =
                        profile,

                    Source =
                        SourceProfileDeviceLE.ModelCache,

                    ModelKey =
                        modelKey,

                    SharedProfileHit =
                        true
                };
            }
        }

        /*
         * ===========================================================
         * FULL SCAN
         * ===========================================================
         *
         * Sólo llegamos aquí cuando:
         *
         * - nunca vimos este modelo/build;
         * - Android fue actualizado;
         * - no pudimos obtener una identidad técnica fiable.
         */

        string primaryAbi =
            await SafeShellAsync(
                    serial,
                    "getprop ro.product.cpu.abi",
                    cancellationToken)
                .ConfigureAwait(false);

        string abiList =
            await SafeShellAsync(
                    serial,
                    "getprop ro.product.cpu.abilist",
                    cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<string> supportedAbis =
            ParseAbisLE(
                primaryAbi,
                abiList);

        string codecDump =
            await SafeShellAsync(
                    serial,
                    "dumpsys media.codec",
                    cancellationToken)
                .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(
            codecDump))
        {
            codecDump =
                await SafeShellAsync(
                        serial,
                        "cmd media.codec list",
                        cancellationToken)
                    .ConfigureAwait(false);
        }

        bool h264 =
            ContainsLE(
                codecDump,
                "video/avc",
                "avc",
                "h264");

        bool h265 =
            ContainsLE(
                codecDump,
                "video/hevc",
                "hevc",
                "h265");

        bool av1 =
            ContainsLE(
                codecDump,
                "video/av01",
                "video/av1",
                "av01",
                "av1");

        bool opus =
            ContainsLE(
                codecDump,
                "audio/opus",
                "opus");

        CompatibilityLevelDevice linkEngine =
            EvaluateLinkEngineLE(
                sdk,
                supportedAbis);

        CompatibilityLevelDevice visionEngine =
            EvaluateVisionEngineLE(
                sdk,
                knownDisplayCapabilities,
                h264,
                h265,
                opus);

        CompatibilityProfileDevice newProfile =
            new()
            {
                Manufacturer =
                    manufacturer,

                Model =
                    model,

                AndroidVersion =
                    androidVersion,

                AndroidSdk =
                    sdk,

                PrimaryAbi =
                    primaryAbi.Trim(),

                SupportedAbis =
                    supportedAbis,

                SupportsH264 =
                    h264,

                SupportsH265 =
                    h265,

                SupportsAv1 =
                    av1,

                SupportsOpus =
                    opus,

                DisplayDetected =
                    knownDisplayCapabilities.IsDetected,

                NativeWidth =
                    knownDisplayCapabilities.NativeWidth,

                NativeHeight =
                    knownDisplayCapabilities.NativeHeight,

                MaxRefreshRateHz =
                    knownDisplayCapabilities.MaxRefreshRateHz,

                LinkEngine =
                    linkEngine,

                VisionEngine =
                    visionEngine
            };

        /*
         * Sólo persistimos si la identidad de modelo/build es fiable.
         */

        if (canUseSharedModelCache)
        {
            ProfileModelDeviceLE modelProfile =
                new()
                {
                    ModelKey =
                        modelKey,

                    Manufacturer =
                        manufacturer,

                    Model =
                        model,

                    BuildFingerprint =
                        fingerprint,

                    AndroidVersion =
                        androidVersion,

                    AndroidSdk =
                        sdk,

                    PrimaryAbi =
                        primaryAbi.Trim(),

                    SupportedAbis =
                        supportedAbis,

                    SupportsH264 =
                        h264,

                    SupportsH265 =
                        h265,

                    SupportsAv1 =
                        av1,

                    SupportsOpus =
                        opus,

                    DisplayDetected =
                        knownDisplayCapabilities.IsDetected,

                    NativeWidth =
                        knownDisplayCapabilities.NativeWidth,

                    NativeHeight =
                        knownDisplayCapabilities.NativeHeight,

                    MaxRefreshRateHz =
                        knownDisplayCapabilities.MaxRefreshRateHz
                };

            await _history.SaveAsync(
                    modelProfile,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new ResultResolverDeviceLE
        {
            Profile =
                newProfile,

            Source =
                SourceProfileDeviceLE.FullScan,

            ModelKey =
                modelKey,

            SharedProfileHit =
                false
        };
    }

    private async Task<string> SafeShellAsync(
        string serial,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _adb.ShellAsync(
                    serial,
                    command,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static CompatibilityProfileDevice ToCompatibilityProfileLE(
        ProfileModelDeviceLE source)
    {
        return new CompatibilityProfileDevice
        {
            Manufacturer =
                source.Manufacturer,

            Model =
                source.Model,

            AndroidVersion =
                source.AndroidVersion,

            AndroidSdk =
                source.AndroidSdk,

            PrimaryAbi =
                source.PrimaryAbi,

            SupportedAbis =
                source.SupportedAbis,

            SupportsH264 =
                source.SupportsH264,

            SupportsH265 =
                source.SupportsH265,

            SupportsAv1 =
                source.SupportsAv1,

            SupportsOpus =
                source.SupportsOpus,

            DisplayDetected =
                source.DisplayDetected,

            NativeWidth =
                source.NativeWidth,

            NativeHeight =
                source.NativeHeight,

            MaxRefreshRateHz =
                source.MaxRefreshRateHz,

            LinkEngine =
                EvaluateLinkEngineLE(
                    source.AndroidSdk,
                    source.SupportedAbis),

            VisionEngine =
                EvaluateVisionEngineLE(
                    source.AndroidSdk,
                    /*
                     * DeviceCapabilities calcula:
                     *
                     *     IsDetected
                     *     MaxRefreshRateHz
                     *
                     * a partir de sus propiedades base.
                     *
                     * Por eso NO se asignan directamente.
                     */
                    new DeviceCapabilities
                    {
                        NativeWidth =
                            source.NativeWidth,

                        NativeHeight =
                            source.NativeHeight,

                        SupportedRefreshRatesHz =
                            source.MaxRefreshRateHz > 0
                                ? new[]
                                {
                                    source.MaxRefreshRateHz
                                }
                                : Array.Empty<double>()
                    },
                    source.SupportsH264,
                    source.SupportsH265,
                    source.SupportsOpus)
        };
    }

    private static CompatibilityLevelDevice EvaluateLinkEngineLE(
        int sdk,
        IReadOnlyList<string> supportedAbis)
    {
        if (sdk <= 0)
        {
            return CompatibilityLevelDevice.Unknown;
        }

        if (sdk <
            MinimumAndroidSdkLE)
        {
            return CompatibilityLevelDevice.Unsupported;
        }

        bool knownAbi =
            supportedAbis.Any(
                abi =>
                    string.Equals(
                        abi,
                        "arm64-v8a",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        abi,
                        "armeabi-v7a",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        abi,
                        "x86_64",
                        StringComparison.OrdinalIgnoreCase));

        return knownAbi
            ? CompatibilityLevelDevice.Full
            : CompatibilityLevelDevice.Partial;
    }

    private static CompatibilityLevelDevice EvaluateVisionEngineLE(
        int sdk,
        DeviceCapabilities display,
        bool h264,
        bool h265,
        bool opus)
    {
        if (sdk <= 0)
        {
            return CompatibilityLevelDevice.Unknown;
        }

        if (sdk <
            MinimumAndroidSdkLE)
        {
            return CompatibilityLevelDevice.Unsupported;
        }

        if (!display.IsDetected ||
            !h264)
        {
            return CompatibilityLevelDevice.Partial;
        }

        /*
         * H265 / Opus son mejoras.
         *
         * H264 + display siguen permitiendo funcionamiento base.
         */

        if (!h265 ||
            !opus)
        {
            return CompatibilityLevelDevice.Partial;
        }

        return CompatibilityLevelDevice.Full;
    }

    private static IReadOnlyList<string> ParseAbisLE(
        string primaryAbi,
        string abiList)
    {
        HashSet<string> result =
            new(
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(
            primaryAbi))
        {
            result.Add(
                primaryAbi.Trim());
        }

        foreach (
            string abi
            in (
                abiList ??
                string.Empty
            ).Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries))
        {
            result.Add(
                abi);
        }

        return result.ToArray();
    }

    private static bool ContainsLE(
        string source,
        params string[] values)
    {
        if (string.IsNullOrWhiteSpace(
            source))
        {
            return false;
        }

        foreach (string value in values)
        {
            if (source.Contains(
                value,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal enum SourceProfileDeviceLE
{
    FullScan = 0,
    ModelCache = 1
}

internal sealed record ResultResolverDeviceLE
{
    internal required CompatibilityProfileDevice Profile { get; init; }

    internal SourceProfileDeviceLE Source { get; init; }

    internal string ModelKey { get; init; } =
        string.Empty;

    internal bool SharedProfileHit { get; init; }
}

