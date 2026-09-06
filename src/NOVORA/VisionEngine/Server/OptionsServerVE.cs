using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Protocol;
using System.Globalization;

namespace NOVORA.VisionEngine.Server;

/// <summary>
/// Opciones del servidor Android de VisionEngine.
/// Block B activa video + audio + control, pero el renderer de imagen sigue apagado.
/// </summary>
public sealed record OptionsServerVE(
    CodecProtocolVE VideoCodec,
    int VideoBitRate,
    int MaxSize,
    double? MaxFps,
    bool AudioEnabled,
    CodecProtocolVE AudioCodec,
    int AudioBitRate,
    SourceAudioVE AudioSource,
    bool AudioPlaybackEnabled,
    bool ControlEnabled,
    bool CleanupEnabled)
{
    public static OptionsServerVE CreateDefaultVE()
        => new(
            VideoCodec: CodecProtocolVE.H264,
            VideoBitRate: 8_000_000,
            MaxSize: 0,
            MaxFps: null,
            AudioEnabled: true,
            AudioCodec: CodecProtocolVE.Opus,
            AudioBitRate: 128_000,
            AudioSource: SourceAudioVE.Output,
            AudioPlaybackEnabled: true,
            ControlEnabled: true,
            CleanupEnabled: true);

    public static OptionsServerVE CreateVideoOnlyVE()
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

        if (VideoCodec != CodecProtocolVE.H264)
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

            if (AudioCodec != CodecProtocolVE.Opus)
            {
                args.Add($"audio_codec={AudioCodec.GetServerNameVE()}");
            }

            if (AudioSource != SourceAudioVE.Output)
            {
                args.Add($"audio_source={AudioSource.GetServerNameVE()}");
            }
        }

        if (!ControlEnabled)
        {
            args.Add("control=false");
        }

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
