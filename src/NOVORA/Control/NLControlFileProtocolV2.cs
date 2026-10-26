using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.Control;

public enum NLFileFrameKindV2 : byte
{
    Begin = 1, Data = 2, Ack = 3, Commit = 4, Cancel = 5, Error = 6, Complete = 7, Cancelled = 8
}

public sealed record NLFileMetadataV2(string Name, long Length, string? RelativePath = null);
public sealed record NLFileFrameV2(NLFileFrameKindV2 Kind, Guid TransferId, long Offset, byte[] Payload);

/// <summary>FILES wire 2, after the existing authenticated JSON hello. No CONTROL/v1 framing is changed.</summary>
public static class NLControlFileProtocolV2
{
    public const int HeaderBytes = 36;
    public const int ChunkBytes = 65_536;
    public const int InitialWindowBytes = 262_144;
    public const int MaxWindowBytes = 1_048_576;
    public const int MaxMetadataBytes = 8192;
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static byte[] EncodeMetadata(NLFileMetadataV2 metadata)
    {
        ValidateMetadata(metadata);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions);
        if (bytes.Length > MaxMetadataBytes) throw new InvalidDataException("FILES metadata exceeds 8192 bytes.");
        return bytes;
    }

    public static NLFileMetadataV2 DecodeMetadata(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaxMetadataBytes) throw new InvalidDataException("Invalid FILES metadata size.");
        try
        {
            // Strict UTF-8 prevents invalid byte sequences from silently becoming replacement characters.
            _ = Utf8.GetString(bytes);
            var metadata = JsonSerializer.Deserialize<NLFileMetadataV2>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Missing FILES metadata.");
            ValidateMetadata(metadata);
            return metadata;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        { throw new InvalidDataException("Invalid FILES metadata.", ex); }
    }

    public static void ValidateMetadata(NLFileMetadataV2 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.Length < 0 || metadata.Length > MaxFileBytes)
            throw new InvalidDataException("The maximum file length is 2 GiB.");
        ValidateSegment(metadata.Name);
        if (metadata.RelativePath is { Length: > 0 } path)
        {
            if (path.Length > 2048 || path[0] is '/' or '\\') throw new InvalidDataException("Invalid relative directory.");
            foreach (string segment in path.Replace('\\', '/').Split('/')) ValidateSegment(segment);
        }
    }

    private static void ValidateSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || value is "." or ".." ||
            value.EndsWith('.') || value.EndsWith(' ')) throw new InvalidDataException("Invalid file name or directory segment.");
        foreach (char c in value)
            if (char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                throw new InvalidDataException("Invalid file name or directory segment.");
    }

    public static byte[] EncodeHeader(NLFileFrameV2 frame)
    {
        Validate(frame);
        byte[] header = new byte[HeaderBytes];
        header[0] = (byte)'N'; header[1] = (byte)'L'; header[2] = (byte)'F'; header[3] = (byte)'2';
        header[4] = (byte)frame.Kind;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), HeaderBytes);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)frame.Payload.Length);
        frame.TransferId.TryWriteBytes(header.AsSpan(12, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(28), (ulong)frame.Offset);
        return header;
    }

    public static async ValueTask WriteAsync(Stream stream, NLFileFrameV2 frame, CancellationToken ct = default)
    {
        byte[] header = EncodeHeader(frame);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (frame.Payload.Length != 0) await stream.WriteAsync(frame.Payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <returns>Null only for clean EOF before any header byte; partial frames throw.</returns>
    public static async ValueTask<NLFileFrameV2?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[HeaderBytes];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), ct).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct).ConfigureAwait(false);
        if (header[0] != 'N' || header[1] != 'L' || header[2] != 'F' || header[3] != '2' ||
            header[5] != 0 || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6)) != HeaderBytes)
            throw new InvalidDataException("Invalid FILES v2 header.");
        var kind = (NLFileFrameKindV2)header[4];
        uint size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        ValidatePayloadSize(kind, size); // Always before allocation.
        ulong wireOffset = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(28));
        if (wireOffset > MaxFileBytes) throw new InvalidDataException("Invalid FILES offset.");
        var id = new Guid(header.AsSpan(12, 16), bigEndian: true);
        if (id == Guid.Empty) throw new InvalidDataException("Empty FILES transfer id.");
        byte[] payload = new byte[(int)size];
        await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        var frame = new NLFileFrameV2(kind, id, (long)wireOffset, payload);
        Validate(frame);
        return frame;
    }

    public static NLFileFrameV2 Ack(Guid id, long offset, int credit)
    {
        if (credit < 0 || credit > MaxWindowBytes) throw new InvalidDataException("Invalid FILES credit.");
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)credit);
        return new(NLFileFrameKindV2.Ack, id, offset, payload);
    }

    public static int ReadCredit(NLFileFrameV2 frame)
    {
        if (frame.Kind != NLFileFrameKindV2.Ack || frame.Payload.Length != 4)
            throw new InvalidDataException("Invalid FILES acknowledgement.");
        uint credit = BinaryPrimitives.ReadUInt32BigEndian(frame.Payload);
        if (credit > MaxWindowBytes) throw new InvalidDataException("Excessive FILES credit.");
        return (int)credit;
    }

    public static NLFileFrameV2 Error(Guid id, long offset, string diagnostic)
    {
        // Diagnostics contain a bounded code/reason, never file payloads or remote private names.
        byte[] payload = Utf8.GetBytes(diagnostic);
        if (payload.Length > 1024) payload = Utf8.GetBytes("FILES storage or transfer failed.");
        return new(NLFileFrameKindV2.Error, id, offset, payload);
    }

    public static string ReadError(NLFileFrameV2 frame) => Utf8.GetString(frame.Payload);

    private static void Validate(NLFileFrameV2 frame)
    {
        if (frame.TransferId == Guid.Empty || frame.Offset < 0 || frame.Offset > MaxFileBytes)
            throw new InvalidDataException("Invalid FILES id or offset.");
        ArgumentNullException.ThrowIfNull(frame.Payload);
        ValidatePayloadSize(frame.Kind, (uint)frame.Payload.Length);
        if (frame.Kind == NLFileFrameKindV2.Begin)
        {
            if (frame.Offset != 0) throw new InvalidDataException("Begin must have offset zero.");
            _ = DecodeMetadata(frame.Payload);
        }
        if (frame.Kind == NLFileFrameKindV2.Ack) _ = ReadCredit(frame);
        if (frame.Kind == NLFileFrameKindV2.Error)
        {
            try { _ = Utf8.GetString(frame.Payload); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid error UTF-8.", ex); }
        }
    }

    private static void ValidatePayloadSize(NLFileFrameKindV2 kind, uint bytes)
    {
        bool valid = kind switch
        {
            NLFileFrameKindV2.Begin => bytes is > 0 and <= MaxMetadataBytes,
            NLFileFrameKindV2.Data => bytes is > 0 and <= ChunkBytes,
            NLFileFrameKindV2.Ack => bytes == 4,
            NLFileFrameKindV2.Commit or NLFileFrameKindV2.Complete => bytes == 32,
            NLFileFrameKindV2.Cancel or NLFileFrameKindV2.Cancelled => bytes == 0,
            NLFileFrameKindV2.Error => bytes <= 1024,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Invalid FILES frame kind or payload length.");
    }
}
