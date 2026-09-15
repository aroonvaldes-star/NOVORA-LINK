using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace NOVORA.Control;

public sealed record NLControlOption(string Value, string Label);
public sealed record NLControlEngines(bool VideoCanStart, bool VideoCanStop, bool LinkCanStart,
    bool LinkCanStop, bool LinkRunning, string LinkState, string LinkMessage, string DeviceName,
    string VideoState = "", string VideoMessage = "");
public sealed record NLControlVideoSettings(string Resolution, string Fps,
    NLControlOption[] Resolutions, NLControlOption[] FrameRates);
public sealed record NLControlMedia(bool CanCapture, bool CanRecord, bool Recording, string Message,
    bool Starting = false, bool AudioIncluded = false);
public sealed record NLControlSnapshot(
    long Revision, string PcName, string PcVersion, string Bitrate, string Profile,
    string AudioOutput, string ActiveAudioOutput, bool VideoRunning,
    NLControlOption[] Bitrates, NLControlOption[] Profiles, NLControlOption[] AudioOutputs, NLControlEngines? Engines = null,
    NLControlVideoSettings? VideoSettings = null, NLControlMedia? Media = null, bool FileSharing = false);
public sealed record NLControlRequest(int Version, long Id, string Action, string? Value = null,
    long Revision = -1, string? Code = null);
public sealed record NLControlReply(int Version, long Id, bool Success, string Message,
    NLControlSnapshot? Snapshot = null, NLControlTrustedPc? TrustedPc = null);

/// <summary>Framed JSON: USB uses loopback ADB; LAN requires the pinned TLS transport. Never expose raw frames on a LAN socket.</summary>
public static class NLControlProtocol
{
    public const int Version = 1;
    public const int Port = 27214;
    public const int MaxFrame = 65536;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 16 };

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > MaxFrame) throw new InvalidDataException("Tamaño de mensaje inválido.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, Options)
            ?? throw new InvalidDataException("Mensaje vacío.");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (payload.Length > MaxFrame) throw new InvalidDataException("Mensaje demasiado grande.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
