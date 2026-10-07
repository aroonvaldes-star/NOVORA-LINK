using NOVORA.Control;
using NOVORA.Contracts.Input;
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
    private bool _nativeProtocolVE;
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

    public Task StartAsync(VETransportSession transport, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(transport);
        cancellationToken.ThrowIfCancellationRequested();

        if (_streamVE is not null)
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
        _nativeProtocolVE = true;
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
            int sentBytes;
            if (_nativeProtocolVE)
            {
                NLControlInputCommand command = ToNativeCommandVE(message);
                await NLControlProtocol.WriteAsync(stream, command, cancellationToken).ConfigureAwait(false);
                sentBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(command).Length + 4;
            }
            else
            {
                byte[] payload = VEControlSerializer.SerializeVE(message);
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                sentBytes = payload.Length;
            }
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
        CancellationTokenSource? cts = _readCtsVE;
        Task? task = _readTaskVE;
        _readCtsVE = null;
        _readTaskVE = null;
        _streamVE = null;
        _nativeProtocolVE = false;

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

    private async Task RunReaderVE(Stream stream, CancellationToken cancellationToken)
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
        Height: message.Height);

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
