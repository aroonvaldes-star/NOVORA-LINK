using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Protocol;
using System.Globalization;
using NOVORA.VisionEngine.Performance;

namespace NOVORA.VisionEngine.Server;

/// <summary>
/// Opciones del servidor Android de VisionEngine.
/// Block B activa video + audio + control, pero el renderer de imagen sigue apagado.
/// </summary>
public sealed record VEServerOptions(
    VEProtocolCodec VideoCodec,
    int VideoBitRate,
    int MaxSize,
    double? MaxFps,
    bool AudioEnabled,
    VEProtocolCodec AudioCodec,
    int AudioBitRate,
    VEAudioSource AudioSource,
    bool AudioPlaybackEnabled,
    bool ControlEnabled,
    bool ClipboardAutosync,
    bool CleanupEnabled)
{
    public VEServerOptions ApplyStreamStabilityVE()
        => this with
        {
            VideoBitRate =
                VideoBitRate > 0
                    ? Math.Min(VideoBitRate, 4_000_000)
                    : VideoBitRate,

            MaxFps =
                MaxFps.HasValue
                    ? Math.Min(MaxFps.Value, 45d)
                    : 45d,

            AudioBitRate =
                Math.Min(AudioBitRate, 64_000)
        };

    public static VEServerOptions CreateForProfileVE(VEPerformanceProfile profile,
        IEnumerable<VEProtocolCodec>? supportedCodecs = null)
    {
        var selected = VEPerformanceOptions.CreateVE(profile, supportedCodecs);
        return CreateDefaultVE() with
        {
            VideoCodec = selected.PreferredCodec,
            VideoBitRate = selected.RecommendedBitrate,
            MaxSize = selected.MaxSize,
            MaxFps = selected.MaxFps
        };
    }

    public static VEServerOptions CreateDefaultVE()
        => new(
            VideoCodec: VEProtocolCodec.H264,
            VideoBitRate: 4_000_000,
            MaxSize: 1280,
            MaxFps: 45d,
            AudioEnabled: true,
            AudioCodec: VEProtocolCodec.Opus,
            AudioBitRate: 64_000,
            AudioSource: VEAudioSource.Output,
            AudioPlaybackEnabled: true,
            ControlEnabled: true,
            ClipboardAutosync: true,
            CleanupEnabled: true);

    public static VEServerOptions CreateVideoOnlyVE()
        => CreateDefaultVE() with
        {
            AudioEnabled = false,
            AudioPlaybackEnabled = false,
            ControlEnabled = false
        };

    public void ValidateVE()
    {
        if (!VideoCodec.IsVideoVE())
        {
            throw new ArgumentOutOfRangeException(
                nameof(VideoCodec),
                "VisionEngine requiere un codec de video válido.");
        }

        if (VideoBitRate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VideoBitRate));
        }

        if (MaxSize < 0 || MaxSize > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSize));
        }

        if (MaxFps.HasValue && MaxFps.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFps));
        }

        if (AudioEnabled && !AudioCodec.IsAudioVE())
        {
            throw new ArgumentOutOfRangeException(
                nameof(AudioCodec),
                "VisionEngine requiere un codec de audio válido.");
        }

        if (AudioBitRate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AudioBitRate));
        }
    }

    internal IReadOnlyList<string> BuildArgumentsVE(
        int scid,
        bool tunnelForward)
    {
        ValidateVE();

        List<string> args =
        [
            $"scid={scid:x8}",
            "log_level=info"
        ];

        if (VideoBitRate > 0)
        {
            args.Add($"video_bit_rate={VideoBitRate}");
        }

        if (VideoCodec != VEProtocolCodec.H264)
        {
            args.Add($"video_codec={VideoCodec.GetServerNameVE()}");
        }

        if (MaxSize > 0)
        {
            args.Add($"max_size={MaxSize}");
        }

        if (MaxFps is double maxFps)
        {
            args.Add(
                "max_fps=" +
                maxFps.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));
        }

        if (!AudioEnabled)
        {
            args.Add("audio=false");
        }
        else
        {
            if (AudioBitRate > 0)
            {
                args.Add($"audio_bit_rate={AudioBitRate}");
            }

            if (AudioCodec != VEProtocolCodec.Opus)
            {
                args.Add($"audio_codec={AudioCodec.GetServerNameVE()}");
            }

            if (AudioSource != VEAudioSource.Output)
            {
                args.Add($"audio_source={AudioSource.GetServerNameVE()}");
            }
        }

        if (!ControlEnabled)
        {
            args.Add("control=false");
        }

        /*
         * VisionEngine usa clipboard explícito.
         *
         * Con clipboard_autosync=false:
         *
         * GET_CLIPBOARD(COPY)
         *       ↓
         * Android inyecta KEYCODE_COPY
         *       ↓
         * lee clipboard
         *       ↓
         * envía DEVICE_MSG_CLIPBOARD inmediatamente
         *
         * Esto evita depender del listener automático del servidor,
         * evita duplicados y mantiene el clipboard bajo demanda.
         */
        /*
         * VisionEngine V3 utiliza el cambio real del clipboard Android
         * como evento.
         *
         * Se envía explícitamente para no depender del default del
         * servidor.
         */
        args.Add(
            $"clipboard_autosync={(ClipboardAutosync ? "true" : "false")}");

        if (tunnelForward)
        {
            args.Add("tunnel_forward=true");
        }

        if (!CleanupEnabled)
        {
            args.Add("cleanup=false");
        }

        return args;
    }
}
