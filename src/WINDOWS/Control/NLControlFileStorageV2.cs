using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.Control;

/// <summary>Confined filesystem target with one unique partial, ordered writes and atomic same-directory publication.</summary>
public sealed class NLControlFileStorageV2 : INLFileReceiveStorageV2
{
    private readonly string _root;
    public NLControlFileStorageV2(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Missing FILES root.", nameof(root));
        _root = Path.GetFullPath(root);
    }

    /// <summary>RelativePath retains v1 semantics: category/subdirectory/name, never a remote absolute path.</summary>
    public static string[] GetDirectories(NLFileMetadataV2 metadata)
    {
        NLControlFileProtocolV2.ValidateMetadata(metadata);
        NLControlFileStorage.SafeName(metadata.Name);
        string category = NLControlFileStorage.Category(metadata.Name);
        string path = metadata.RelativePath ?? category + "/" + metadata.Name;
        string[] parts = path.Replace('\\', '/').Split('/');
        if (parts.Length < 2 || parts.Length > 32 || parts[^1] != metadata.Name ||
            (parts[0] != category && parts[0] != "Otros"))
            throw new InvalidDataException("FILES directory is outside the permitted category.");
        foreach (string part in parts) NLControlFileStorage.SafeName(part);
        return parts[..^1];
    }

    public ValueTask<INLFileReceiveTargetV2> OpenAsync(Guid transferId, long generation, NLFileMetadataV2 metadata, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (transferId == Guid.Empty || generation < 0) throw new InvalidDataException("Invalid FILES storage owner.");
        string[] segments = GetDirectories(metadata);
        EnsureNoLinks(_root, create: true);
        string directory = _root;
        foreach (string segment in segments)
        {
            directory = Path.Combine(directory, segment);
            EnsureNoLinks(directory, create: true);
        }
        ct.ThrowIfCancellationRequested();
        string temporary = Path.Combine(directory, ".novora-" + transferId.ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".partial");
        FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            NLControlFileProtocolV2.ChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult<INLFileReceiveTargetV2>(new Target(directory, temporary, metadata, stream));
    }

    private static void EnsureNoLinks(string path, bool create)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? throw new InvalidDataException("Invalid FILES storage root.");
        string current = root;
        foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                if (!create) throw new DirectoryNotFoundException("FILES target directory disappeared.");
                Directory.CreateDirectory(current);
            }
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("FILES destination may not contain a directory link.");
        }
    }

    private sealed class Target(string directory, string temporary, NLFileMetadataV2 metadata, FileStream stream) : INLFileReceiveTargetV2
    {
        private readonly SemaphoreSlim _mutation = new(1, 1);
        private readonly object _publication = new();
        private FileStream? _stream = stream;
        private bool _aborted, _disposed;
        private string? _published;
        private long _written;

        public async ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Check(ct);
                if (offset != _written || data.Length <= 0 || data.Length > metadata.Length - _written)
                    throw new InvalidDataException("FILES provider write is out of sequence.");
                await _stream!.WriteAsync(data, ct).ConfigureAwait(false);
                _written += data.Length;
            }
            finally { _mutation.Release(); }
        }

        public async ValueTask<string?> CommitAsync(long length, ReadOnlyMemory<byte> sha256, CancellationToken ct)
        {
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Check(ct);
                if (length != metadata.Length || _written != length || sha256.Length != 32)
                    throw new InvalidDataException("FILES provider commit length/hash is invalid.");
                await _stream!.FlushAsync(ct).ConfigureAwait(false);
                _stream.Flush(flushToDisk: true);
                Check(ct); _stream.Dispose(); _stream = null;
                EnsureNoLinks(directory, create: false);
                if ((File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("FILES partial may not be a file link.");
                lock (_publication)
                {
                    Check(ct);
                    for (int index = 0; index < 10000; index++)
                    {
                        string name = index == 0 ? metadata.Name : $"{Path.GetFileNameWithoutExtension(metadata.Name)} ({index}){Path.GetExtension(metadata.Name)}";
                        string candidate = Path.Combine(directory, name);
                        try { File.Move(temporary, candidate, overwrite: false); _published = candidate; return candidate; }
                        catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
                    }
                    throw new IOException("FILES cannot reserve a destination name.");
                }
            }
            finally { _mutation.Release(); }
        }

        public async ValueTask AbortAsync(CancellationToken ct)
        {
            // Invalidates commit before waiting for a cancellable in-progress write/flush.
            lock (_publication) { if (_disposed) return; _aborted = true; }
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _stream?.Dispose(); _stream = null;
                if (File.Exists(temporary)) File.Delete(temporary);
                // A commit completed just before cancellation but not yet accepted by Complete is also rolled back.
                if (_published is not null && File.Exists(_published)) File.Delete(_published);
                _published = null;
                if (File.Exists(temporary)) throw new IOException("FILES partial cleanup did not complete.");
            }
            finally { _mutation.Release(); }
        }

        private void Check(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_aborted || _disposed) throw new IOException("FILES target is no longer current.");
        }

        public async ValueTask DisposeAsync()
        {
            await _mutation.WaitAsync().ConfigureAwait(false);
            try { lock (_publication) _disposed = true; _stream?.Dispose(); _stream = null; }
            finally { _mutation.Release(); }
        }
    }
}
