using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace NOVORA.Linux.Control;

/// <summary>The control routes the automatic connector drives (implemented by <see cref="LinuxControlHost"/>).</summary>
internal interface IAutoConnectTarget
{
    bool IsRunning { get; }
    IPAddress? LanAddress { get; }
    bool IsLanAuthorized { get; }
    bool LanBusy { get; }
    bool IsUsbRunning { get; }
    string? UsbSerial { get; }
    bool IsUsbAuthorized { get; }
    /// <summary>The ADB route runs over paired wireless debugging instead of a cable.</summary>
    bool UsbIsWireless { get; }
    Task StartLanAsync(IPAddress address, CancellationToken cancellationToken);
    Task StopLanAsync();
    Task StartUsbAsync(AdbDevice device, CancellationToken cancellationToken);
    Task StopUsbAsync();
}

/// <summary>
/// Keeps LINK Control reachable without asking the user to pick USB or LAN:
/// the LAN listener follows the active private network and every authorized USB phone
/// is bootstrapped when it appears in ADB. Engines are never started here; they run on
/// whichever authorized route exists (see <see cref="LinuxConnectionSelection"/>).
/// </summary>
internal sealed class LinuxAutoConnector : IAsyncDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SessionEndedDelay = TimeSpan.FromSeconds(2);

    private readonly IAutoConnectTarget _target;
    private readonly Func<IReadOnlyList<AdbDevice>?> _devices;
    private readonly Func<IReadOnlyList<LanCandidate>> _lanCandidates;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly SemaphoreSlim _evaluation = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, byte> _pausedUsb = new(StringComparer.Ordinal);
    private Task _run = Task.CompletedTask;
    private int _started;
    private volatile bool _lanPaused;
    private volatile string? _preferredSerial;

    private int _usbAttempts;
    private string? _attemptSerial;
    private DateTimeOffset _usbNextAttempt;
    private DateTimeOffset? _usbPendingSince;
    private bool _usbWasAuthorized;
    private int _lanFailures;
    private DateTimeOffset _lanNextAttempt;
    private bool _everConnected;
    private string? _lastLanNotice;
    private string? _lastUsbNotice;

    public LinuxAutoConnector(
        IAutoConnectTarget target,
        Func<IReadOnlyList<AdbDevice>?> devices,
        Func<IReadOnlyList<LanCandidate>> lanCandidates,
        TimeProvider? time = null)
    {
        _target = target;
        _devices = devices;
        _lanCandidates = lanCandidates;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Short state for the Home card: «Conectado por USB», «Reconectando…», …</summary>
    public string Summary { get; private set; } = "Buscando teléfono…";
    public bool LanPaused => _lanPaused;
    public int UsbAttempts => _usbAttempts;
    /// <summary>Raised from a worker thread when <see cref="Summary"/> may have changed.</summary>
    public event Action? Changed;
    /// <summary>A user-facing explanation, raised once per distinct situation.</summary>
    public event Action<string, bool>? Notice;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _run = Task.Run(RunAsync);
    }

    /// <summary>Requests an immediate re-evaluation (ADB event, network change, session change).</summary>
    public void Signal()
    {
        if (_wake.CurrentCount < 4) _wake.Release();
    }

    /// <summary>A manual LAN stop keeps LAN off until the user prepares it again.</summary>
    public void PauseLan(bool paused)
    {
        _lanPaused = paused;
        if (!paused) _lanFailures = 0;
        Signal();
    }

    /// <summary>A manual USB stop leaves that phone alone until it is unplugged or linked manually.</summary>
    public void PauseUsb(string serial)
    {
        _pausedUsb[serial] = 0;
        Signal();
    }

    /// <summary>A manual USB link: prefer this phone and restore the bounded attempts.</summary>
    public void PreferUsb(string serial)
    {
        _preferredSerial = serial;
        _pausedUsb.TryRemove(serial, out _);
        _usbAttempts = 0;
        _usbNextAttempt = default;
        _attemptSerial = serial;
        Signal();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await EvaluateOnceAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report("La conexión automática encontró un error y lo volverá a intentar: " + ex.Message, true);
            }
            try
            {
                await _wake.WaitAsync(Tick, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One serialized pass over LAN and USB. Public for deterministic tests.</summary>
    public async Task EvaluateOnceAsync(CancellationToken cancellationToken)
    {
        await _evaluation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EvaluateLanAsync(cancellationToken).ConfigureAwait(false);
            await EvaluateUsbAsync(cancellationToken).ConfigureAwait(false);
            UpdateSummary();
        }
        finally { _evaluation.Release(); }
    }

    private async Task EvaluateLanAsync(CancellationToken cancellationToken)
    {
        if (_lanPaused) return;
        IReadOnlyList<LanCandidate> candidates;
        try { candidates = _lanCandidates(); }
        catch (NetworkInformationException) { candidates = []; }

        IPAddress? current = _target.IsRunning ? _target.LanAddress : null;
        LanCandidate? choice = LinuxAutoConnectPolicy.ChooseLan(candidates, current);
        if (_target.IsRunning)
        {
            if (current is null || choice is not null && choice.Address.Equals(current))
                return;
            // The listener's address left this PC: its sessions are already gone.
            try { await _target.StopLanAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { ReportLan("La red cambió y el control LAN anterior se cerró con un error: " + ex.Message, true); }
            if (choice is null)
            {
                ReportLan("La red LAN se desconectó. Se usará USB si el teléfono está conectado por cable.", true);
                return;
            }
        }
        if (choice is null)
        {
            ReportLan("No hay una red LAN privada. Conecta el cable USB o únete a la misma Wi-Fi que el teléfono.", false);
            return;
        }
        if (_time.GetUtcNow() < _lanNextAttempt) return;
        try
        {
            await _target.StartLanAsync(choice.Address, cancellationToken).ConfigureAwait(false);
            _lanFailures = 0;
            ReportLan($"Control LAN automático en {choice.InterfaceName} · {choice.Address}.", false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _lanFailures++;
            _lanNextAttempt = _time.GetUtcNow() + LinuxAutoConnectPolicy.RetryDelay(_lanFailures);
            ReportLan("No se pudo preparar el control LAN automático: " + ex.Message, true);
        }
    }

    private async Task EvaluateUsbAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AdbDevice>? devices = _devices();
        if (devices is null)
            return; // ADB state unknown: never tear down a route on missing information.

        foreach (string serial in _pausedUsb.Keys)
            if (!devices.Any(device => device.Serial == serial))
                _pausedUsb.TryRemove(serial, out _);
        if (_attemptSerial is not null && !devices.Any(device => device.Serial == _attemptSerial))
        {
            // Unplugging resets the bounded bootstrap attempts for the next plug-in.
            _attemptSerial = null;
            _usbAttempts = 0;
            _usbNextAttempt = default;
        }

        DateTimeOffset now = _time.GetUtcNow();
        bool running = _target.IsUsbRunning;
        bool authorized = running && _target.IsUsbAuthorized;
        if (authorized)
        {
            _usbWasAuthorized = true;
            _usbPendingSince = null;
            _usbAttempts = 0;
        }
        bool sessionEnded = running && !authorized && _usbWasAuthorized;
        if (running && !authorized && !sessionEnded)
            _usbPendingSince ??= now;

        UsbAutoPlan plan = LinuxAutoConnectPolicy.DecideUsb(new UsbAutoState(
            devices,
            running ? _target.UsbSerial : null,
            authorized,
            sessionEnded,
            _usbPendingSince is { } since ? now - since : TimeSpan.Zero,
            _target.IsLanAuthorized,
            _target.LanBusy,
            _preferredSerial,
            new HashSet<string>(_pausedUsb.Keys, StringComparer.Ordinal),
            _usbAttempts,
            now >= _usbNextAttempt));

        switch (plan.Decision)
        {
            case UsbAutoDecision.Stop:
                await StopUsbAsync().ConfigureAwait(false);
                ReportUsb("El teléfono USB se desconectó. NOVORA seguirá por LAN si LINK Control está en la misma red.", false);
                break;
            case UsbAutoDecision.Restart:
                if (sessionEnded)
                {
                    _usbNextAttempt = now + SessionEndedDelay;
                    ReportUsb("LINK Control cerró la sesión USB; reconectando…", false);
                }
                else
                {
                    _usbAttempts++;
                    _usbNextAttempt = now + LinuxAutoConnectPolicy.RetryDelay(_usbAttempts);
                    ReportUsb("LINK Control no confirmó el enlace USB; se volverá a intentar.", true);
                }
                await StopUsbAsync().ConfigureAwait(false);
                Signal();
                break;
            case UsbAutoDecision.Start when plan.Device is { } device:
                _attemptSerial = device.Serial;
                try
                {
                    await _target.StartUsbAsync(device, cancellationToken).ConfigureAwait(false);
                    _usbPendingSince = _time.GetUtcNow();
                    _usbWasAuthorized = false;
                    ReportUsb($"Enlazando automáticamente por {(device.IsUsb ? "USB" : "ADB Wi-Fi")} con {DeviceName(device)}…", false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _usbAttempts++;
                    _usbNextAttempt = _time.GetUtcNow() + LinuxAutoConnectPolicy.RetryDelay(_usbAttempts);
                    ReportUsb($"No se pudo enlazar por USB con {DeviceName(device)}: {ex.Message}", true);
                }
                break;
            case UsbAutoDecision.WaitAmbiguous:
                ReportUsb("Hay varios teléfonos por USB. Elige uno en TIPO DE CONEXIÓN › USB (cable).", true);
                break;
            case UsbAutoDecision.WaitLanBusy:
                ReportUsb("Teléfono USB detectado. La sesión LAN continúa mientras tenga motores activos.", false);
                break;
            case UsbAutoDecision.WaitGaveUp:
                ReportUsb("LINK Control no confirmó el enlace USB. Reconecta el cable o pulsa «Enlazar por USB».", true);
                break;
        }
    }

    private async Task StopUsbAsync()
    {
        _usbWasAuthorized = false;
        _usbPendingSince = null;
        try { await _target.StopUsbAsync().ConfigureAwait(false); }
        // An unplugged phone cannot answer «adb reverse --remove»; its routes died with the cable.
        catch (Exception ex) when (ex is not OutOfMemoryException) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private void UpdateSummary()
    {
        bool usb = _target.IsUsbAuthorized, lan = _target.IsLanAuthorized;
        if (usb || lan) _everConnected = true;
        string summary =
            usb ? (_target.UsbIsWireless ? "Conectado por ADB Wi-Fi" : "Conectado por USB") :
            lan ? "Conectado por LAN" :
            _target.IsUsbRunning ? (_target.UsbIsWireless ? "Conectando por ADB Wi-Fi…" : "Conectando por USB…") :
            _everConnected ? "Reconectando…" :
            _target.IsRunning ? "Esperando teléfono (USB o Wi-Fi)" :
            "Sin conexión";
        if (summary == Summary) return;
        Summary = summary;
        Changed?.Invoke();
    }

    private void ReportLan(string message, bool warning) => Report(ref _lastLanNotice, message, warning);
    private void ReportUsb(string message, bool warning) => Report(ref _lastUsbNotice, message, warning);

    private void Report(string message, bool warning)
    {
        string? last = null;
        Report(ref last, message, warning);
    }

    private void Report(ref string? last, string message, bool warning)
    {
        if (message == last) return;
        last = message;
        Notice?.Invoke(message, warning);
    }

    private static string DeviceName(AdbDevice device) =>
        string.IsNullOrWhiteSpace(device.Model) ? device.Serial : device.Model;

    private void OnNetworkChanged(object? sender, EventArgs e) => Signal();
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Signal();

    public async ValueTask DisposeAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _stop.Cancel();
        try { await _run.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        // Let a pass that is still finishing release the gate before disposal.
        await _evaluation.WaitAsync().ConfigureAwait(false);
        _evaluation.Release();
        _stop.Dispose();
    }
}
