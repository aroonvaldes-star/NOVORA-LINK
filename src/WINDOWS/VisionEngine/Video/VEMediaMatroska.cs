using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace NOVORA.VisionEngine.Video;

internal sealed record VEMediaMuxPacket(int Track, long TimeUs, bool KeyFrame, byte[] Data);

/// <summary>Matroska v4: H264 length-prefixed NAL units and PCM16LE phone audio, source microsecond timestamps.</summary>
internal sealed class VEMediaMatroska : IDisposable
{
    private readonly Stream _output;
    private readonly long _segmentStart, _durationPosition;
    private readonly List<(long Time, long Position)> _cues = new();
    private long _audioEnd;
    private long? _lastVideoTime;
    private long _videoDelta = 1_000_000 / 45;
    internal long DurationUs => Math.Max(_audioEnd, (_lastVideoTime ?? 0) + _videoDelta);
    private bool _disposed;
    public VEMediaMatroska(Stream output, byte[] avcConfiguration, int width, int height, bool audio)
    {
        if (!output.CanSeek || !output.CanWrite) throw new ArgumentException("Matroska requires a seekable output.");
        _output = output;
        WriteElement(0x1A45DFA3, Join(UInt(0x4286, 1), UInt(0x42F7, 1), UInt(0x42F2, 4), UInt(0x42F3, 8), Text(0x4282, "matroska"), UInt(0x4287, 4), UInt(0x4285, 2)));
        WriteId(_output, 0x18538067); _output.Write(new byte[] { 1, 255, 255, 255, 255, 255, 255, 255 });
        _segmentStart = _output.Position;
        byte[] infoPrefix = Join(UInt(0x2AD7B1, 1000), Text(0x4D80, "NOVORA"), Text(0x5741, "NOVORA"));
        byte[] info = Join(infoPrefix, Element(0x4489, new byte[8]));
        WriteId(_output, 0x1549A966); WriteSize(_output, (ulong)info.Length);
        _durationPosition = _output.Position + infoPrefix.Length + 3;
        _output.Write(info);
        byte[] video = Element(0xAE, Join(UInt(0xD7, 1), UInt(0x73C5, 1), UInt(0x83, 1), UInt(0x9C, 0), Text(0x86, "V_MPEG4/ISO/AVC"), Element(0x63A2, avcConfiguration), Element(0xE0, Join(UInt(0xB0, checked((ulong)width)), UInt(0xBA, checked((ulong)height))))));
        byte[] audioTrack = audio ? Element(0xAE, Join(UInt(0xD7, 2), UInt(0x73C5, 2), UInt(0x83, 2), UInt(0x9C, 0), Text(0x86, "A_PCM/INT/LIT"), Element(0xE1, Join(Float(0xB5, 48000), UInt(0x9F, 2), UInt(0x6264, 16))))) : Array.Empty<byte>();
        WriteElement(0x1654AE6B, Join(video, audioTrack));
    }
    public void Write(VEMediaMuxPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (packet.TimeUs < 0 || packet.Track is not (1 or 2)) throw new InvalidDataException("Invalid recording packet.");
        byte[] data = packet.Track == 1 ? AvcSample(packet.Data) : packet.Data;
        byte[] block = new byte[checked(data.Length + 4)];
        block[0] = (byte)(0x80 | packet.Track); // one-byte track VINT
        block[3] = packet.KeyFrame ? (byte)0x80 : (byte)0;
        data.CopyTo(block, 4);
        long position = _output.Position - _segmentStart;
        WriteElement(0x1F43B675, Join(UInt(0xE7, (ulong)packet.TimeUs), Element(0xA3, block)));
        if (packet.Track == 2) _audioEnd = Math.Max(_audioEnd, checked(packet.TimeUs + data.Length * 1_000_000L / (48000 * 4)));
        else
        {
            if (_lastVideoTime is { } previous && packet.TimeUs > previous)
                _videoDelta = Math.Clamp(packet.TimeUs - previous, 1, 5_000_000);
            _lastVideoTime = Math.Max(_lastVideoTime ?? 0, packet.TimeUs);
        }
        if (packet.Track == 1 && packet.KeyFrame && _cues.Count < 100000) _cues.Add((packet.TimeUs, position));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        using var cues = new MemoryStream();
        foreach (var cue in _cues.OrderBy(c => c.Time))
        {
            byte[] entry = Element(0xBB, Join(UInt(0xB3, (ulong)cue.Time), Element(0xB7, Join(UInt(0xF7, 1), UInt(0xF1, (ulong)cue.Position)))));
            cues.Write(entry);
        }
        WriteElement(0x1C53BB6B, cues.ToArray());
        long end = _output.Position;
        _output.Position = _durationPosition;
        byte[] duration = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(duration, BitConverter.DoubleToInt64Bits(DurationUs)); _output.Write(duration);
        _output.Position = end; _output.Flush();
    }
    internal static byte[] AvcConfiguration(byte[] annexB)
    {
        var units = NalUnits(annexB);
        byte[][] sps = units.Where(n => (n[0] & 31) == 7).ToArray();
        byte[][] pps = units.Where(n => (n[0] & 31) == 8).ToArray();
        if (sps.Length is 0 or > 31 || pps.Length is 0 or > 255 || sps[0].Length < 4) throw new InvalidDataException("H264 SPS/PPS configuration is missing.");
        using var output = new MemoryStream();
        output.Write(new byte[] { 1, sps[0][1], sps[0][2], sps[0][3], 255, (byte)(0xE0 | sps.Length) });
        foreach (var nal in sps) WriteNal16(output, nal);
        output.WriteByte((byte)pps.Length); foreach (var nal in pps) WriteNal16(output, nal);
        return output.ToArray();
    }
    internal static byte[] MergeConfiguration(byte[]? previous, byte[] incoming)
    {
        var fresh = NalUnits(incoming).Where(n => (n[0] & 31) is 7 or 8).ToList();
        var old = previous is null ? new List<byte[]>() : NalUnits(previous);
        var types = fresh.Select(n => n[0] & 31).ToHashSet();
        using var output = new MemoryStream();
        foreach (var nal in old.Where(n => !types.Contains(n[0] & 31)).Concat(fresh))
        { output.Write(new byte[] { 0, 0, 0, 1 }); output.Write(nal); }
        if (output.Length > 1024 * 1024) throw new InvalidDataException("H264 configuration is too large.");
        return output.ToArray();
    }
    private static void WriteNal16(Stream output, byte[] nal)
    {
        if (nal.Length > ushort.MaxValue) throw new InvalidDataException("H264 configuration too large.");
        Span<byte> length = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)nal.Length); output.Write(length); output.Write(nal);
    }
    internal static byte[] AvcSample(byte[] annexB)
    {
        using var output = new MemoryStream();
        foreach (var nal in NalUnits(annexB))
        {
            byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, nal.Length); output.Write(length); output.Write(nal);
        }
        return output.ToArray();
    }
    private static List<byte[]> NalUnits(byte[] data)
    {
        var result = new List<byte[]>(); int start = -1;
        for (int i = 0; i + 2 < data.Length;)
        {
            int prefix = data[i] == 0 && data[i + 1] == 0 ? (data[i + 2] == 1 ? 3 : i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1 ? 4 : 0) : 0;
            if (prefix == 0) { i++; continue; }
            if (start >= 0 && i > start) result.Add(data[start..i]);
            i += prefix; start = i;
        }
        if (start >= 0 && start < data.Length) result.Add(data[start..]);
        if (result.Count == 0) throw new InvalidDataException("Expected H264 Annex B packet.");
        return result;
    }
    private void WriteElement(uint id, byte[] data) { WriteId(_output, id); WriteSize(_output, (ulong)data.Length); _output.Write(data); }
    private static byte[] Element(uint id, byte[] data) { using var stream = new MemoryStream(); WriteId(stream, id); WriteSize(stream, (ulong)data.Length); stream.Write(data); return stream.ToArray(); }
    private static byte[] Text(uint id, string value) => Element(id, Encoding.UTF8.GetBytes(value));
    private static byte[] Float(uint id, double value) { byte[] data = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(data, BitConverter.DoubleToInt64Bits(value)); return Element(id, data); }
    private static byte[] UInt(uint id, ulong value)
    {
        int length = 1; while (length < 8 && value >= (1UL << (length * 8))) length++;
        byte[] bytes = new byte[length]; for (int i = 0; i < length; i++) bytes[length - 1 - i] = (byte)(value >> (i * 8));
        return Element(id, bytes);
    }
    private static void WriteId(Stream stream, uint id)
    {
        int length = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
        for (int i = length - 1; i >= 0; i--) stream.WriteByte((byte)(id >> (i * 8)));
    }
    private static void WriteSize(Stream stream, ulong value)
    {
        int length = 1; while (length < 8 && value >= (1UL << (length * 7)) - 1) length++;
        ulong encoded = value | (1UL << (length * 7));
        for (int i = length - 1; i >= 0; i--) stream.WriteByte((byte)(encoded >> (i * 8)));
    }
    private static byte[] Join(params byte[][] values)
    {
        byte[] output = new byte[values.Sum(v => v.Length)]; int position = 0;
        foreach (var value in values) { value.CopyTo(output, position); position += value.Length; }
        return output;
    }
}
