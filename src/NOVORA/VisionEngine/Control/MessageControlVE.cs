namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Mensaje de control inmutable. Los factories exponen sólo combinaciones
/// válidas y SerializerControlVE realiza la codificación wire exacta.
/// </summary>
public sealed record MessageControlVE
{
    public TypeControlVE Type { get; init; }
    public ActionKeyControlVE KeyAction { get; init; }
    public uint Keycode { get; init; }
    public uint Repeat { get; init; }
    public uint MetaState { get; init; }
    public string? Text { get; init; }
    public ActionMotionControlVE MotionAction { get; init; }
    public ulong PointerId { get; init; }
    public PositionControlVE Position { get; init; }
    public float Pressure { get; init; }
    public uint ActionButton { get; init; }
    public uint Buttons { get; init; }
    public float HorizontalScroll { get; init; }
    public float VerticalScroll { get; init; }
    public CopyControlVE CopyKey { get; init; }
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

    private MessageControlVE(TypeControlVE type)
    {
        Type = type;
    }

    public static MessageControlVE KeycodeVE(
        ActionKeyControlVE action,
        uint keycode,
        uint repeat = 0,
        uint metaState = 0)
        => new(TypeControlVE.InjectKeycode)
        {
            KeyAction = action,
            Keycode = keycode,
            Repeat = repeat,
            MetaState = metaState
        };

    public static MessageControlVE TextVE(string text)
        => new(TypeControlVE.InjectText)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text))
        };

    public static MessageControlVE TouchVE(
        ActionMotionControlVE action,
        ulong pointerId,
        int x,
        int y,
        ushort screenWidth,
        ushort screenHeight,
        float pressure,
        uint actionButton,
        uint buttons)
        => new(TypeControlVE.InjectTouchEvent)
        {
            MotionAction = action,
            PointerId = pointerId,
            Position = new PositionControlVE(x, y, screenWidth, screenHeight),
            Pressure = pressure,
            ActionButton = actionButton,
            Buttons = buttons
        };

    public static MessageControlVE ScrollVE(
        int x,
        int y,
        ushort screenWidth,
        ushort screenHeight,
        float horizontal,
        float vertical,
        uint buttons = 0)
        => new(TypeControlVE.InjectScrollEvent)
        {
            Position = new PositionControlVE(x, y, screenWidth, screenHeight),
            HorizontalScroll = horizontal,
            VerticalScroll = vertical,
            Buttons = buttons
        };

    public static MessageControlVE BackOrScreenOnVE(ActionKeyControlVE action)
        => new(TypeControlVE.BackOrScreenOn) { KeyAction = action };

    public static MessageControlVE SimpleVE(TypeControlVE type)
        => new(type);

    public static MessageControlVE GetClipboardVE(CopyControlVE copyKey = CopyControlVE.None)
        => new(TypeControlVE.GetClipboard) { CopyKey = copyKey };

    public static MessageControlVE SetClipboardVE(
        ulong sequence,
        string text,
        bool paste = false)
        => new(TypeControlVE.SetClipboard)
        {
            Sequence = sequence,
            Text = text ?? throw new ArgumentNullException(nameof(text)),
            Paste = paste
        };

    public static MessageControlVE SetDisplayPowerVE(bool on)
        => new(TypeControlVE.SetDisplayPower) { BooleanValue = on };

    public static MessageControlVE UhidCreateVE(
        ushort id,
        ushort vendorId,
        ushort productId,
        string name,
        byte[] reportDescriptor)
        => new(TypeControlVE.UhidCreate)
        {
            UhidId = id,
            VendorId = vendorId,
            ProductId = productId,
            Name = name ?? throw new ArgumentNullException(nameof(name)),
            Data = reportDescriptor?.ToArray() ?? throw new ArgumentNullException(nameof(reportDescriptor))
        };

    public static MessageControlVE UhidInputVE(ushort id, byte[] data)
        => new(TypeControlVE.UhidInput)
        {
            UhidId = id,
            Data = data?.ToArray() ?? throw new ArgumentNullException(nameof(data))
        };

    public static MessageControlVE UhidDestroyVE(ushort id)
        => new(TypeControlVE.UhidDestroy) { UhidId = id };

    public static MessageControlVE StartAppVE(string name)
        => new(TypeControlVE.StartApp)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name))
        };

    public static MessageControlVE ResizeDisplayVE(ushort width, ushort height)
        => new(TypeControlVE.ResizeDisplay) { Width = width, Height = height };

    public static MessageControlVE ScanFileVE(string path)
        => new(TypeControlVE.ScanFile)
        {
            Text = path ?? throw new ArgumentNullException(nameof(path))
        };
}
