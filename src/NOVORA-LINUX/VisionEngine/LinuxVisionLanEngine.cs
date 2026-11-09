using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NOVORA.Linux.Control;

namespace NOVORA.Linux.VisionEngine;

internal sealed class LinuxVisionLanEngine : IAsyncDisposable
{
    private readonly IPAddress _address;
    private readonly LinuxTrustStore _trustStore;
    private readonly SemaphoreSlim _exclusiveSessionGate;
    private readonly bool _allowLoopback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LinuxVisionLanSession? _session;
    private CancellationTokenSource? _sessionCancellation;
    private Task _sessionTask = Task.CompletedTask;
    private static long _generation = DateTime.UtcNow.Ticks;
    public LinuxVisionRuntime? Runtime { get; private set; }
    private int _ownsExclusiveGate;
    private int _disposed;
    private string _state = "Stopped";
    private string _message = "VisionEngine LAN está detenido.";

    public LinuxVisionSettings Settings { get; set; } = LinuxVisionSettings.Default;
    public event EventHandler? StatusChanged;
    public bool IsRunning => _state is "Waiting" or "Streaming";
    public bool HasSession => _session is not null;
    public string State => _state;
    public string Message => _message;
    public LinuxVisionTelemetry Telemetry => Runtime?.Telemetry ?? new("Unavailable",0,0,0,0,0);

    public LinuxVisionLanEngine(
        IPAddress address,
        LinuxTrustStore trustStore,
        SemaphoreSlim exclusiveSessionGate,
        bool allowLoopbackForTests = false)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        if (allowLoopbackForTests ? !IPAddress.IsLoopback(address) : !LinuxLanDiscovery.IsPrivateIpv4(address))
            throw new InvalidDataException("VisionEngine LAN requiere una dirección IPv4 privada.");
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _exclusiveSessionGate = exclusiveSessionGate ?? throw new ArgumentNullException(nameof(exclusiveSessionGate));
        _allowLoopback = allowLoopbackForTests;
    }

    public async Task<string> StartAsync(
        bool audioEnabled,
        CancellationToken cancellationToken,
        int bitrate = 8_000_000,
        int maxSize = 1920,
        int fps = 60,
        bool mobileData = false)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        LinuxCaptureCapabilities.ValidateCapture(bitrate, maxSize, fps, mobileData);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasSession)
                throw new InvalidOperationException("Detén la sesión anterior de VisionEngine LAN antes de reiniciarla.");
            if (!await _exclusiveSessionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("VisionEngine ya tiene una sesión activa por otro transporte.");

            Interlocked.Exchange(ref _ownsExclusiveGate, 1);
            LinuxVisionLanSession? session = null;
            try
            {
                session = new LinuxVisionLanSession(_address, _trustStore.Certificate, _allowLoopback);
                Runtime = new LinuxVisionRuntime(Settings,Interlocked.Increment(ref _generation));
                Runtime.Changed += RaiseStatusChanged;
                LinuxVisionLanOffer offer = session.Prepare(
                    bitrate,
                    maxSize,
                    fps,
                    audioEnabled && !mobileData, Runtime.Generation, mobileData);
                _session = session;
                _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _state = "Waiting";
                _message = "VisionEngine LAN espera la autorización de captura y los canales TLS.";
                _sessionTask = RunSessionAsync(session, Runtime, _sessionCancellation.Token);
                RaiseStatusChanged();
                return JsonSerializer.Serialize(offer);
            }
            catch
            {
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
                ReleaseExclusiveGate();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunSessionAsync(LinuxVisionLanSession session,LinuxVisionRuntime runtime,CancellationToken cancellationToken)
    {
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(LinuxVisionLanProtocol.OfferLifetimeSeconds));
            await using var streams=await session.AcceptAsync(timeout.Token).ConfigureAwait(false);
            _state="Streaming"; _message=streams.AudioDegraded ? "VisionEngine LAN conectado sin audio; control y archivos autenticados." : "VisionEngine LAN conectado: video, audio, control y archivos autenticados.";
            RaiseStatusChanged();
            await runtime.RunAsync(streams.Video,streams.Control,streams.Audio,streams.Files,cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { _state="Failed"; _message=$"VisionEngine LAN se detuvo: {ex.Message}"; RaiseStatusChanged(); }
        finally
        {
            _sessionCancellation?.Cancel();
            try { await LinuxVisionCleanup.RunAsync(()=>runtime.DisposeAsync().AsTask(),()=>session.DisposeAsync().AsTask()).ConfigureAwait(false); }
            catch (Exception ex) { _state="Failed"; _message="VE LAN cerró con errores: "+ex.GetBaseException().Message; }
            finally
            {
                _session=null; ReleaseExclusiveGate();
                if (_state is "Waiting" or "Streaming") { _state="Stopped"; _message="VisionEngine LAN detenido."; }
                RaiseStatusChanged();
            }
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            LinuxVisionLanSession? session = _session;
            CancellationTokenSource? cancellation = _sessionCancellation;
            _session = null;
            _sessionCancellation = null;
            cancellation?.Cancel();

            List<Exception>? errors = null;
            if (session is not null)
            {
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { (errors ??= []).Add(ex); }
            }
            try { await _sessionTask.ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            cancellation?.Dispose();
            ReleaseExclusiveGate();
            _state = errors is null ? "Stopped" : "Failed";
            _message = errors is null
                ? "VisionEngine LAN está detenido."
                : $"No se pudo liberar completamente VisionEngine LAN: {string.Join("; ", errors.Select(error => error.Message))}";
            RaiseStatusChanged();
            if (errors is not null)
                throw new AggregateException(_message, errors);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ReleaseExclusiveGate()
    {
        if (Interlocked.Exchange(ref _ownsExclusiveGate, 0) != 0)
            _exclusiveSessionGate.Release();
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

}
