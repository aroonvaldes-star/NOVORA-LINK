using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NOVORA.Linux.Files;
using NOVORA.Linux.VisionEngine;
using AvaloniaControl = Avalonia.Controls.Control;

namespace NOVORA.Linux;

/// <summary>
/// Windows ExchangeVE on Linux: Ctrl+C / Ctrl+V in the video window, files dropped on the video
/// window or on Integración, RECIBIR CARPETA NOVORA, the per-session transfer summary and the
/// live VE measurements. Clipboard contents are never displayed, logged or stored.
/// </summary>
public sealed partial class MainWindow
{
    private readonly DispatcherTimer _veExchangeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private LinuxVisionRuntime? _veExchangeRuntime;
    private LinuxVisionTransferSummary _veTransfers = new();
    private readonly Queue<string> _veSendQueue = new();
    private bool _veSending;
    private Button? _veReceiveFolderButton;
    private bool _veReceivingFolder;
    private TextBlock? _integrationFilesStatus;
    private Button? _integrationRestartButton;
<<<<<<< Updated upstream
    private (long At, long Frames, long Bytes)? _veTelemetrySample;
    private (double Fps, double Mbps) _veTelemetryRates;
=======
>>>>>>> Stashed changes

    private void InitializeVisionExchangeVE()
    {
        _veExchangeTimer.Tick += (_, _) => SyncVisionExchangeVE();
        _veExchangeTimer.Start();
        Closed += (_, _) => { _veExchangeTimer.Stop(); AttachVisionExchangeVE(null); };
    }

    private void SyncVisionExchangeVE()
    {
        LinuxVisionRuntime? runtime = _host.VisionRuntime;
        if (!ReferenceEquals(runtime, _veExchangeRuntime)) AttachVisionExchangeVE(runtime);
        if (_veReceiveFolderButton is not null) _veReceiveFolderButton.IsEnabled = !_veReceivingFolder;
        UpdateFilesStatusVE();
    }

    /// <summary>Shows the FILES channel state in Integración: healthy, waiting, or closed with its real cause.</summary>
    private void UpdateFilesStatusVE()
    {
        LinuxVisionRuntime? runtime = _host.VisionRuntime;
        bool closed = runtime?.Files.ClosedAfterFailure == true;
        bool capturing = _host.HasVisionSession || _host.VisionRequested;
        if (_integrationFilesStatus is { } status)
        {
            status.Text = runtime is null ? "Archivos: inicia VisionEngine para enviar o recibir." : runtime.Files.Status;
            status.Foreground = Brush(closed ? "#f87171" : runtime?.Files.IsConnected == true ? "#22c55e" : "#a8b7ca");
        }
        if (_integrationRestartButton is { } restart)
        {
            restart.IsEnabled = capturing;
            restart.Content = closed ? "REINICIAR CAPTURA (ARCHIVOS CERRADOS)" : "REINICIAR CAPTURA";
        }
    }

    /// <summary>Stops and restarts capture so the phone opens a fresh FILES channel. The phone must authorize again.</summary>
    private async Task RestartCaptureVEAsync()
    {
        try
        {
            SetMessage("Reiniciando captura para reconectar archivos…", "#fbbf24");
            await _host.StopVisionAsync();
            _host.RequestVision();
            SetMessage("Captura reiniciada: autoriza la captura en el teléfono para reconectar archivos.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            SetMessage("No se pudo reiniciar la captura: " + ex.Message, "#f87171");
        }
        UpdateFilesStatusVE();
    }

    private void AttachVisionExchangeVE(LinuxVisionRuntime? runtime)
    {
        if (_veExchangeRuntime is { } old)
        {
            old.ClipboardShortcut -= OnVisionClipboardShortcutVE;
            old.ClipboardReceived -= OnVisionClipboardReceivedVE;
            old.FilesDropped -= OnVisionFilesDroppedVE;
            old.Transfers.Changed -= OnVisionTransfersChangedVE;
        }
        _veExchangeRuntime = runtime;
        if (runtime is null) return;
        // "Archivos de esta sesión": a new VE session starts a new summary, as a new pairing does on Windows.
        _veTransfers = runtime.Transfers;
        runtime.ClipboardShortcut += OnVisionClipboardShortcutVE;
        runtime.ClipboardReceived += OnVisionClipboardReceivedVE;
        runtime.FilesDropped += OnVisionFilesDroppedVE;
        runtime.Transfers.Changed += OnVisionTransfersChangedVE;
        _veSendQueue.Clear();
        UpdateIntegrationSummaryVE();
    }

    private void OnVisionTransfersChangedVE() => Dispatcher.UIThread.Post(UpdateIntegrationSummaryVE);

    private void UpdateIntegrationSummaryVE()
    {
        _integrationSentSummary.Text = _veTransfers.SentText;
        _integrationReceivedSummary.Text = _veTransfers.ReceivedText;
        _integrationSentNames.Text = _veTransfers.SentNames;
        _integrationReceivedNames.Text = _veTransfers.ReceivedNames;
    }

    // ============================================================
    // CLIPBOARD (Windows VisionExchange_KeyDownVE / VisionClipboard_ChangedVE)
    // ============================================================

    private void OnVisionClipboardShortcutVE(bool copy)
    {
        if (copy)
        {
            try { _veExchangeRuntime?.RequestClipboard(); }
            catch (InvalidOperationException ex) { SetMessage("Clipboard Android → Linux: " + ex.Message, "#fbbf24"); }
            return;
        }
        _ = PasteToAndroidVEAsync();
    }

    private void OnVisionClipboardReceivedVE(string text) => Dispatcher.UIThread.Post(async () =>
    {
        if (Clipboard is not { } clipboard) return;
        try
        {
            if (string.IsNullOrEmpty(text)) await clipboard.ClearAsync();
            else await clipboard.SetTextAsync(text);
            SetMessage("Clipboard Android → Linux actualizado.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetMessage("Clipboard Linux ocupado; cambio no aplicado.", "#fbbf24");
        }
    });

    private async Task PasteToAndroidVEAsync()
    {
        if (_veExchangeRuntime is not { IsStreaming: true } runtime)
        {
            SetMessage("ExchangeVE requiere VisionEngine activo.", "#fbbf24");
            return;
        }
        if (Clipboard is not { } clipboard) return;
        try
        {
            IReadOnlyList<string> paths = await ReadClipboardPathsVEAsync(clipboard);
            if (paths.Count > 0)
            {
                QueueVisionPathsVE(paths);
                return;
            }
            string? text = await clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(text))
            {
                SetMessage("ExchangeVE: el portapapeles no contiene contenido compatible.", "#fbbf24");
                return;
            }
            await runtime.SetClipboardAsync(text, paste: true, CancellationToken.None);
            SetMessage("ExchangeVE: contenido del portapapeles enviado.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or TimeoutException or OperationCanceledException)
        {
            SetMessage("ExchangeVE: " + ex.Message, "#f87171");
        }
    }

    /// <summary>Copied files from Linux file managers: Avalonia Files, text/uri-list or GNOME's copied-files list.</summary>
    private static async Task<IReadOnlyList<string>> ReadClipboardPathsVEAsync(Avalonia.Input.Platform.IClipboard clipboard)
    {
        string[] formats;
        try { formats = await clipboard.GetFormatsAsync() ?? []; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return []; }
        foreach (string format in new[] { DataFormats.Files, "text/uri-list", "x-special/gnome-copied-files" })
        {
            if (!formats.Contains(format)) continue;
            object? data;
            try { data = await clipboard.GetDataAsync(format); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
            List<string> paths = ClipboardDataPathsVE(data);
            if (paths.Count > 0) return paths;
        }
        // Some managers publish only text: accept it as paths only when every line is a local file URI.
        string? text;
        try { text = await clipboard.GetTextAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return []; }
        return ParseUriListVE(text, requireAll: true);
    }

    internal static List<string> ClipboardDataPathsVE(object? data) => data switch
    {
        IEnumerable<IStorageItem> items => items.Select(item => item.TryGetLocalPath()).OfType<string>().ToList(),
        string text => ParseUriListVE(text, requireAll: false),
        byte[] bytes => ParseUriListVE(System.Text.Encoding.UTF8.GetString(bytes), requireAll: false),
        IEnumerable<string> names => names.Where(Path.IsPathFullyQualified).ToList(),
        _ => []
    };

    internal static List<string> ParseUriListVE(string? text, bool requireAll)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return paths;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimEnd('\0');
            if (line.Length == 0 || line.StartsWith('#') || line is "copy" or "cut") continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile && Path.IsPathFullyQualified(uri.LocalPath))
                paths.Add(uri.LocalPath);
            else if (requireAll)
                return [];
        }
        return paths;
    }

    // ============================================================
    // FILES (Windows VisionPresentation_FilesDroppedVE / IntegrationFiles_Drop)
    // ============================================================

    private void OnVisionFilesDroppedVE(string[] paths)
    {
        SetMessage(paths.Length == 1
            ? "VisionEngine: archivo recibido para transferencia."
            : $"VisionEngine: {paths.Length} elementos recibidos para transferencia.");
        QueueVisionPathsVE(paths);
    }

    private void QueueVisionPathsVE(IReadOnlyCollection<string> paths)
    {
        if (_host.VisionRuntime is not { } runtime || !runtime.Files.IsConnected)
        {
            // Dropped files are refused, not queued: say why and what to do, next to the VE window too.
            bool closed = _host.VisionRuntime?.Files.ClosedAfterFailure == true;
            string text = closed
                ? "El canal de archivos se cerró; el archivo no se envió. Pulsa REINICIAR CAPTURA en Integración."
                : "VisionEngine debe estar activo para transferir archivos; el archivo no se envió.";
            SetMessage(text, "#fbbf24");
            _host.VisionRuntime?.ReportFilesRejected(text);
            return;
        }
        foreach (string path in paths) _veSendQueue.Enqueue(path);
        if (paths.Count > 1) SetMessage($"{paths.Count} elementos agregados a la transferencia.");
        if (!_veSending) _ = DrainVisionSendQueueVEAsync(runtime);
    }

    private async Task DrainVisionSendQueueVEAsync(LinuxVisionRuntime runtime)
    {
        _veSending = true;
        using var stop = new CancellationTokenSource();
        bool ownsCancel = _veTransfer is null;
        if (ownsCancel) _veTransfer = stop;
        RefreshFunctionalVisionState();
        try
        {
            while (_veSendQueue.TryDequeue(out string? path))
            {
                if (!ReferenceEquals(runtime, _host.VisionRuntime)) break;
                CancellationToken token = ownsCancel ? stop.Token : _veTransfer?.Token ?? stop.Token;
                await runtime.Files.SendAsync(path, token);
            }
            SetMessage("ExchangeVE: transferencia completada.");
        }
        catch (OperationCanceledException) { _veSendQueue.Clear(); SetMessage("Envío cancelado.", "#fbbf24"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
        {
            _veSendQueue.Clear();
            SetMessage("Archivos NOVORA: " + LinuxFileDiagnostics.Describe(ex), "#f87171");
        }
        finally
        {
            _veSending = false;
            if (ownsCancel && ReferenceEquals(_veTransfer, stop)) _veTransfer = null;
            RefreshFunctionalVisionState();
        }
    }

    /// <summary>Wires the Integración "Archivos / Share" card: drop target and RECIBIR CARPETA NOVORA.</summary>
    private void ConfigureIntegrationExchangeVE(AvaloniaControl dropTarget, Button receiveFolder)
    {
        DragDrop.SetAllowDrop(dropTarget, true);
        dropTarget.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        dropTarget.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            e.Handled = true;
            string[] paths = e.Data.GetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            if (paths.Length == 0) return;
            QueueVisionPathsVE(paths);
            if (paths.Length == 1 && _host.VisionRuntime?.Files.IsConnected == true) SetMessage("Archivo agregado a la transferencia.");
        });
        _veReceiveFolderButton = receiveFolder;
        receiveFolder.IsEnabled = !_veReceivingFolder;
        receiveFolder.Click += async (_, _) => await ReceiveNovoraFolderVEAsync();
        UpdateIntegrationSummaryVE();
    }

    private async Task ReceiveNovoraFolderVEAsync()
    {
        if (_veReceivingFolder) return;
        if (_host.SelectedTransport == "LAN")
        {
            SetMessage("ExchangeVE: " + LinuxNovoraFolder.LanMessage, "#fbbf24");
            return;
        }
        if (!_host.IsUsbAuthorized || _host.UsbSerial is not { } serial)
        {
            SetMessage("Conecta y autoriza un teléfono por USB antes de recibir archivos.", "#fbbf24");
            return;
        }
        _veReceivingFolder = true; SyncVisionExchangeVE();
        SetMessage("ExchangeVE: recibiendo /sdcard/NOVORA desde Android...");
        try
        {
            var result = await LinuxNovoraFolder.PullAsync(serial, _host.VisionSettings.ReceiveDirectory, CancellationToken.None);
            if (result.Success)
            {
                _veTransfers.RecordReceived(result.Files, result.Bytes, LinuxNovoraFolder.AndroidRoot);
                SetMessage($"ExchangeVE: carpeta NOVORA recibida en {result.Destination}.");
            }
            else SetMessage("ExchangeVE: " + result.Message, "#f87171");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetMessage("ExchangeVE: " + ex.Message, "#f87171");
        }
        finally { _veReceivingFolder = false; SyncVisionExchangeVE(); }
    }

<<<<<<< Updated upstream
    // ============================================================
    // LIVE MEASUREMENTS (Windows UpdateVisionTelemetryText14)
    // ============================================================

    private string VisionLiveTelemetryTextVE()
    {
        LinuxVisionTelemetry? t = _host.VisionTelemetry;
        if (t is null || t.FramesRendered == 0)
        {
            _veTelemetrySample = null;
            return _host.VisionState is "Waiting" or "Streaming" || _host.HasVisionSession
                ? "La pantalla se está preparando."
                : "La pantalla aún no se está mostrando.";
        }
        long now = Stopwatch.GetTimestamp();
        if (_veTelemetrySample is { } previous && now > previous.At)
        {
            double seconds = (now - previous.At) / (double)Stopwatch.Frequency;
            if (seconds >= 0.5)
            {
                _veTelemetryRates = (Math.Max(0, t.FramesRendered - previous.Frames) / seconds,
                    Math.Max(0, t.VideoBytesReceived - previous.Bytes) * 8 / seconds / 1_000_000d);
                _veTelemetrySample = (now, t.FramesRendered, t.VideoBytesReceived);
            }
        }
        else _veTelemetrySample = (now, t.FramesRendered, t.VideoBytesReceived);
        return VisionLiveTelemetryFormatVE(t, _veTelemetryRates.Fps, _veTelemetryRates.Mbps);
    }

    internal static string VisionLiveTelemetryFormatVE(LinuxVisionTelemetry t, double fps, double mbps) =>
        $"Monitor VE: salida {t.OutputWidth} × {t.OutputHeight} · {fps:F1} FPS · {mbps:F1} Mbps · " +
        $"paquetes {t.VideoPacketsReceived:N0}, renderizados {t.FramesRendered:N0}, sin presentar aprox. {t.FramesDroppedEstimate:N0}.\n" +
        $"Decoder: {t.DecoderBackend} · SDL: {t.RendererBackend} · audio: {(t.AudioPlaying ? "activo" : "sin reproducción")}\n{t.InputStatus}";
=======
>>>>>>> Stashed changes
}
