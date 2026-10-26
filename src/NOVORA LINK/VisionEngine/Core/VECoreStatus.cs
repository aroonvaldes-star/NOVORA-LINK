namespace NOVORA.VisionEngine.Core;

/// <summary>
/// Snapshot global de VisionEngine Block D.
/// </summary>
public sealed record VECoreStatus(
    VECoreStates State,
    bool IsInitialized,
    bool IsRunning,
    string? DeviceSerial,
    Guid? SessionId,
    bool DeviceReady,
    bool ServerRunning,
    bool TransportConnected,
    bool VideoStreaming,
    long VideoPacketsReceived,
    long VideoFramesDecoded,
    long VideoDecodeErrors,
    bool AudioStreaming,
    long AudioPacketsReceived,
    long AudioFramesDecoded,
    long AudioDecodeErrors,
    bool AudioPlaybackEnabled,
    bool ControlReady,
    long ControlMessagesSent,
    long ControlMessagesReceived,
    int ConnectedGamepads,
    long GamepadReportsSent,
    bool RendererEnabled,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public string? RendererBackend { get; init; }
    public long VideoFramesRendered { get; init; }
    public long RendererFramesDropped { get; init; }
    public long RendererErrors { get; init; }

    public static VECoreStatus CreateInitialVE()
        => new(
            State: VECoreStates.Stopped,
            IsInitialized: false,
            IsRunning: false,
            DeviceSerial: null,
            SessionId: null,
            DeviceReady: false,
            ServerRunning: false,
            TransportConnected: false,
            VideoStreaming: false,
            VideoPacketsReceived: 0,
            VideoFramesDecoded: 0,
            VideoDecodeErrors: 0,
            AudioStreaming: false,
            AudioPacketsReceived: 0,
            AudioFramesDecoded: 0,
            AudioDecodeErrors: 0,
            AudioPlaybackEnabled: false,
            ControlReady: false,
            ControlMessagesSent: 0,
            ControlMessagesReceived: 0,
            ConnectedGamepads: 0,
            GamepadReportsSent: 0,
            RendererEnabled: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "VisionEngine detenido.",
            LastError: null)
        {
            RendererBackend = null,
            VideoFramesRendered = 0,
            RendererFramesDropped = 0,
            RendererErrors = 0
        };
}
