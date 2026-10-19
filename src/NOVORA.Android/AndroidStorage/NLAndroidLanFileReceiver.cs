using Android.Content;
using Android.Net;
using Android.Provider;
using NOVORA.Control;
using System.Security.Cryptography;
using System.Text.Json;
using Uri = Android.Net.Uri;

namespace NOVORA.AndroidStorage;

/// <summary>Receives PC-to-Android files on the authenticated VE LAN FILES stream.</summary>
internal sealed class NLAndroidLanFileReceiver : IAsyncDisposable
{
    private readonly ContentResolver _resolver;
    private readonly Uri _tree;
    private Stream? _output;
    private IncrementalHash? _hash;
    private Uri? _temporary;
    private string? _id;
    private string? _name;
    private long _length;
    private long _received;

    private NLAndroidLanFileReceiver(Context context, Uri tree)
    {
        _resolver = context.ContentResolver ?? throw new InvalidOperationException("Android no entregó ContentResolver.");
        _tree = tree;
    }

    internal static bool TryCreate(Context context, out NLAndroidLanFileReceiver? receiver)
    {
        if (!NLAndroidStorageNovoraFolder.TryGet(context, out Uri tree))
        {
            receiver = null;
            return false;
        }

        receiver = new NLAndroidLanFileReceiver(context, tree);
        return true;
    }

    internal async Task RunAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NLControlRequest request = await NLControlProtocol.ReadAsync<NLControlRequest>(stream, cancellationToken)
                    .ConfigureAwait(false);
                NLControlReply reply;
                try
                {
                    if (request.Version != NLControlProtocol.Version || request.Id <= 0 ||
                        !NLControlFileReceiver.IsFileAction(request.Action))
                        throw new InvalidDataException("Solicitud de archivo LAN inválida.");
                    string message = await HandleAsync(request.Action, request.Value, cancellationToken).ConfigureAwait(false);
                    reply = new NLControlReply(NLControlProtocol.Version, request.Id, true, message);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                    JsonException or FormatException)
                {
                    Reset();
                    reply = new NLControlReply(NLControlProtocol.Version, request.Id, false, ex.Message);
                }

                await NLControlProtocol.WriteAsync(stream, reply, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { Reset(); }
    }

    private async Task<string> HandleAsync(string action, string? json, CancellationToken cancellationToken)
    {
        if (json is null || json.Length > 51000)
            throw new InvalidDataException("Mensaje de archivo LAN inválido.");
        NLControlFileMessage message = JsonSerializer.Deserialize<NLControlFileMessage>(json)
            ?? throw new InvalidDataException("Mensaje de archivo LAN vacío.");
        if (!Guid.TryParseExact(message.Id, "N", out _))
            throw new InvalidDataException("Identificador de archivo LAN inválido.");

        if (action == "file.begin")
        {
            if (_output is not null) throw new InvalidOperationException("Ya hay una transferencia LAN activa.");
            if (message.Length < 0 || message.Length > NLControlFileReceiver.MaxLength)
                throw new InvalidDataException("El máximo por archivo es 2 GiB.");

            _name = NLControlFileStorage.SafeName(message.Name ?? "");
            Uri directory = NLAndroidStorageNovoraFolder.GetOrCreateDirectory(_resolver, _tree,
                GetDirectories(message.RelativePath, _name));
            _temporary = DocumentsContract.CreateDocument(_resolver, directory,
                "application/octet-stream", ".novora-" + Guid.NewGuid().ToString("N") + ".partial")
                ?? throw new IOException("No se pudo preparar el archivo en NOVORA.");
            _output = _resolver.OpenOutputStream(_temporary, "w")
                ?? throw new IOException("No se pudo abrir el archivo NOVORA.");
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _id = message.Id;
            _length = message.Length;
            _received = 0;
            return "Recepción LAN preparada.";
        }

        if (message.Id != _id || _output is null || _hash is null || _temporary is null)
            throw new InvalidOperationException("La transferencia LAN ya no está activa.");
        if (action == "file.cancel")
        {
            Reset();
            return "Transferencia LAN cancelada.";
        }
        if (action == "file.chunk")
        {
            if (message.Data is null || message.Data.Length > NLControlFileReceiver.ChunkLength * 2 ||
                message.Offset != _received)
                throw new InvalidDataException("Bloque LAN fuera de secuencia.");
            byte[] data = Convert.FromHexString(message.Data);
            if (data.Length == 0 || _received + data.Length > _length)
                throw new InvalidDataException("Tamaño de bloque LAN inválido.");
            await _output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            _hash.AppendData(data);
            _received += data.Length;
            return "Bloque LAN recibido.";
        }

        if (_received != _length || message.Sha256 is null || message.Sha256.Length != 64 ||
            !string.Equals(Convert.ToHexString(_hash.GetHashAndReset()), message.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El archivo LAN está incompleto o no coincide su SHA-256.");

        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        _output.Dispose();
        _output = null;
        Uri committed = DocumentsContract.RenameDocument(_resolver, _temporary, _name!)
            ?? throw new IOException("No se pudo finalizar el archivo NOVORA.");
        _temporary = null;
        Reset();
        return "Guardado en " + committed;
    }

    private static IEnumerable<string> GetDirectories(string? relativePath, string name)
    {
        string expectedCategory = NLControlFileStorage.Category(name);
        string[] parts = (relativePath ?? expectedCategory + "/" + name)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[^1] != name ||
            (parts[0] != expectedCategory && parts[0] != "Otros"))
            throw new InvalidDataException("Ruta de archivo LAN fuera de NOVORA.");
        for (int index = 0; index < parts.Length; index++)
            NLControlFileStorage.SafeName(parts[index]);
        return parts[..^1];
    }

    private void Reset()
    {
        _output?.Dispose();
        _output = null;
        _hash?.Dispose();
        _hash = null;
        if (_temporary is not null)
        {
            try { DocumentsContract.DeleteDocument(_resolver, _temporary); }
            catch { }
        }
        _temporary = null;
        _id = _name = null;
        _length = _received = 0;
    }

    public ValueTask DisposeAsync()
    {
        Reset();
        return ValueTask.CompletedTask;
    }
}
