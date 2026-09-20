using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.Control;

public sealed record NLControlFileMessage(string Id, string? Name = null, long Length = 0,
    string? Data = null, string? Sha256 = null, long Offset = 0);

/// <summary>Single streaming transfer owned by one authenticated control session.</summary>
public sealed class NLControlFileReceiver(string root) : IDisposable
{
    public const long MaxLength = 2L * 1024 * 1024 * 1024;
    // Hexadecimal payload is ASCII-safe through both JSON envelopes: 24 KiB becomes 48 KiB.
    public const int ChunkLength = 24 * 1024;
    private readonly object _gate = new();
    private FileStream? _stream;
    private IncrementalHash? _hash;
    private string? _id, _name, _temporary;
    private long _length, _received;
    private bool _disposed;
    public static bool IsFileAction(string action) => action is "file.begin" or "file.chunk" or "file.end" or "file.cancel";

    public Task<string> HandleAsync(string action, string? json, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!IsFileAction(action) || json is null || json.Length > 51000) throw new InvalidDataException("Mensaje de archivo inválido.");
                var message = JsonSerializer.Deserialize<NLControlFileMessage>(json) ?? throw new InvalidDataException("Mensaje vacío.");
                if (!Guid.TryParseExact(message.Id, "N", out _)) throw new InvalidDataException("Identificador inválido.");
                if (action == "file.begin")
                {
                    if (_stream is not null) throw new InvalidOperationException("Ya hay una transferencia activa.");
                    if (message.Length < 0 || message.Length > MaxLength) throw new InvalidDataException("El máximo por archivo es 2 GiB.");
                    _name = NLControlFileStorage.SafeName(message.Name ?? "");
                    Directory.CreateDirectory(root);
                    if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("La carpeta NOVORA no puede ser un enlace.");
                    _temporary = Path.Combine(root, ".novora-" + Guid.NewGuid().ToString("N") + ".partial");
                    _stream = new FileStream(_temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, ChunkLength);
                    _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    _id = message.Id; _length = message.Length; _received = 0;
                    return Task.FromResult("Recepción preparada.");
                }
                if (message.Id != _id || _stream is null) throw new InvalidOperationException("La transferencia ya no está activa.");
                if (action == "file.cancel") { Reset(); return Task.FromResult("Transferencia cancelada."); }
                if (action == "file.chunk")
                {
                    if (message.Data is null || message.Data.Length > ChunkLength * 2 || message.Offset != _received)
                        throw new InvalidDataException("Bloque fuera de secuencia.");
                    byte[] data = Convert.FromHexString(message.Data);
                    if (data.Length == 0 || _received + data.Length > _length) throw new InvalidDataException("Tamaño de bloque inválido.");
                    _stream.Write(data); _hash!.AppendData(data); _received += data.Length;
                    return Task.FromResult("Bloque recibido.");
                }
                if (_received != _length || message.Sha256 is null || message.Sha256.Length != 64 ||
                    !string.Equals(Convert.ToHexString(_hash!.GetHashAndReset()), message.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El archivo está incompleto o no coincide su SHA-256.");
                _stream.Flush(true); _stream.Dispose(); _stream = null;
                string destination = NLControlFileStorage.Commit(_temporary!, root, _name!);
                _temporary = null; Reset();
                return Task.FromResult("Guardado en " + destination);
            }
            catch { Reset(); throw; }
        }
    }
    private void Reset()
    {
        _stream?.Dispose(); _stream = null; _hash?.Dispose(); _hash = null;
        if (_temporary is { } path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        _temporary = _id = _name = null; _received = _length = 0;
    }
    public void Dispose() { lock (_gate) { Reset(); _disposed = true; } }
}

public static class NLControlFileSender
{
    public static async Task<string> SendAsync(Stream source, string name, long length,
        Func<string, string, Task<NLControlReply>> send, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        NLControlFileStorage.SafeName(name);
        if (length < 0 || length > NLControlFileReceiver.MaxLength) throw new InvalidDataException("Se requiere un tamaño conocido de hasta 2 GiB.");
        string id = Guid.NewGuid().ToString("N");
        async Task<string> Request(string action, NLControlFileMessage value)
        {
            var reply = await send(action, JsonSerializer.Serialize(value));
            if (!reply.Success) throw new IOException(reply.Message);
            return reply.Message;
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            await Request("file.begin", new(id, name, length));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[NLControlFileReceiver.ChunkLength];
            long offset = 0;
            while (true)
            {
                int count = await source.ReadAsync(buffer, ct);
                if (count == 0) break;
                if (offset + count > length) throw new IOException("El archivo cambió durante el envío.");
                hash.AppendData(buffer, 0, count);
                await Request("file.chunk", new(id, Data: Convert.ToHexString(buffer.AsSpan(0, count)), Offset: offset));
                offset += count; progress?.Report(offset); ct.ThrowIfCancellationRequested();
            }
            if (offset != length) throw new IOException("El tamaño del archivo cambió.");
            return await Request("file.end", new(id, Sha256: Convert.ToHexString(hash.GetHashAndReset())));
        }
        catch { try { await Request("file.cancel", new(id)); } catch { } throw; }
    }
}
