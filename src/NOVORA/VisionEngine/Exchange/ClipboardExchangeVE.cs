using NOVORA.VisionEngine.Control;
using System.Collections.Concurrent;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Clipboard de texto PC <-> Android a través del control channel.
/// </summary>
public sealed class ClipboardExchangeVE : IDisposable
{
    private readonly ManagerControlVE _controlVE;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<bool>> _acksVE = new();
    private TaskCompletionSource<string>? _nextClipboardVE;
    private long _sequenceVE;
    private bool _disposedVE;

    public ClipboardExchangeVE(ManagerControlVE control)
    {
        _controlVE = control ?? throw new ArgumentNullException(nameof(control));
        _controlVE.ClipboardChangedVE += Control_ClipboardChangedVE;
        _controlVE.ClipboardAcknowledgedVE += Control_ClipboardAcknowledgedVE;
    }

    public event EventHandler<string>? ClipboardChangedVE;

    public async Task<string> GetTextAsync(
        CopyControlVE copyKey = CopyControlVE.None,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        TaskCompletionSource<string> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _nextClipboardVE, tcs);
        await _controlVE.SendAsync(MessageControlVE.GetClipboardVE(copyKey), cancellationToken).ConfigureAwait(false);
        TimeSpan effective = timeout ?? TimeSpan.FromSeconds(3);
        try { return await tcs.Task.WaitAsync(effective, cancellationToken).ConfigureAwait(false); }
        finally { Interlocked.CompareExchange(ref _nextClipboardVE, null, tcs); }
    }

    public async Task<ulong> SetTextAsync(
        string text,
        bool paste = false,
        bool waitForAck = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(text);
        ulong sequence = unchecked((ulong)Interlocked.Increment(ref _sequenceVE));
        TaskCompletionSource<bool>? tcs = null;
        if (waitForAck)
        {
            tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_acksVE.TryAdd(sequence, tcs)) throw new InvalidOperationException("Secuencia de clipboard VisionEngine duplicada.");
        }
        try
        {
            await _controlVE.SendAsync(MessageControlVE.SetClipboardVE(sequence, text, paste), cancellationToken).ConfigureAwait(false);
            if (tcs is not null)
                await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            return sequence;
        }
        finally { if (tcs is not null) _acksVE.TryRemove(sequence, out _); }
    }

    private void Control_ClipboardChangedVE(object? sender, string text)
    {
        Interlocked.Exchange(ref _nextClipboardVE, null)?.TrySetResult(text);
        ClipboardChangedVE?.Invoke(this, text);
    }

    private void Control_ClipboardAcknowledgedVE(object? sender, ulong sequence)
    {
        if (_acksVE.TryGetValue(sequence, out TaskCompletionSource<bool>? tcs)) tcs.TrySetResult(true);
    }

    private void ThrowIfDisposedVE() => ObjectDisposedException.ThrowIf(_disposedVE, this);

    public void Dispose()
    {
        if (_disposedVE) return;
        _disposedVE = true;
        _controlVE.ClipboardChangedVE -= Control_ClipboardChangedVE;
        _controlVE.ClipboardAcknowledgedVE -= Control_ClipboardAcknowledgedVE;
        _nextClipboardVE?.TrySetCanceled();
        foreach (TaskCompletionSource<bool> tcs in _acksVE.Values) tcs.TrySetCanceled();
        _acksVE.Clear();
    }
}
