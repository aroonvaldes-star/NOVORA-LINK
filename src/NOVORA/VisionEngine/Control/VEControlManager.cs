using NOVORA.ExInEngine;
using NOVORA.VisionEngine.Transport;
using System.Net.Sockets;

namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Canal bidireccional de input, clipboard y UHID de VisionEngine.
/// </summary>
public sealed class VEControlManager : IAsyncDisposable, IExInOutput, IExInUiOutput
{
    private readonly SemaphoreSlim _sendGateVE = new(1, 1);
    private readonly object _statusGateVE = new();
    private NetworkStream? _streamVE;
    private CancellationTokenSource? _readCtsVE;
    private Task? _readTaskVE;
    private VEControlStatus _statusVE = VEControlStatus.CreateInitialVE();
    private long _sentVE;
    private long _sentBytesVE;
    private long _receivedVE;
    private long _receivedBytesVE;
    private long _clipboardVE;
    private long _uhidVE;
    private long _errorsVE;
    private long _lastStatusPublishMsVE;
    private Func<VEControlMessage, bool>? _canSendMessageVE;
    private Func<bool>? _canReceiveClipboardVE;
    private bool _disposedVE;

    public event EventHandler<VEControlStatus>? StatusChangedVE;
    public event EventHandler<string>? ClipboardChangedVE;
    public event EventHandler<ulong>? ClipboardAcknowledgedVE;
    public event EventHandler<VEControlMessageDevice>? UhidOutputVE;

    public VEControlStatus StatusVE
    {
        get { lock (_statusGateVE) return _statusVE; }
    }

    public bool IsReadyVE => StatusVE.State == VEControlStates.Ready;
    bool IExInOutput.IsReady => IsReadyVE;

    Task IExInOutput.CreateAsync(ExInDevice device, byte[] descriptor, CancellationToken cancellationToken) =>
        SendAsync(VEControlMessage.UhidCreateVE(device.UhidId, device.VendorId, device.ProductId, device.Name, descriptor), cancellationToken);
    Task IExInOutput.SendAsync(ushort deviceId, byte[] report, CancellationToken cancellationToken) =>
        SendAsync(VEControlMessage.UhidInputVE(deviceId, report), cancellationToken);
    Task IExInOutput.DestroyAsync(ushort deviceId, CancellationToken cancellationToken) =>
        SendAsync(VEControlMessage.UhidDestroyVE(deviceId), cancellationToken);
    async Task IExInUiOutput.SendUiActionAsync(ExInUiAction action, CancellationToken cancellationToken)
    {
        uint? keycode = action switch
        {
            ExInUiAction.Up => 19,
            ExInUiAction.Down => 20,
            ExInUiAction.Left => 21,
            ExInUiAction.Right => 22,
            ExInUiAction.Select => 23,
            _ => null
        };

        if (action == ExInUiAction.Back)
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

    public Task StartAsync(VETransportSession transport, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(transport);
        cancellationToken.ThrowIfCancellationRequested();

        if (_readTaskVE is not null)
        {
            throw new InvalidOperationException("Control VisionEngine ya está iniciado.");
        }

        _streamVE = transport.ControlStream
            ?? throw new InvalidOperationException("La sesión VisionEngine no contiene control socket.");

        _readCtsVE = new CancellationTokenSource();
        PublishVE(VEControlStates.Starting, "Iniciando canal de control VisionEngine.", null);
        _readTaskVE = RunReaderVE(_streamVE, _readCtsVE.Token);
        PublishVE(VEControlStates.Ready, "Control VisionEngine listo.", null);
        return Task.CompletedTask;
    }

    public async Task SendAsync(VEControlMessage message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(message);

        if (_canSendMessageVE is not null && !_canSendMessageVE(message))
            return;

        NetworkStream stream = _streamVE
            ?? throw new InvalidOperationException("Control VisionEngine no está iniciado.");

        byte[] payload = VEControlSerializer.SerializeVE(message);
        await _sendGateVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _sentVE);
            Interlocked.Add(ref _sentBytesVE, payload.Length);
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

        PublishVE(VEControlStates.Stopped, "Control VisionEngine detenido.", null);
    }

    private async Task RunReaderVE(NetworkStream stream, CancellationToken cancellationToken)
    {
        VEControlReader reader = new(stream);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                VEControlMessageDevice message = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _receivedVE);

                switch (message.Type)
                {
                    case VEControlTypeDevice.Clipboard:
                        Interlocked.Increment(ref _clipboardVE);
                        if (message.ClipboardText is string text)
                        {
                            Interlocked.Add(ref _receivedBytesVE, System.Text.Encoding.UTF8.GetByteCount(text) + 5);
                            if (_canReceiveClipboardVE is null || _canReceiveClipboardVE())
                                ClipboardChangedVE?.Invoke(this, text);
                        }
                        break;
                    case VEControlTypeDevice.ClipboardAck:
                        Interlocked.Add(ref _receivedBytesVE, 9);
                        if (message.Sequence is ulong sequence)
                        {
                            ClipboardAcknowledgedVE?.Invoke(this, sequence);
                        }
                        break;
                    case VEControlTypeDevice.UhidOutput:
                        Interlocked.Increment(ref _uhidVE);
                        Interlocked.Add(ref _receivedBytesVE, 5 + (message.Data?.Length ?? 0));
                        UhidOutputVE?.Invoke(this, message);
                        break;
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
            PublishVE(VEControlStates.Failed, "Falló el canal de control VisionEngine.", ex.Message);
        }
    }

    private VEControlStats SnapshotVE()
        => new(
            Interlocked.Read(ref _sentVE),
            Interlocked.Read(ref _sentBytesVE),
            Interlocked.Read(ref _receivedVE),
            Interlocked.Read(ref _receivedBytesVE),
            Interlocked.Read(ref _clipboardVE),
            Interlocked.Read(ref _uhidVE),
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
