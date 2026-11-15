using NOVORA.Contracts.Input;

namespace NOVORA.ExInEngine;

public sealed class ExInOutputGeneration : IAsyncDisposable
{
    private readonly INLInputOutput _outputVE;
    private readonly Func<ExInInputMode, ExInOutputProfile> _profileFactoryVE;

    private readonly SemaphoreSlim _transitionVE = new(1, 1);
    private ExInOutputProfile _currentVE;
    private long _generationVE = 1;

    private bool _createdVE;
    private bool _disposedVE;

    public ExInOutputGeneration(
        INLInputOutput output,
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
                await _outputVE.CreateAsync(ToContractVE(_currentVE.Device), _currentVE.Descriptor, cancellationToken).ConfigureAwait(false);
                _createdVE = true;
                _lastStateReportVE = null;
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

            bool nextDeviceCreated = false; // ⭐ NUEVO: Rastreo explícito
            
            try
            {
                // Destruir anterior
                if (_createdVE)
                {
                    _lastStateReportVE = null;
                    await _outputVE.SendAsync(previous.Device.UhidId, previous.NeutralReport, cancellationToken).ConfigureAwait(false);
                    await _outputVE.DestroyAsync(previous.Device.UhidId, cancellationToken).ConfigureAwait(false);
                    _createdVE = false; // ⭐ NUEVO: Marcar como no creado antes de crear nuevo
                }

                // Crear nuevo dispositivo
                await _outputVE.CreateAsync(ToContractVE(next.Device), next.Descriptor, cancellationToken).ConfigureAwait(false);
                nextDeviceCreated = true; // ⭐ NUEVO: Marcar creación exitosa

                _lastStateReportVE = null;
                await _outputVE.SendAsync(next.Device.UhidId, next.NeutralReport, cancellationToken).ConfigureAwait(false);

                _currentVE = next;
                _createdVE = true;

                return new(true, mode, generation, "Modo de control actualizado.");
            }
            catch (Exception ex)
            {
                // ⭐ FIJO: Limpieza exhaustiva y condicional
                if (nextDeviceCreated)
                {
                    try 
                    { 
                        _lastStateReportVE = null;
                        await _outputVE.SendAsync(next.Device.UhidId, next.NeutralReport, CancellationToken.None).ConfigureAwait(false); 
                    } 
                    catch { }
                    
                    try 
                    { 
                        await _outputVE.DestroyAsync(next.Device.UhidId, CancellationToken.None).ConfigureAwait(false); 
                    } 
                    catch { }
                }

                bool restored = false;
                try
                {
                    await _outputVE.CreateAsync(ToContractVE(previous.Device), previous.Descriptor, CancellationToken.None).ConfigureAwait(false);
                    _lastStateReportVE = null;
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
            if (_createdVE) return;

            bool created = false;
            try
            {
                await _outputVE.CreateAsync(ToContractVE(_currentVE.Device), _currentVE.Descriptor, cancellationToken).ConfigureAwait(false);
                created = true;
                _lastStateReportVE = null;
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
                _lastStateReportVE = null;
                await _outputVE.SendAsync(_currentVE.Device.UhidId, _currentVE.NeutralReport, cancellationToken).ConfigureAwait(false);
                await _outputVE.DestroyAsync(_currentVE.Device.UhidId, cancellationToken).ConfigureAwait(false);
            }
            _createdVE = false;
        }
        finally
        {
            // An unavailable/failed transport no longer owns a usable UHID device.
            _createdVE = false;
            _transitionVE.Release();
        }
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

    private static readonly TimeSpan StateHeartbeatVE = TimeSpan.FromMilliseconds(100);
    private object? _lastStateProfileVE;
    private byte[]? _lastStateReportVE;
    private long _lastStateSentAtVE;

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

            // A gamepad report is absolute state: repeating an identical one only adds pipe
            // traffic. Changes go out at once; an unchanged state is refreshed as a heartbeat.
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (ReferenceEquals(_lastStateProfileVE, _currentVE) && _lastStateReportVE is { } last &&
                last.AsSpan().SequenceEqual(report) &&
                System.Diagnostics.Stopwatch.GetElapsedTime(_lastStateSentAtVE, now) < StateHeartbeatVE)
                return false;

            _lastStateReportVE = null;
            await _outputVE.SendAsync(_currentVE.Device.UhidId, report, cancellationToken).ConfigureAwait(false);
            _lastStateProfileVE = _currentVE;
            _lastStateReportVE = report;
            _lastStateSentAtVE = now;
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
                _lastStateReportVE = null;
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

    private static NLInputDevice ToContractVE(ExInDevice device)
        => new(device.UhidId, device.Name, device.VendorId, device.ProductId);
}
