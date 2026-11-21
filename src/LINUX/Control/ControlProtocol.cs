using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace NOVORA.Linux.Control;

internal sealed record ControlOption(string Value, string Label);
internal sealed record ControlVideoSettings(
    string Resolution,
    string Fps,
    ControlOption[] Resolutions,
    ControlOption[] FrameRates,
    string Monitor = "0", ControlOption[]? Monitors = null, bool CanApplyTogether = true,
    string PresentationMode = "Window", ControlOption[]? PresentationModes = null);
internal sealed record ControlEngines(
    bool VideoCanStart,
    bool VideoCanStop,
    bool LinkCanStart,
    bool LinkCanStop,
    bool LinkRunning,
    string LinkState,
    string LinkMessage,
    string DeviceName,
    string VideoState = "",
    string VideoMessage = "",
    string ExInState = "NotDetected",
    string ExInMessage = "ExInEngine no está disponible en esta compilación Linux.",
    bool LinkCanTakeOver = false,
    bool VideoAuthorizationRequested = false,
    string VideoTransport = "",
    string VideoPhase = "",
    bool VideoDegraded = false,
    object? LinkTelemetry = null,
    object? VisionTelemetry = null,
    long VideoAuthorizationRequestId = 0,
    bool ExInCanStart = false);
internal sealed record ControlVideoChanges(string Profile,string Bitrate,string Resolution,string Fps,string Audio,string Monitor,string PresentationMode = "Window");
internal sealed record ControlMedia(bool CanCapture, bool CanRecord, bool Recording, string Message, bool Starting = false, bool AudioIncluded = false);
internal sealed record ControlSnapshot(
    long Revision,
    string PcName,
    string PcVersion,
    string Bitrate,
    string Profile,
    string AudioOutput,
    string ActiveAudioOutput,
    bool VideoRunning,
    ControlOption[] Bitrates,
    ControlOption[] Profiles,
    ControlOption[] AudioOutputs,
    ControlEngines? Engines = null,
    ControlVideoSettings? VideoSettings = null, ControlMedia? Media = null, bool FileSharing = false, object? Capabilities = null,
    NOVORA.Linux.ExIn.LinuxControlExIn? ExIn = null);
internal sealed record ControlRequest(
    int Version,
    long Id,
    string Action,
    string? Value = null,
    long Revision = -1,
    string? Code = null);
internal sealed record TrustedPc(
    string PcName,
    string Host,
    int Port,
    string Fingerprint,
    string DeviceId,
    string Token,
    string Transport = "LAN");
internal sealed record ControlReply(
    int Version,
    long Id,
    bool Success,
    string Message,
    ControlSnapshot? Snapshot = null,
    TrustedPc? TrustedPc = null,
    string? Value = null);
internal sealed record LanPeer(string Name, string Host, int Port, string Fingerprint = "");
internal sealed record ControlInvitation(int Version, string Host, int Port, string Fingerprint,
    string Secret, long ExpiresUnix);
internal sealed record TunnelBootstrap(ControlInvitation Invitation, string Transport);
internal sealed record UsbLinkOffer(string Transport, string Token);
internal sealed record DiscoveryPacket(
    string Protocol,
    string Nonce,
    string? Code = null,
    LanPeer? Peer = null,
    object? Pairing = null);

internal static class ControlProtocol
{
    public const int Version = 1;
    public const int Port = 27215;
    public const int DiscoveryPort = 27216;
    public const int MaxFrame = 65536;
    public const string DiscoveryName = "NOVORA-CONTROL-DISCOVER/2";
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16 };

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > MaxFrame)
            throw new InvalidDataException("Tamaño de mensaje inválido.");

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new InvalidDataException("Mensaje vacío.");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length > MaxFrame)
            throw new InvalidDataException("Mensaje demasiado grande.");

        byte[] frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, sizeof(int));
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
