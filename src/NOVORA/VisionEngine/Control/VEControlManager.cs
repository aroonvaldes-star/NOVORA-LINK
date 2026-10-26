using NOVORA.Control;
using NOVORA.Contracts.Input;
using NOVORA.Network;
using NOVORA.VisionEngine.Transport;
using System.IO;

namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Canal bidireccional de input, clipboard y UHID de VisionEngine.
/// </summary>
public sealed class VEControlManager : IAsyncDisposable, INLInputOutput, INLInputUiOutput
{
    private readonly SemaphoreSlim _sendGateVE = new(1, 1);
    private readonly object _statusGateVE = new();
    private Stream? _streamVE;
    private CancellationTokenSource? _readCtsVE;
    private Task? _readTaskVE;
    private VEControlStatus _statusVE = VEControlStatus.CreateInitialVE();
    private long _sentVE;
    private long _sentBytesVE;
    private long _receivedVE;
    private long _receivedBytesVE;
    private long _clipboardVE;
    private long _errorsVE;
    private long _lastStatusPublishMsVE;
    private Func<VEControlMessage, bool>? _canSendMessageVE;
    private Func<bool>? _canReceiveClipboardVE;
    private bool _disposedVE;
    private readonly SemaphoreSlim _captureGateVE = new(1,1);
    private readonly object _captureSyncVE = new();
    private (long Generation,long Sequence,int Bitrate,TaskCompletionSource<NLControlCapturePolicyAck?> Completion)? _capturePendingVE;
    private VECaptureTelemetrySnapshot? _captureTelemetryVE;
    public VECaptureTelemetrySnapshot? CaptureTelemetrySnapshotVE => Volatile.Read(ref _captureTelemetryVE);
    public NLControlCaptureTelemetry? CaptureTelemetryVE => CaptureTelemetrySnapshotVE?.Telemetry;
    private readonly object _networkProbeSyncVE = new();
    private NLNetworkControlProbe? _networkProbeVE;
    private CancellationTokenSource? _networkProbeCancellationVE;
    private Task? _networkProbeTaskVE;
    private long _networkProbeSequenceVE;
    public IReadOnlyList<NLNetworkSample> NetworkProbeSamplesVE
    {
        get { lock (_networkProbeSyncVE) return _networkProbeVE?.Samples ?? Array.Empty<NLNetworkSample>(); }
    }
    /// <summary>Phone monotonic clock minus PC Stopwatch clock (µs), when the echo reports it.</summary>
    public long? DeviceClockOffsetUsVE
    {
        get { lock (_networkProbeSyncVE) return _networkProbeVE?.DeviceClockOffsetUs; }
    }

    public void ConfigureNetworkProbeVE(long generation, string linkKey, NLControlNetworkCapabilities? capabilities,
        TimeProvider? clock = null)
    {
        lock (_networkProbeSyncVE)
        {
            if (!IsReadyVE || capabilities is not { Version: 1, StabilityTelemetry: true } || generation <= 0)
            { StopNetworkProbeVE(); return; }
            if (_networkProbeVE is { } current && current.Generation == generation && current.LinkKey == linkKey) return;
            var source = linkKey.StartsWith("VE:LAN:", StringComparison.Ordinal) ? NLNetworkSampleSource.LanControl :
                linkKey.StartsWith("VE:USB:", StringComparison.Ordinal) ? NLNetworkSampleSource.UsbControl :
                throw new ArgumentException("El eco requiere una ruta VE USB o LAN autorizada.");
            var probe = new NLNetworkControlProbe(generation, linkKey, source, clock);
            StopNetworkProbeVE();
            _networkProbeVE = probe;
            var controlLifetime = _readCtsVE!.Token;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(controlLifetime);
            _networkProbeCancellationVE = cancellation;
            Task? previous = _networkProbeTaskVE;
            _networkProbeTaskVE = Task.Run(async () =>
            {
                // A replaced producer releases its writer before the new route can send.
                if (previous is not null) { try { await previous.ConfigureAwait(false); } catch { } }
                await RunNetworkProbeVE(probe, cancellation, controlLifetime).ConfigureAwait(false);
            });
        }
    }

    public void StopNetworkProbeVE()
    {
        lock (_networkProbeSyncVE)
        {
            _networkProbeVE = null;
            _networkProbeCancellationVE?.Cancel();
            _networkProbeCancellationVE = null;
        }
    }

    private async Task RunNetworkProbeVE(NLNetworkControlProbe probe, CancellationTokenSource cancellation, CancellationToken controlLifetime)
    {
        var ct = cancellation.Token;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), probe.Clock);
            do
            {
                ct.ThrowIfCancellationRequested();
                probe.Expire();
                // Observation never queues ahead of input or keeps its writer while awaiting a reply.
                if (!await _sendGateVE.WaitAsync(0, ct).ConfigureAwait(false)) continue;
                try
                {
                    Stream? stream;
                    ulong sequence = unchecked((ulong)Interlocked.Increment(ref _networkProbeSequenceVE));
                    var message = VEControlMessage.SimpleVE(VEControlType.NetworkEcho) with { Sequence = sequence };
                    if (_canSendMessageVE is not null && !_canSendMessageVE(message)) continue;
                    lock (_networkProbeSyncVE)
                    {
                        stream = _streamVE;
                        if (!ReferenceEquals(probe, _networkProbeVE) || !IsReadyVE || stream is null || !probe.TryBegin(sequence)) continue;
                    }
                    var command = new NLControlInputCommand((int)VEControlType.NetworkEcho, Sequence: sequence);
                    // Disabling/reconfiguring observation must finish a started frame. A stalled
                    // control write has its own deadline; uncertain framing closes that channel.
                    using var writeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2), probe.Clock);
                    using var writeLifetime = CancellationTokenSource.CreateLinkedTokenSource(controlLifetime, writeDeadline.Token);
                    try { await NLControlProtocol.WriteAsync(stream, command, writeLifetime.Token).ConfigureAwait(false); }
                    catch (Exception ex) when (!controlLifetime.IsCancellationRequested)
                    {
                        FailNetworkProbeWriteVE(stream, writeDeadline.IsCancellationRequested
                            ? new TimeoutException("El eco no pudo completar su frame de control en 2 segundos.", ex) : ex);
                        return;
                    }
                    Interlocked.Increment(ref _sentVE);
                    Interlocked.Add(ref _sentBytesVE, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(command).Length + 4);
                }
                finally { _sendGateVE.Release(); }
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || controlLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lock (_networkProbeSyncVE)
                if (ReferenceEquals(probe, _networkProbeVE))
                    PublishVE(VEControlStates.Failed, "Falló la escritura del eco de control VisionEngine.", ex.Message);
        }
        finally
        {
            lock (_networkProbeSyncVE)
                if (ReferenceEquals(cancellation, _networkProbeCancellationVE))
                { _networkProbeVE = null; _networkProbeCancellationVE = null; }
            cancellation.Dispose();
        }
    }

    private void FailNetworkProbeWriteVE(Stream stream, Exception exception)
    {
        lock (_networkProbeSyncVE)
        {
            if (!ReferenceEquals(stream, _streamVE)) return;
            StopNetworkProbeVE();
        }
        Interlocked.Increment(ref _errorsVE);
        PublishVE(VEControlStates.Failed, "Falló la escritura del eco de control VisionEngine; canal cerrado por encuadre incierto.", exception.Message);
        try { stream.Dispose(); } catch { }
    }

    public async Task<NLControlCapturePolicyAck?> ApplyCapturePolicyAsync(NLControlCapturePolicy policy, CancellationToken ct=default)
    {
        await _captureGateVE.WaitAsync(ct).ConfigureAwait(false);
        var pending=new TaskCompletionSource<NLControlCapturePolicyAck?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            lock(_captureSyncVE) _capturePendingVE=(policy.Generation,policy.Sequence,policy.TargetBitrate,pending);
            await SendAsync(VEControlMessage.CapturePolicyVE(policy),ct).ConfigureAwait(false);
            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(2),ct).ConfigureAwait(false);
        }
        catch(TimeoutException) { return null; }
        finally
        {
            lock(_captureSyncVE) if(_capturePendingVE?.Completion==pending) _capturePendingVE=null;
            _captureGateVE.Release();
        }
    }

    public event EventHandler<VEControlStatus>? StatusChangedVE;
    public event EventHandler<string>? ClipboardChangedVE;
    public event EventHandler<ulong>? ClipboardAcknowledgedVE;

    public VEControlStatus StatusVE
    {
        get { lock (_statusGateVE) return _statusVE; }
    }

    public bool IsReadyVE => StatusVE.State == VEControlStates.Ready;
    bool INLInputOutput.IsReady => IsReadyVE;

    Task INLInputOutput.CreateAsync(NLInputDevice device, byte[] descriptor, CancellationToken cancellationToken) =>
        SendAsync(VEControlMessage.UhidCreateVE(device.DeviceId, device.VendorId, device.ProductId, device.Name, descriptor), cancellationToken);
    Task INLInputOutput.SendAsync(ushort deviceId, byte[] report, CancellationToken cancellationToken) =>
        SendAsync(VEControlMessage.UhidInputVE(deviceId, report), cancellationToken);
    Task INLInputOutput.DestroyAsync(ushort deviceId, CancellationToken cancellationToken) =>
        SendAsync(VEControlMessage.UhidDestroyVE(deviceId), cancellationToken);
    async Task INLInputUiOutput.SendUiActionAsync(NLInputUiAction action, CancellationToken cancellationToken)
    {
        uint? keycode = action switch
        {
            NLInputUiAction.Up => 19,
            NLInputUiAction.Down => 20,
            NLInputUiAction.Left => 21,
            NLInputUiAction.Right => 22,
            NLInputUiAction.Select => 23,
            _ => null
        };

        if (action == NLInputUiAction.Back)
        {
            await SendAsync(VEControlMessage.BackOrScreenOnVE(VEControlActionKey.Down), cancellationToken).ConfigureAwait(false);
            await SendAsync(VEControlMessage.BackOrScreenOnVE(VEControlActionKey.Up), cancellationToken).ConfigureAwait(false);
        }
        else if (keycode is uint value)
        {
            await SendAsync(VEControlMessage.KeycodeVE(VEControlActionKey.Down, value), cancellationToken).ConfigureAwait(false);
            await SendAsync(VEControlMessage.KeycodeVE(VEControlActionKey.Up, value), cancellationToken).ConfigureAwait(false);
        }
    }

    public void SetPrivacyGatesVE(
        Func<VEControlMessage, bool>? canSendMessage,
        Func<bool>? canReceiveClipboard)
    {
        _canSendMessageVE = canSendMessage;
        _canReceiveClipboardVE = canReceiveClipboard;
    }

    public Task StartNativeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (_streamVE is not null)
            throw new InvalidOperationException("Control VisionEngine ya está iniciado.");
        if (!stream.CanWrite)
            throw new ArgumentException("El canal AppControl no permite escribir.", nameof(stream));

        _streamVE = stream;
        _readCtsVE = new CancellationTokenSource();
        _readTaskVE = RunNativeReaderVE(stream, _readCtsVE.Token);
        PublishVE(VEControlStates.Ready, "Control VisionEngine listo por AppControl.", null);
        return Task.CompletedTask;
    }

    public async Task SendAsync(VEControlMessage message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(message);

        if (_canSendMessageVE is not null && !_canSendMessageVE(message))
            return;

        Stream stream = _streamVE
            ?? throw new InvalidOperationException("Control VisionEngine no está iniciado.");

        await _sendGateVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NLControlInputCommand command = ToNativeCommandVE(message);
            int sentBytes = await NLControlProtocol.WriteFrameAsync(stream, command, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _sentVE);
            Interlocked.Add(ref _sentBytesVE, sentBytes);
            PublishSnapshotVE();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorsVE);
            PublishVE(VEControlStates.Failed, "Falló la escritura del canal de control VisionEngine.", ex.Message);
            throw;
        }
        finally
        {
            _sendGateVE.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? probeTask;
        lock (_networkProbeSyncVE) { probeTask = _networkProbeTaskVE; StopNetworkProbeVE(); _networkProbeTaskVE = null; }
        lock(_captureSyncVE) { _capturePendingVE?.Completion.TrySetResult(null); _capturePendingVE=null; }
        Volatile.Write(ref _captureTelemetryVE,null);
        CancellationTokenSource? cts = _readCtsVE;
        Task? task = _readTaskVE;
        _readCtsVE = null;
        _readTaskVE = null;
        _streamVE = null;

        if (cts is not null)
        {
            cts.Cancel();
            try
            {
                if (task is not null) await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch { }
            cts.Dispose();
        }

        if (probeTask is not null) { try { await probeTask.WaitAsync(cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { } }

        PublishVE(VEControlStates.Stopped, "Control VisionEngine detenido.", null);
    }

    private async Task RunNativeReaderVE(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NLControlInputResponse response = await NLControlProtocol
                    .ReadAsync<NLControlInputResponse>(stream, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Increment(ref _receivedVE);
                if (response.Type == NLControlInputResponse.ClipboardType)
                {
                    string text = response.Text ?? string.Empty;
                    Interlocked.Increment(ref _clipboardVE);
                    Interlocked.Add(ref _receivedBytesVE, System.Text.Encoding.UTF8.GetByteCount(text) + 4);
                    if (_canReceiveClipboardVE is null || _canReceiveClipboardVE())
                        ClipboardChangedVE?.Invoke(this, text);
                }
                else if (response.Type == NLControlInputResponse.ClipboardAckType)
                {
                    ClipboardAcknowledgedVE?.Invoke(this, response.Sequence);
                }
                else if (response.Type == NLControlInputResponse.NetworkEchoType)
                {
                    lock (_networkProbeSyncVE) _networkProbeVE?.ObserveEcho(response.Sequence, response.DeviceClockUs);
                }
                else if(response.Type==NLControlInputResponse.CapturePolicyAckType && response.CapturePolicyAck is { } ack)
                {
                    lock(_captureSyncVE)
                        if(_capturePendingVE is { } current && current.Generation==ack.Generation && current.Sequence==ack.Sequence && current.Bitrate==ack.RequestedBitrate)
                            current.Completion.TrySetResult(ack);
                }
                else if(response.Type==NLControlInputResponse.CaptureTelemetryType && response.CaptureTelemetry is { } telemetry)
                {
                    var old=Volatile.Read(ref _captureTelemetryVE);
                    if(old is null || old.Telemetry.Generation!=telemetry.Generation || telemetry.Sequence>old.Telemetry.Sequence)
                        Volatile.Write(ref _captureTelemetryVE,new(telemetry,TimeProvider.System.GetTimestamp()));
                }
                PublishSnapshotVE();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (EndOfStreamException)
        {
            PublishVE(VEControlStates.EndOfStream, "Android cerró el canal de control VisionEngine.", null);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorsVE);
            PublishVE(VEControlStates.Failed, "Falló la respuesta del canal AppControl.", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(stream, _streamVE)) StopNetworkProbeVE();
        }
    }

    private static NLControlInputCommand ToNativeCommandVE(VEControlMessage message) => new(
        Type: (int)message.Type,
        KeyAction: (int)message.KeyAction,
        Keycode: message.Keycode,
        Repeat: message.Repeat,
        MetaState: message.MetaState,
        Text: message.Text,
        MotionAction: (int)message.MotionAction,
        PointerId: message.PointerId,
        X: message.Position.X,
        Y: message.Position.Y,
        ScreenWidth: message.Position.ScreenWidth,
        ScreenHeight: message.Position.ScreenHeight,
        Pressure: message.Pressure,
        ActionButton: message.ActionButton,
        Buttons: message.Buttons,
        HorizontalScroll: message.HorizontalScroll,
        VerticalScroll: message.VerticalScroll,
        CopyKey: (int)message.CopyKey,
        Sequence: message.Sequence,
        Paste: message.Paste,
        BooleanValue: message.BooleanValue,
        UhidId: message.UhidId,
        VendorId: message.VendorId,
        ProductId: message.ProductId,
        Name: message.Name,
        Data: message.Data,
        Width: message.Width,
        Height: message.Height,
        CapturePolicy: message.CapturePolicy);

    private VEControlStats SnapshotVE()
        => new(
            Interlocked.Read(ref _sentVE),
            Interlocked.Read(ref _sentBytesVE),
            Interlocked.Read(ref _receivedVE),
            Interlocked.Read(ref _receivedBytesVE),
            Interlocked.Read(ref _clipboardVE),
            0,
            Interlocked.Read(ref _errorsVE));

    private void PublishSnapshotVE()
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastStatusPublishMsVE);
        if (previous != 0 && now - previous < 250) return;
        Interlocked.Exchange(ref _lastStatusPublishMsVE, now);
        VEControlStatus current = StatusVE;
        PublishVE(current.State, current.Message, current.LastError);
    }

    private void PublishVE(VEControlStates state, string message, string? error)
    {
        VEControlStatus status = new(state, SnapshotVE(), DateTimeOffset.UtcNow, message, error);
        EventHandler<VEControlStatus>? handler;
        lock (_statusGateVE)
        {
            _statusVE = status;
            handler = StatusChangedVE;
        }
        handler?.Invoke(this, status);
    }

    private void ThrowIfDisposedVE() => ObjectDisposedException.ThrowIf(_disposedVE, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE) return;
        await StopAsync().ConfigureAwait(false);
        _disposedVE = true;
        _sendGateVE.Dispose();
    }
}
public sealed record VECaptureTelemetrySnapshot(NLControlCaptureTelemetry Telemetry,long ReceivedAtTicks);
