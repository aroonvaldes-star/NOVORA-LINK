using Android.Content;
using Android.Provider;
using NOVORA.Control;
using Uri = Android.Net.Uri;

namespace NOVORA.AndroidStorage;

/// <summary>FILES v2 uses only the persisted grant for primary:NOVORA. No direct filesystem/all-files bypass.</summary>
internal sealed class NLAndroidFileStorageV2 : INLFileReceiveStorageV2
{
    private readonly ContentResolver _resolver;
    private readonly Uri _tree;
    private NLAndroidFileStorageV2(Context context, Uri tree)
    { _resolver = context.ContentResolver ?? throw new InvalidOperationException("Android no entregó ContentResolver."); _tree = tree; }

    internal static bool TryCreate(Context context, out NLAndroidFileStorageV2? storage)
    {
        if (!NLAndroidStorageNovoraFolder.TryGet(context, out Uri tree)) { storage = null; return false; }
        storage = new(context, tree); return true;
    }

    public async ValueTask<INLFileReceiveTargetV2> OpenAsync(Guid transferId, long generation, NLFileMetadataV2 metadata, CancellationToken ct)
    {
        if (transferId == Guid.Empty || generation < 0) throw new InvalidDataException("Identidad FILES inválida.");
        string[] directories = NLControlFileStorageV2.GetDirectories(metadata);
        return await Task.Run<INLFileReceiveTargetV2>(() =>
        {
            ct.ThrowIfCancellationRequested();
            Uri directory = NLAndroidStorageNovoraFolder.GetOrCreateDirectory(_resolver, _tree, directories);
            ct.ThrowIfCancellationRequested();
            Uri temporary = DocumentsContract.CreateDocument(_resolver, directory, "application/octet-stream",
                ".novora-" + Guid.NewGuid().ToString("N") + ".partial") ?? throw new IOException("No se pudo preparar el archivo NOVORA.");
            Stream? output = null;
            try
            {
                using var flags = _resolver.Query(temporary, new[] { DocumentsContract.Document.ColumnFlags }, null, null, null);
                if (flags?.MoveToFirst() != true) throw new IOException("El proveedor no informa publicación segura.");
                int supported = flags.GetInt(0);
                if ((supported & (int)DocumentContractFlags.SupportsRename) == 0 ||
                    (supported & (int)DocumentContractFlags.SupportsDelete) == 0)
                    throw new IOException("El proveedor no permite renombrar y limpiar el archivo temporal.");
                ct.ThrowIfCancellationRequested();
                output = _resolver.OpenOutputStream(temporary, "w") ?? throw new IOException("No se pudo abrir el archivo NOVORA.");
                ct.ThrowIfCancellationRequested();
                return new Target(_resolver, _tree, directory, temporary, output, metadata);
            }
            catch
            {
                output?.Dispose();
                if (!DocumentsContract.DeleteDocument(_resolver, temporary)) throw new IOException("No se pudo limpiar el parcial tras abrirlo.");
                throw;
            }
        }).ConfigureAwait(false);
    }

    private sealed class Target(ContentResolver resolver, Uri tree, Uri directory, Uri temporary, Stream output,
        NLFileMetadataV2 metadata) : INLFileReceiveTargetV2
    {
        private readonly SemaphoreSlim _mutation = new(1, 1);
        private Stream? _output = output;
        private Uri? _document = temporary;
        private volatile bool _aborted, _disposed;
        private long _written;

        public async ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Check(ct);
                if (offset != _written || data.Length <= 0 || data.Length > metadata.Length - _written)
                    throw new InvalidDataException("Bloque FILES fuera de secuencia.");
                await _output!.WriteAsync(data, ct).ConfigureAwait(false);
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
                    throw new InvalidDataException("Longitud/hash FILES inválidos.");
                await _output!.FlushAsync(ct).ConfigureAwait(false); _output.Dispose(); _output = null;
                return await Task.Run(() =>
                {
                    Check(ct);
                    using (var size = resolver.Query(_document!, new[] { DocumentsContract.Document.ColumnSize }, null, null, null))
                        if (size?.MoveToFirst() != true || size.IsNull(0) || size.GetLong(0) != length)
                            throw new IOException("El proveedor no confirmó la longitud escrita.");
                    string name = ReserveName();
                    Check(ct);
                    // Only the built-in external-storage provider/primary:NOVORA was accepted by TryCreate.
                    Uri published = DocumentsContract.RenameDocument(resolver, _document!, name)
                        ?? throw new IOException("No se pudo publicar el archivo NOVORA.");
                    _document = published;
                    // A SAF rename that completes after Abort is cleaned by Abort after this writer releases the semaphore.
                    Check(ct);
                    using var result = resolver.Query(published, new[] { DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnSize }, null, null, null);
                    if (result?.MoveToFirst() != true || result.IsNull(1) || result.GetLong(1) != length ||
                        result.GetString(0) != name || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("El proveedor no confirmó el archivo publicado.");
                    return published.ToString();
                }).ConfigureAwait(false);
            }
            finally { _mutation.Release(); }
        }

        private string ReserveName()
        {
            Uri children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, DocumentsContract.GetDocumentId(directory))
                ?? throw new IOException("No se pudo consultar el destino.");
            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cursor = resolver.Query(children, new[] { DocumentsContract.Document.ColumnDisplayName }, null, null, null))
                while (cursor?.MoveToNext() == true)
                {
                    if (occupied.Count >= 10000) throw new IOException("La carpeta contiene demasiados nombres para reservar destino.");
                    occupied.Add(cursor.GetString(0) ?? "");
                }
            for (int index = 0; index < 10000; index++)
            {
                string name = index == 0 ? metadata.Name : $"{Path.GetFileNameWithoutExtension(metadata.Name)} ({index}){Path.GetExtension(metadata.Name)}";
                if (!occupied.Contains(name)) return name;
            }
            throw new IOException("No se pudo reservar un nombre disponible.");
        }

        public async ValueTask AbortAsync(CancellationToken ct)
        {
            if (_disposed) return;
            _aborted = true;
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _output?.Dispose(); _output = null;
                if (_document is { } document)
                {
                    await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!DocumentsContract.DeleteDocument(resolver, document)) throw new IOException("No se pudo limpiar el archivo temporal NOVORA.");
                    }).ConfigureAwait(false);
                    _document = null;
                }
            }
            finally { _mutation.Release(); }
        }
        private void Check(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (_aborted || _disposed) throw new IOException("El destino FILES ya no está activo."); }
        public async ValueTask DisposeAsync()
        {
            await _mutation.WaitAsync().ConfigureAwait(false);
            try { _disposed = true; _output?.Dispose(); _output = null; }
            finally { _mutation.Release(); }
        }
    }
}
