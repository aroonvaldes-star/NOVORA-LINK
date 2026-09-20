namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Tipos de mensajes del control protocol compatible con scrcpy 4.1.
/// Los valores son parte del wire protocol y no deben reordenarse.
/// </summary>
public enum VEControlType : byte
{
    InjectKeycode = 0,
    InjectText = 1,
    InjectTouchEvent = 2,
    InjectScrollEvent = 3,
    BackOrScreenOn = 4,
    ExpandNotificationPanel = 5,
    ExpandSettingsPanel = 6,
    CollapsePanels = 7,
    GetClipboard = 8,
    SetClipboard = 9,
    SetDisplayPower = 10,
    RotateDevice = 11,
    UhidCreate = 12,
    UhidInput = 13,
    UhidDestroy = 14,
    OpenHardKeyboardSettings = 15,
    StartApp = 16,
    ResetVideo = 17,
    CameraSetTorch = 18,
    CameraZoomIn = 19,
    CameraZoomOut = 20,
    ResizeDisplay = 21,
    ScanFile = 22
}
