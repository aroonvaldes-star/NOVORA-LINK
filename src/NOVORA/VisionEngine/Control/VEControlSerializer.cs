using System.Buffers.Binary;
using System.Text;

namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Serializa los mensajes de control con el layout de scrcpy 4.1.
/// Todos los enteros del control protocol son big-endian, salvo los HID
/// reports que ya llegan como un blob opaco preparado por GamepadVE.
/// </summary>
public static class VEControlSerializer
{
    public const int MaxMessageSizeVE = 1 << 18;
    public const int MaxInjectTextBytesVE = 300;
    public const int MaxClipboardTextBytesVE = MaxMessageSizeVE - 14;
    public const int MaxScanPathBytesVE = 256;

    public static byte[] SerializeVE(VEControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Type switch
        {
            VEControlType.InjectKeycode => SerializeKeycodeVE(message),
            VEControlType.InjectText => SerializeTextVE(message),
            VEControlType.InjectTouchEvent => SerializeTouchVE(message),
            VEControlType.InjectScrollEvent => SerializeScrollVE(message),
            VEControlType.BackOrScreenOn =>
                [(byte)message.Type, (byte)message.KeyAction],
            VEControlType.GetClipboard =>
                [(byte)message.Type, (byte)message.CopyKey],
            VEControlType.SetClipboard => SerializeSetClipboardVE(message),
            VEControlType.SetDisplayPower =>
                [(byte)message.Type, message.BooleanValue ? (byte)1 : (byte)0],
            VEControlType.UhidCreate => SerializeUhidCreateVE(message),
            VEControlType.UhidInput => SerializeUhidInputVE(message),
            VEControlType.UhidDestroy => SerializeUhidDestroyVE(message),
            VEControlType.StartApp => SerializeTinyStringVE(message.Type, message.Name, 255),
            VEControlType.ResizeDisplay => SerializeResizeVE(message),
            VEControlType.ScanFile => SerializeStringVE(message.Type, message.Text, MaxScanPathBytesVE),
            _ => SerializeSimpleVE(message.Type)
        };
    }

    private static byte[] SerializeKeycodeVE(VEControlMessage message)
    {
        byte[] data = new byte[14];
        data[0] = (byte)message.Type;
        data[1] = (byte)message.KeyAction;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(2, 4), message.Keycode);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(6, 4), message.Repeat);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(10, 4), message.MetaState);
        return data;
    }

    private static byte[] SerializeTextVE(VEControlMessage message)
        => SerializeStringVE(message.Type, message.Text, MaxInjectTextBytesVE);

    private static byte[] SerializeTouchVE(VEControlMessage message)
    {
        message.Position.ValidateVE();
        byte[] data = new byte[32];
        data[0] = (byte)message.Type;
        data[1] = (byte)message.MotionAction;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(2, 8), message.PointerId);
        WritePositionVE(data.AsSpan(10, 12), message.Position);
        BinaryPrimitives.WriteUInt16BigEndian(
            data.AsSpan(22, 2),
            FloatToU16FixedVE(message.Pressure));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24, 4), message.ActionButton);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28, 4), message.Buttons);
        return data;
    }

    private static byte[] SerializeScrollVE(VEControlMessage message)
    {
        message.Position.ValidateVE();
        byte[] data = new byte[21];
        data[0] = (byte)message.Type;
        WritePositionVE(data.AsSpan(1, 12), message.Position);
        BinaryPrimitives.WriteInt16BigEndian(
            data.AsSpan(13, 2),
            FloatToI16FixedVE(Math.Clamp(message.HorizontalScroll / 16f, -1f, 1f)));
        BinaryPrimitives.WriteInt16BigEndian(
            data.AsSpan(15, 2),
            FloatToI16FixedVE(Math.Clamp(message.VerticalScroll / 16f, -1f, 1f)));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(17, 4), message.Buttons);
        return data;
    }

    private static byte[] SerializeSetClipboardVE(VEControlMessage message)
    {
        byte[] text = GetUtf8TruncatedVE(message.Text, MaxClipboardTextBytesVE);
        byte[] data = new byte[14 + text.Length];
        data[0] = (byte)message.Type;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(1, 8), message.Sequence);
        data[9] = message.Paste ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(10, 4), checked((uint)text.Length));
        text.CopyTo(data, 14);
        return data;
    }

    private static byte[] SerializeUhidCreateVE(VEControlMessage message)
    {
        byte[] descriptor = message.Data ?? throw new InvalidOperationException("UHID descriptor ausente.");
        if (descriptor.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(message), "Descriptor UHID demasiado grande.");
        }

        byte[] name = GetUtf8TruncatedVE(message.Name, 127);
        int length = 1 + 2 + 2 + 2 + 1 + name.Length + 2 + descriptor.Length;
        if (length > MaxMessageSizeVE)
        {
            throw new ArgumentOutOfRangeException(nameof(message), "Mensaje UHID_CREATE demasiado grande.");
        }

        byte[] data = new byte[length];
        data[0] = (byte)message.Type;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1, 2), message.UhidId);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3, 2), message.VendorId);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(5, 2), message.ProductId);
        data[7] = checked((byte)name.Length);
        name.CopyTo(data, 8);
        int index = 8 + name.Length;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(index, 2), checked((ushort)descriptor.Length));
        index += 2;
        descriptor.CopyTo(data, index);
        return data;
    }

    private static byte[] SerializeUhidInputVE(VEControlMessage message)
    {
        byte[] input = message.Data ?? throw new InvalidOperationException("UHID input ausente.");
        if (input.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(message), "UHID input demasiado grande.");
        }

        byte[] data = new byte[5 + input.Length];
        data[0] = (byte)message.Type;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1, 2), message.UhidId);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3, 2), checked((ushort)input.Length));
        input.CopyTo(data, 5);
        return data;
    }

    private static byte[] SerializeUhidDestroyVE(VEControlMessage message)
    {
        byte[] data = new byte[3];
        data[0] = (byte)message.Type;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1, 2), message.UhidId);
        return data;
    }

    private static byte[] SerializeResizeVE(VEControlMessage message)
    {
        byte[] data = new byte[5];
        data[0] = (byte)message.Type;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1, 2), message.Width);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3, 2), message.Height);
        return data;
    }

    private static byte[] SerializeSimpleVE(VEControlType type)
    {
        if (type is VEControlType.InjectKeycode or
            VEControlType.InjectText or
            VEControlType.InjectTouchEvent or
            VEControlType.InjectScrollEvent or
            VEControlType.BackOrScreenOn or
            VEControlType.GetClipboard or
            VEControlType.SetClipboard or
            VEControlType.SetDisplayPower or
            VEControlType.UhidCreate or
            VEControlType.UhidInput or
            VEControlType.UhidDestroy or
            VEControlType.StartApp or
            VEControlType.ResizeDisplay or
            VEControlType.ScanFile)
        {
            throw new InvalidOperationException($"{type} requiere payload.");
        }

        return [(byte)type];
    }

    private static byte[] SerializeStringVE(VEControlType type, string? value, int maxBytes)
    {
        byte[] text = GetUtf8TruncatedVE(value, maxBytes);
        byte[] data = new byte[5 + text.Length];
        data[0] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1, 4), checked((uint)text.Length));
        text.CopyTo(data, 5);
        return data;
    }

    private static byte[] SerializeTinyStringVE(VEControlType type, string? value, int maxBytes)
    {
        byte[] text = GetUtf8TruncatedVE(value, Math.Min(maxBytes, byte.MaxValue));
        byte[] data = new byte[2 + text.Length];
        data[0] = (byte)type;
        data[1] = checked((byte)text.Length);
        text.CopyTo(data, 2);
        return data;
    }

    private static void WritePositionVE(Span<byte> target, VEControlPosition position)
    {
        BinaryPrimitives.WriteUInt32BigEndian(target.Slice(0, 4), checked((uint)position.X));
        BinaryPrimitives.WriteUInt32BigEndian(target.Slice(4, 4), checked((uint)position.Y));
        BinaryPrimitives.WriteUInt16BigEndian(target.Slice(8, 2), position.ScreenWidth);
        BinaryPrimitives.WriteUInt16BigEndian(target.Slice(10, 2), position.ScreenHeight);
    }

    private static ushort FloatToU16FixedVE(float value)
        => checked((ushort)Math.Round(Math.Clamp(value, 0f, 1f) * ushort.MaxValue));

    private static short FloatToI16FixedVE(float value)
        => value <= -1f
            ? short.MinValue
            : checked((short)Math.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue));

    private static byte[] GetUtf8TruncatedVE(string? value, int maxBytes)
    {
        value ??= string.Empty;
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        Encoder encoder = Encoding.UTF8.GetEncoder();
        char[] chars = value.ToCharArray();
        byte[] buffer = new byte[maxBytes];
        encoder.Convert(
            chars,
            0,
            chars.Length,
            buffer,
            0,
            buffer.Length,
            true,
            out _,
            out int bytesUsed,
            out _);

        return buffer.AsSpan(0, bytesUsed).ToArray();
    }
}
