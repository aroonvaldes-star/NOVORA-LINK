using Android.Content;
using Android.Provider;
using NOVORA.Control;
using Uri = Android.Net.Uri;

namespace NOVORA.AndroidService;

public sealed partial class NLAndroidServiceControl
{
    private CancellationTokenSource? _backgroundTransferStop;
    public string FileTransferMessage { get; private set; } = "";

    private void StartBackgroundFileTransfer(Intent intent)
    {
        var clip = intent.ClipData;
        if (clip is null || clip.ItemCount is < 1 or > 100 || !BeginFileTransfer()) return;
        var uris = new List<Uri>(clip.ItemCount);
        for (int i = 0; i < clip.ItemCount; i++)
        {
            Uri? uri = clip.GetItemAt(i)?.Uri;
            if (uri?.Scheme != "content") { EndFileTransfer(); return; }
            uris.Add(uri);
        }
        var stop = new CancellationTokenSource();
        _backgroundTransferStop = stop;
        long generation = Session.Current.Generation;
        SetFileTransferMessage($"Enviando 0/{uris.Count} archivos en segundo plano.");
        _ = RunBackgroundFileTransferAsync(uris, generation, stop);
    }

    public void CancelBackgroundFileTransfer() => _backgroundTransferStop?.Cancel();

    private async Task RunBackgroundFileTransferAsync(IReadOnlyList<Uri> uris, long generation,
        CancellationTokenSource stop)
    {
        int completed = 0;
        void TransferState(object? sender, string message) => SetFileTransferMessage(message);
        FileTransferStateChanged += TransferState;
        try
        {
            foreach (Uri uri in uris)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (Session.Current.Generation != generation || Session.Current.Phase != NLControlSessionPhase.Connected)
                    throw new IOException("La conexión cambió durante el envío.");
                var (name, length) = DescribeTransferUri(uri);
                using var stream = ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("No se pudo abrir " + name + ".");
                SetFileTransferMessage($"{completed + 1}/{uris.Count}: {name} · preparando envío");
                await SendFileToPcAsync(stream, name, length,
                    new Progress<long>(bytes => SetFileTransferMessage(
                        $"{completed + 1}/{uris.Count}: {name} · {bytes:N0} / {length:N0} bytes")),
                    stop.Token);
                completed++;
                SetFileTransferMessage($"{completed}/{uris.Count}: {name} recibido y verificado en PC.");
            }
            SetFileTransferMessage($"Envío completo: {completed} archivo(s) verificado(s) en PC.");
        }
        catch (OperationCanceledException)
        {
            SetFileTransferMessage($"Envío cancelado. {completed} archivo(s) completado(s).");
        }
        catch (Exception ex)
        {
            SetFileTransferMessage($"Envío interrumpido tras {completed} archivo(s): {ex.Message}");
        }
        finally
        {
            FileTransferStateChanged -= TransferState;
            if (ReferenceEquals(_backgroundTransferStop, stop)) _backgroundTransferStop = null;
            EndFileTransfer();
            stop.Dispose();
        }
    }

    private (string Name, long Length) DescribeTransferUri(Uri uri)
    {
        using var cursor = ContentResolver!.Query(uri,
            new[] { IOpenableColumns.DisplayName, IOpenableColumns.Size }, null, null, null);
        if (cursor?.MoveToFirst() != true || cursor.IsNull(0) || cursor.IsNull(1))
            throw new IOException("El proveedor no informa el nombre y tamaño. Guarda una copia local y vuelve a elegirla.");
        string name = NLControlFileStorage.SafeName(cursor.GetString(0)!);
        long length = cursor.GetLong(1);
        if (length is < 0 or > NLControlFileReceiver.MaxLength)
            throw new IOException("El archivo supera el límite de 2 GiB: " + name);
        return (name, length);
    }

    private void SetFileTransferMessage(string message)
    {
        FileTransferMessage = message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
