using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace NOVORA.Control;

public sealed record NLControlOption(string Value, string Label);
public sealed record NLControlMetric<T>(T Value, DateTimeOffset SampledAtUtc, TimeSpan Window)
{
    public bool IsFreshVE(DateTimeOffset nowUtc, TimeSpan maxAge) =>
        nowUtc >= SampledAtUtc && nowUtc - SampledAtUtc <= maxAge;
}
public sealed record NLControlLinkTelemetry(double RoundTripMs, double PacketLossPercent,
    double TxMbps, double RxMbps, DateTimeOffset SampledAtUtc);
public sealed record NLControlVisionTelemetry(int Width, int Height, double FramesPerSecond,
    double MegabitsPerSecond, DateTimeOffset SampledAtUtc);
public sealed record NLControlAcceleration(string Requested, string Selected, string Active,
    bool Confirmed, string Evidence, string? FallbackReason = null);
public sealed record NLControlExInTelemetry(long Sequence, DateTimeOffset SampledAtUtc,
    double PollingHz, double PcProcessingMs, double JitterMs);
public sealed record NLControlExInCalibration(int LeftStick, int RightStick,
    int LeftTrigger, int RightTrigger, int Overall, bool Complete, string Phase);
public sealed record NLControlEngines(bool VideoCanStart, bool VideoCanStop, bool LinkCanStart,
    bool LinkCanStop, bool LinkRunning, string LinkState, string LinkMessage, string DeviceName,
    string VideoState = "", string VideoMessage = "",
    string ExInState = "NotDetected", string ExInMessage = "ExInEngine no reportado por esta PC.",
    bool LinkCanTakeOver = false, bool VideoAuthorizationRequested = false,
    string VideoTransport = "", string VideoPhase = "", bool VideoDegraded = false,
    NLControlLinkTelemetry? LinkTelemetry = null, NLControlVisionTelemetry? VisionTelemetry = null);
public sealed record NLControlVideoSettings(string Resolution, string Fps,
    NLControlOption[] Resolutions, NLControlOption[] FrameRates,
    string Monitor = "", NLControlOption[]? Monitors = null, bool CanApplyTogether = false);
public sealed record NLControlVideoChanges(string Profile, string Bitrate, string Resolution, string Fps, string Audio, string Monitor);
public sealed record NLControlMedia(bool CanCapture, bool CanRecord, bool Recording, string Message,
    bool Starting = false, bool AudioIncluded = false);
public sealed record NLControlExIn(bool Detected, string DeviceName, string VidPid,
    int LeftX, int LeftY, int RightX, int RightY, int LeftTrigger, int RightTrigger,
    string[] Buttons, bool Calibrating, bool Calibrated, double Deadzone, string Message,
    string Family = "Unknown", string Mode = "Game", bool Transitioning = false,
    bool CanSetMode = false, bool CanReactivate = false,
    int CorrectedLeftX = 0, int CorrectedLeftY = 0, int CorrectedRightX = 0, int CorrectedRightY = 0,
    int CorrectedLeftTrigger = 0, int CorrectedRightTrigger = 0,
    string Health = "Unknown", bool Correctable = false, string DiagnosticMessage = "",
    string BatteryState = "Unknown", int BatteryPercent = -1,
    string Identity = "", string ConnectionType = "Unknown",
    bool SupportsGamepad = true, bool SupportsPointer = false, bool SupportsTouchpad = false,
    bool SupportsNavigation = false, bool CanCalibrate = false, string CalibrationDetails = "",
    string BatteryAlert = "", long BatteryAlertSequence = 0,
    NLControlExInTelemetry? Telemetry = null, NLControlExInCalibration? CalibrationProgress = null);
public sealed record NLControlSnapshot(
    long Revision, string PcName, string PcVersion, string Bitrate, string Profile,
    string AudioOutput, string ActiveAudioOutput, bool VideoRunning,
    NLControlOption[] Bitrates, NLControlOption[] Profiles, NLControlOption[] AudioOutputs, NLControlEngines? Engines = null,
    NLControlVideoSettings? VideoSettings = null, NLControlMedia? Media = null, bool FileSharing = false,
    NLControlExIn? ExIn = null, NLControlAcceleration? Acceleration = null);
public sealed record NLControlRequest(int Version, long Id, string Action, string? Value = null,
    long Revision = -1, string? Code = null);
public sealed record NLControlReply(int Version, long Id, bool Success, string Message,
    NLControlSnapshot? Snapshot = null, NLControlTrustedPc? TrustedPc = null, string? Value = null);
public sealed record NLControlVideoSourceOffer(int Port, string Token, int Bitrate, int MaxSize, int Fps,
    int ControlPort = 0, string ControlToken = "", int AudioPort = 0, string AudioToken = "",
    bool MuteDeviceAudio = false, bool AudioEnabled = true);
public sealed record NLControlTunnelBootstrap(NLControlLanInvitation Invitation, string Transport);
public sealed record NLControlLinkOffer(string Host, int Port, string Fingerprint, string Token,
    string Transport = "LAN");
public sealed record NLControlLinkHello(int Version, string Token, string Channel = "DATA");
public sealed record NLControlInputCommand(
    int Type, int KeyAction = 0, uint Keycode = 0, uint Repeat = 0, uint MetaState = 0,
    string? Text = null, int MotionAction = 0, ulong PointerId = 0,
    int X = 0, int Y = 0, ushort ScreenWidth = 0, ushort ScreenHeight = 0,
    float Pressure = 0, uint ActionButton = 0, uint Buttons = 0,
    float HorizontalScroll = 0, float VerticalScroll = 0, int CopyKey = 0,
    ulong Sequence = 0, bool Paste = false, bool BooleanValue = false,
    ushort UhidId = 0, ushort VendorId = 0, ushort ProductId = 0,
    string? Name = null, byte[]? Data = null, ushort Width = 0, ushort Height = 0);
public sealed record NLControlInputResponse(int Type, string? Text = null, ulong Sequence = 0)
{
    public const int ClipboardType = 1;
    public const int ClipboardAckType = 2;
}

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
