using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NOVORA.Remote;

namespace NOVORA;

public sealed class AndroidShareFileRowNV :
    INotifyPropertyChanged
{
    private string _state = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TransferId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string SavedPath { get; set; } = string.Empty;

    public string State
    {
        get => _state;
        set
        {
            if (string.Equals(_state, value, StringComparison.Ordinal))
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public partial class MainWindow
{
    private const int AndroidShareHeaderLimitNV =
        64 * 1024;

    private readonly ObservableCollection<AndroidShareFileRowNV>
        _androidShareFilesNV =
            new();

    private readonly ConcurrentDictionary<string, AndroidShareFileRowNV>
        _androidShareRowsByTransferNV =
            new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _androidShareTransferCtsNV;
    private TcpListener? _androidShareTransferListenerNV;
    private Task? _androidShareTransferTaskNV;

    private void InitializeAndroidShareListNV()
    {
        AndroidShareFilesList14.ItemsSource =
            _androidShareFilesNV;

        try
        {
            StartAndroidShareTransferServerNV();
        }
        catch (Exception ex)
        {
            AndroidShareEmptyText14.Text =
                "Transferencia Android no disponible: " +
                ex.Message;
        }
    }

    private void StartAndroidShareTransferServerNV()
    {
        if (_androidShareTransferTaskNV is { IsCompleted: false })
        {
            return;
        }

        _androidShareTransferCtsNV?.Dispose();
        _androidShareTransferCtsNV =
            new CancellationTokenSource();

        var listener =
            new TcpListener(
                IPAddress.Loopback,
                ProtocolRemoteNV.DefaultFileTransferPortNV);

        listener.Server.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true);

        listener.Start(backlog: 4);

        _androidShareTransferListenerNV =
            listener;

        _androidShareTransferTaskNV =
            AcceptAndroidShareTransfersNVAsync(
                _androidShareTransferCtsNV.Token);
    }

    private async Task StopAndroidShareTransferServerNVAsync()
    {
        CancellationTokenSource? cts =
            _androidShareTransferCtsNV;

        Task? task =
            _androidShareTransferTaskNV;

        cts?.Cancel();

        try
        {
            _androidShareTransferListenerNV?.Stop();
        }
        catch
        {
        }

        if (task is not null)
        {
            try
            {
                await task
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(true);
            }
            catch
            {
            }
        }

        _androidShareTransferListenerNV = null;
        _androidShareTransferTaskNV = null;

        cts?.Dispose();
        _androidShareTransferCtsNV = null;
    }

    private async Task AcceptAndroidShareTransfersNVAsync(
        CancellationToken cancellationToken)
    {
        TcpListener listener =
            _androidShareTransferListenerNV ??
            throw new InvalidOperationException(
                "El servidor de archivos Android no está iniciado.");

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client =
                    await listener
                        .AcceptTcpClientAsync(cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ =
                ReceiveAndroidShareTransferNVAsync(
                    client,
                    cancellationToken);
        }
    }

    private async Task ReceiveAndroidShareTransferNVAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        string transferId =
            string.Empty;

        await using NetworkStream stream =
            client.GetStream();

        using (client)
        {
            try
            {
                ShareFileTransferHeaderRemoteNV header =
                    await ReadAndroidShareHeaderNVAsync(
                            stream,
                            cancellationToken)
                        .ConfigureAwait(false);

                transferId =
                    header.TransferId;

                if (string.IsNullOrWhiteSpace(transferId) ||
                    !_androidShareRowsByTransferNV.TryGetValue(
                        transferId,
                        out AndroidShareFileRowNV? row))
                {
                    throw new InvalidOperationException(
                        "Transferencia Android no registrada.");
                }

                string outputPath =
                    CreateAndroidShareOutputPathNV(header.Name);

                await Dispatcher
                    .InvokeAsync(
                        () =>
                        {
                            row.SavedPath = outputPath;
                            row.State = "Recibiendo 0%";
                        });

                await ReceiveAndroidShareBytesNVAsync(
                        stream,
                        outputPath,
                        row,
                        header.SizeBytes,
                        cancellationToken)
                    .ConfigureAwait(false);

                _androidShareRowsByTransferNV.TryRemove(
                    transferId,
                    out _);

                await Dispatcher
                    .InvokeAsync(
                        () =>
                        {
                            row.State = "Recibido";
                            ShowTopMessage14(
                                $"Archivo recibido: {row.Name}",
                                MessageKind14.Success);
                        });
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(transferId) &&
                    _androidShareRowsByTransferNV.TryGetValue(
                        transferId,
                        out AndroidShareFileRowNV? row))
                {
                    await Dispatcher
                        .InvokeAsync(
                            () =>
                                row.State =
                                    "Error de transferencia");
                }

                await Dispatcher
                    .InvokeAsync(
                        () =>
                            ShowTopMessage14(
                                $"No se pudo recibir archivo Android: {ex.Message}",
                                MessageKind14.Error));
            }
        }
    }

    private static async Task<ShareFileTransferHeaderRemoteNV> ReadAndroidShareHeaderNVAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] lengthBuffer =
            new byte[sizeof(int)];

        await ReadExactlyAndroidShareNVAsync(
                stream,
                lengthBuffer,
                cancellationToken)
            .ConfigureAwait(false);

        int length =
            BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);

        if (length <= 0 ||
            length > AndroidShareHeaderLimitNV)
        {
            throw new InvalidDataException(
                "Cabecera de archivo Android inválida.");
        }

        byte[] payload =
            new byte[length];

        await ReadExactlyAndroidShareNVAsync(
                stream,
                payload,
                cancellationToken)
            .ConfigureAwait(false);

        ShareFileTransferHeaderRemoteNV? header =
            JsonSerializer.Deserialize<ShareFileTransferHeaderRemoteNV>(
                Encoding.UTF8.GetString(payload));

        return header ??
               throw new InvalidDataException(
                   "Cabecera de archivo Android vacía.");
    }

    private async Task ReceiveAndroidShareBytesNVAsync(
        Stream stream,
        string outputPath,
        AndroidShareFileRowNV row,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath) ??
            GetAndroidShareDownloadsDirectoryNV());

        await using FileStream output =
            new(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 128 * 1024,
                useAsync: true);

        byte[] buffer =
            new byte[128 * 1024];

        long received =
            0;

        while (expectedBytes < 0 ||
               received < expectedBytes)
        {
            int readLimit =
                expectedBytes < 0
                    ? buffer.Length
                    : (int)Math.Min(
                        buffer.Length,
                        expectedBytes - received);

            int read =
                await stream
                    .ReadAsync(
                        buffer.AsMemory(0, readLimit),
                        cancellationToken)
                    .ConfigureAwait(false);

            if (read == 0)
            {
                if (expectedBytes < 0)
                {
                    break;
                }

                throw new EndOfStreamException(
                    "Android cerró la transferencia antes de completar el archivo.");
            }

            await output
                .WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);

            received +=
                read;

            if (expectedBytes > 0)
            {
                int percent =
                    (int)Math.Min(
                        100,
                        received * 100 / expectedBytes);

                await Dispatcher
                    .InvokeAsync(
                        () =>
                            row.State =
                                $"Recibiendo {percent}%");
            }
        }
    }

    private ResultRemoteNV ReceiveShareFilesFromRemoteNV(
        string commandPayload)
    {
        if (string.IsNullOrWhiteSpace(commandPayload))
        {
            return ResultRemoteNV.FailNV(
                "Android no envió información de archivos.");
        }

        try
        {
            ShareFilesOfferRemoteNV? offer =
                JsonSerializer.Deserialize<ShareFilesOfferRemoteNV>(
                    commandPayload);

            if (offer?.Files is null ||
                offer.Files.Length == 0)
            {
                return ResultRemoteNV.FailNV(
                    "Android no incluyó archivos para compartir.");
            }

            foreach (ShareFileItemRemoteNV file in offer.Files)
            {
                string transferId =
                    string.IsNullOrWhiteSpace(file.TransferId)
                        ? Guid.NewGuid().ToString("N")
                        : file.TransferId.Trim();

                string name =
                    string.IsNullOrWhiteSpace(file.Name)
                        ? "Archivo Android"
                        : file.Name.Trim();

                string mime =
                    string.IsNullOrWhiteSpace(file.MimeType)
                        ? "tipo desconocido"
                        : file.MimeType.Trim();

                var row =
                    new AndroidShareFileRowNV
                    {
                        TransferId = transferId,
                        Name = name,
                        Detail = $"{FormatAndroidShareSizeNV(file.SizeBytes)} - {mime}",
                        State = "Esperando bytes"
                    };

                _androidShareRowsByTransferNV[transferId] =
                    row;

                _androidShareFilesNV.Insert(0, row);

                _ =
                    MarkAndroidShareTimeoutNVAsync(
                        transferId,
                        row,
                        TimeSpan.FromSeconds(60));
            }

            AndroidShareEmptyText14.Visibility =
                _androidShareFilesNV.Count == 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;

            ShowTopMessage14(
                offer.Files.Length == 1
                    ? $"Android compartió {offer.Files[0].Name}."
                    : $"Android compartió {offer.Files.Length} archivos.",
                MessageKind14.Success);

            return ResultRemoteNV.OkNV(
                offer.Files.Length == 1
                    ? "Archivo autorizado. Enviando bytes a NOVORA PC."
                    : "Archivos autorizados. Enviando bytes a NOVORA PC.");
        }
        catch (JsonException ex)
        {
            return ResultRemoteNV.FailNV(
                $"Oferta de archivos inválida: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ResultRemoteNV.FailNV(
                $"No se pudo registrar el archivo compartido: {ex.Message}");
        }
    }

    private async Task MarkAndroidShareTimeoutNVAsync(
        string transferId,
        AndroidShareFileRowNV row,
        TimeSpan timeout)
    {
        try
        {
            await Task
                .Delay(timeout)
                .ConfigureAwait(false);

            if (!_androidShareRowsByTransferNV.ContainsKey(
                    transferId))
            {
                return;
            }

            await Dispatcher
                .InvokeAsync(
                    () =>
                    {
                        if (string.Equals(
                                row.State,
                                "Esperando bytes",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            row.State =
                                "Sin conexión de bytes";

                            ShowTopMessage14(
                                "Android no abrió el canal de bytes para el archivo compartido.",
                                MessageKind14.Warning);
                        }
                    });
        }
        catch
        {
        }
    }

    private ResultRemoteNV ReceiveShareFileStatusFromRemoteNV(
        string commandPayload)
    {
        if (string.IsNullOrWhiteSpace(
                commandPayload))
        {
            return ResultRemoteNV.FailNV(
                "Android no envió estado de transferencia.");
        }

        try
        {
            ShareFileStatusRemoteNV? status =
                JsonSerializer.Deserialize<ShareFileStatusRemoteNV>(
                    commandPayload);

            if (status is null ||
                string.IsNullOrWhiteSpace(
                    status.TransferId))
            {
                return ResultRemoteNV.FailNV(
                    "Estado de transferencia Android inválido.");
            }

            if (!_androidShareRowsByTransferNV.TryGetValue(
                    status.TransferId,
                    out AndroidShareFileRowNV? row))
            {
                return ResultRemoteNV.OkNV(
                    "Estado de archivo recibido.");
            }

            if (!status.Success)
            {
                row.State =
                    "Error: " +
                    (string.IsNullOrWhiteSpace(
                        status.Message)
                        ? "no se pudo transferir"
                        : status.Message);

                return ResultRemoteNV.OkNV(
                    "Error de archivo registrado en NOVORA PC.");
            }

            return ResultRemoteNV.OkNV(
                "Estado de archivo recibido.");
        }
        catch (JsonException ex)
        {
            return ResultRemoteNV.FailNV(
                $"Estado de archivo inválido: {ex.Message}");
        }
    }

    private static async Task ReadExactlyAndroidShareNVAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int totalRead =
            0;

        while (totalRead < buffer.Length)
        {
            int read =
                await stream
                    .ReadAsync(
                        buffer[totalRead..],
                        cancellationToken)
                    .ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Android cerró el canal de archivo.");
            }

            totalRead +=
                read;
        }
    }

    private static string CreateAndroidShareOutputPathNV(
        string fileName)
    {
        string directory =
            GetAndroidShareDownloadsDirectoryNV();

        Directory.CreateDirectory(directory);

        string safeName =
            MakeSafeAndroidShareFileNameNV(fileName);

        string candidate =
            Path.Combine(directory, safeName);

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        string name =
            Path.GetFileNameWithoutExtension(safeName);

        string extension =
            Path.GetExtension(safeName);

        for (int index = 1; index < 10_000; index++)
        {
            candidate =
                Path.Combine(
                    directory,
                    $"{name} ({index}){extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            "No se pudo crear un nombre libre para el archivo Android.");
    }

    private static string GetAndroidShareDownloadsDirectoryNV()
    {
        string userProfile =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        return Path.Combine(
            userProfile,
            "Downloads",
            "NOVORA",
            "Android Shares");
    }

    private static string MakeSafeAndroidShareFileNameNV(
        string fileName)
    {
        string safe =
            string.IsNullOrWhiteSpace(fileName)
                ? "archivo-android"
                : Path.GetFileName(fileName.Trim());

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            safe =
                safe.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(safe)
            ? "archivo-android"
            : safe;
    }

    private static string FormatAndroidShareSizeNV(
        long bytes)
    {
        if (bytes < 0)
        {
            return "tamano desconocido";
        }

        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB"
        };

        double value =
            bytes;

        int unit =
            0;

        while (value >= 1024 &&
               unit < units.Length - 1)
        {
            value /=
                1024;

            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}
