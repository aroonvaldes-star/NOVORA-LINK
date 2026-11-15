namespace NOVORA.VisionEngine.Integration;

public sealed record VEIntegrationCapabilities(
    bool Clipboard,
    bool FileTransfer,
    bool DragDrop,
    bool Applications,
    bool Notifications,
    bool DynamicResize,
    bool Camera,
    bool AndroidMicrophone,
    bool PcMicrophoneToAndroid)
{
    public static VEIntegrationCapabilities CreateDefaultVE()
        => new(
            Clipboard: true,
            FileTransfer: true,
            DragDrop: true,
            Applications: false,
            Notifications: false,
            DynamicResize: false,
            Camera: false,
            AndroidMicrophone: true,
            PcMicrophoneToAndroid: false);
}
