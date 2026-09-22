namespace NOVORA.ExInEngine;

public sealed class ExInOutputGeneration : IAsyncDisposable
{
    private readonly IExInOutput _outputVE;
    private readonly Func<ExInInputMode, ExInOutputProfile> _profileFactoryVE;
    private readonly SemaphoreSlim _transitionVE = new(1, 1);
    private ExInOutputProfile _currentVE;
    private long _generationVE = 1;
    private bool _createdVE;
    private bool _disposedVE;

    public ExInOutputGeneration(
        IExInOutput output,
        ExInOutputProfile initialProfile,
        Func<ExInInputMode, ExInOutputProfile> profileFactory)
    {
        _outputVE = output ?? throw new ArgumentNullException(nameof(output));
        _currentVE = initialProfile ?? throw new ArgumentNullException(nameof(initialProfile));
        _profileFactoryVE = profileFactory ?? throw new ArgumentNullException(nameof(profileFactory));
    }

    public ExInInputMode ModeVE => _currentVE.Mode;
    public long GenerationVE => Interlocked.Read(ref _generationVE);
    public ExInOutputProfile ProfileVE => _currentVE;

    public async Task CreateAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        await _transitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);
            if (_createdVE) return;
            if (!_outputVE.IsReady) return;
            try
            {
                await _outputVE.CreateAsync(_currentVE.Device, _currentVE.Descriptor, cancellationToken).ConfigureAwait(false);
                _createdVE = true;
                await _outputVE.SendAsync(_currentVE.Device.UhidId, _currentVE.NeutralReport, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (_createdVE)
                {
                    try { await _outputVE.DestroyAsync(_currentVE.Device.UhidId, CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                _createdVE = false;
                throw;
            }
        }
        finally { _transitionVE.Release(); }
    }

    public async Task<ExInModeResult> SetModeAsync(ExInInputMode mode, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        await _transitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);
            if (_currentVE.Mode == mode)
                return new(true, mode, GenerationVE, "El modo ya está activo.");

            ExInOutputProfile previous = _currentVE;
            ExInOutputProfile next = _profileFactoryVE(mode);
            long generation = Interlocked.Increment(ref _generationVE);
            if (!_outputVE.IsReady)
                return new(false, previous.Mode, generation, "La salida ExIn no está disponible.", "OutputNotReady");
            try
            {
                if (_createdVE)
                {
                    await _outputVE.SendAsync(previous.Device.UhidId, previous.NeutralReport, cancellationToken).ConfigureAwait(false);
                    await _outputVE.DestroyAsync(previous.Device.UhidId, cancellationToken).ConfigureAwait(false);
                }
                await _outputVE.CreateAsync(next.Device, next.Descriptor, cancellationToken).ConfigureAwait(false);
                await _outputVE.SendAsync(next.Device.UhidId, next.NeutralReport, cancellationToken).ConfigureAwait(false);
                _currentVE = next;
                _createdVE = true;
                return new(true, mode, generation, "Modo de control actualizado.");
            }
            catch (Exception ex)
            {
                try { await _outputVE.DestroyAsync(next.Device.UhidId, CancellationToken.None).ConfigureAwait(false); } catch { }
                bool restored = false;
                try
                {
                    await _outputVE.CreateAsync(previous.Device, previous.Descriptor, CancellationToken.None).ConfigureAwait(false);
                    await _outputVE.SendAsync(previous.Device.UhidId, previous.NeutralReport, CancellationToken.None).ConfigureAwait(false);
                    _createdVE = true;
                    restored = true;
                }
                catch { _createdVE = false; }
                _currentVE = previous;
                if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("Cambio de modo cancelado después de restaurar la salida anterior.", ex, cancellationToken);
                string message = restored
                    ? "No fue posible cambiar el modo; se restauró la salida anterior."
                    : "No fue posible cambiar el modo ni restaurar la salida anterior.";
                return new(false, previous.Mode, generation, message, ex.Message);
            }
        }
        finally { _transitionVE.Release(); }
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await _transitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);
            if (!_outputVE.IsReady) return;
            bool created = false;
            try
            {
                await _outputVE.CreateAsync(_currentVE.Device, _currentVE.Descriptor, cancellationToken).ConfigureAwait(false);
                created = true;
                await _outputVE.SendAsync(_currentVE.Device.UhidId, _currentVE.NeutralReport, cancellationToken).ConfigureAwait(false);
                _createdVE = true;
            }
            catch
            {
                if (created)
                    try { await _outputVE.DestroyAsync(_currentVE.Device.UhidId, CancellationToken.None).ConfigureAwait(false); } catch { }
                _createdVE = false;
                throw;
            }
        }
        finally { _transitionVE.Release(); }
    }

    public async Task DetachAsync(CancellationToken cancellationToken)
    {
        await _transitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);
            if (_createdVE && _outputVE.IsReady)
            {
                await _outputVE.SendAsync(_currentVE.Device.UhidId, _currentVE.NeutralReport, cancellationToken).ConfigureAwait(false);
                await _outputVE.DestroyAsync(_currentVE.Device.UhidId, cancellationToken).ConfigureAwait(false);
            }
            _createdVE = false;
        }
        finally { _transitionVE.Release(); }
    }

    public async Task<bool> SendAsync(long generation, byte[] report, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        await _transitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);
            if (generation != GenerationVE || !_createdVE || !_outputVE.IsReady) return false;
            await _outputVE.SendAsync(_currentVE.Device.UhidId, report, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _transitionVE.Release(); }
    }

    public async Task<bool> SendStateAsync(ExInState state, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        await _transitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);
            if (!_createdVE || !_outputVE.IsReady) return false;
            byte[] report = _currentVE.Mode == ExInInputMode.Game
                ? _currentVE.BuildReportVE(state)
                : _currentVE.NeutralReport;
            await _outputVE.SendAsync(_currentVE.Device.UhidId, report, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _transitionVE.Release(); }
    }

    public async Task<bool> SendPointerAsync(ExInPointerReport report, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        await _transitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);
            if (_currentVE.Mode != ExInInputMode.Ui || !_createdVE || !_outputVE.IsReady) return false;
            await _outputVE.SendAsync(
                _currentVE.Device.UhidId,
                ExInMouseReport.BuildVE(report),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _transitionVE.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _transitionVE.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposedVE) return;
            if (_createdVE)
            {
                try { await _outputVE.SendAsync(_currentVE.Device.UhidId, _currentVE.NeutralReport, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _outputVE.DestroyAsync(_currentVE.Device.UhidId, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            _createdVE = false;
            _disposedVE = true;
        }
        finally
        {
            _transitionVE.Release();
        }
    }
}
