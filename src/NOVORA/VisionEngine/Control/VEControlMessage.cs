namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Mensaje de control inmutable. Los factories exponen sólo combinaciones
/// válidas y VEControlSerializer realiza la codificación wire exacta.
/// </summary>
public sealed record VEControlMessage
{
    public VEControlType Type { get; init; }
    public VEControlActionKey KeyAction { get; init; }
    public uint Keycode { get; init; }
    public uint Repeat { get; init; }
    public uint MetaState { get; init; }
    public string? Text { get; init; }
    public VEControlActionMotion MotionAction { get; init; }
    public ulong PointerId { get; init; }
    public VEControlPosition Position { get; init; }
    public float Pressure { get; init; }
    public uint ActionButton { get; init; }
    public uint Buttons { get; init; }
    public float HorizontalScroll { get; init; }
    public float VerticalScroll { get; init; }
    public VEControlCopy CopyKey { get; init; }
    public ulong Sequence { get; init; }
    public bool Paste { get; init; }
    public bool BooleanValue { get; init; }
    public ushort UhidId { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
    public string? Name { get; init; }
    public byte[]? Data { get; init; }
    public ushort Width { get; init; }
    public ushort Height { get; init; }

    private VEControlMessage(VEControlType type)
    {
        Type = type;
    }

    public static VEControlMessage KeycodeVE(
        VEControlActionKey action,
        uint keycode,
        uint repeat = 0,
        uint metaState = 0)
        => new(VEControlType.InjectKeycode)
        {
            KeyAction = action,
            Keycode = keycode,
            Repeat = repeat,
            MetaState = metaState
        };

    public static VEControlMessage TextVE(string text)
        => new(VEControlType.InjectText)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text))
        };

    public static VEControlMessage TouchVE(
        VEControlActionMotion action,
        ulong pointerId,
        int x,
        int y,
        ushort screenWidth,
        ushort screenHeight,
        float pressure,
        uint actionButton,
        uint buttons)
        => new(VEControlType.InjectTouchEvent)
        {
            MotionAction = action,
            PointerId = pointerId,
            Position = new VEControlPosition(x, y, screenWidth, screenHeight),
            Pressure = pressure,
            ActionButton = actionButton,
            Buttons = buttons
        };

    public static VEControlMessage ScrollVE(
        int x,
        int y,
        ushort screenWidth,
        ushort screenHeight,
        float horizontal,
        float vertical,
        uint buttons = 0)
        => new(VEControlType.InjectScrollEvent)
        {
            Position = new VEControlPosition(x, y, screenWidth, screenHeight),
            HorizontalScroll = horizontal,
            VerticalScroll = vertical,
            Buttons = buttons
        };

    public static VEControlMessage BackOrScreenOnVE(VEControlActionKey action)
        => new(VEControlType.BackOrScreenOn) { KeyAction = action };

    public static VEControlMessage SimpleVE(VEControlType type)
        => new(type);

    public static VEControlMessage GetClipboardVE(VEControlCopy copyKey = VEControlCopy.None)
        => new(VEControlType.GetClipboard) { CopyKey = copyKey };

    public static VEControlMessage SetClipboardVE(
        ulong sequence,
        string text,
        bool paste = false)
        => new(VEControlType.SetClipboard)
        {
            Sequence = sequence,
            Text = text ?? throw new ArgumentNullException(nameof(text)),
            Paste = paste
        };

    public static VEControlMessage SetDisplayPowerVE(bool on)
        => new(VEControlType.SetDisplayPower) { BooleanValue = on };

    public static VEControlMessage UhidCreateVE(
        ushort id,
        ushort vendorId,
        ushort productId,
        string name,
        byte[] reportDescriptor)
        => new(VEControlType.UhidCreate)
        {
            UhidId = id,
            VendorId = vendorId,
            ProductId = productId,
            Name = name ?? throw new ArgumentNullException(nameof(name)),
            Data = reportDescriptor?.ToArray() ?? throw new ArgumentNullException(nameof(reportDescriptor))
        };

    public static VEControlMessage UhidInputVE(ushort id, byte[] data)
        => new(VEControlType.UhidInput)
        {
            UhidId = id,
            Data = data?.ToArray() ?? throw new ArgumentNullException(nameof(data))
        };

    public static VEControlMessage UhidDestroyVE(ushort id)
        => new(VEControlType.UhidDestroy) { UhidId = id };

    public static VEControlMessage StartAppVE(string name)
        => new(VEControlType.StartApp)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name))
        };

    public static VEControlMessage ResizeDisplayVE(ushort width, ushort height)
        => new(VEControlType.ResizeDisplay) { Width = width, Height = height };

    public static VEControlMessage ScanFileVE(string path)
        => new(VEControlType.ScanFile)
        {
            Text = path ?? throw new ArgumentNullException(nameof(path))
        };
}
