using Android.Content;
using NOVORA.AndroidStorage;
using NOVORA.Control;
using System.Text.Json;

namespace NOVORA.AndroidService;

/// <summary>Only the authenticated, negotiated FILES v2 capture endpoint attaches here. Its lifetime is separate from video.</summary>
internal static class NLAndroidServiceFilesV2
{
    internal sealed class Attachment(NLControlFileTransferV2 engine, long generation)
    {
        internal readonly NLControlFileTransferV2 Engine = engine;
        internal readonly long Generation = generation;
        internal Func<int, CancellationToken, ValueTask>? Pacer;
        internal readonly object BudgetGate = new();
        internal NLAndroidServiceControl.AndroidFilesBudget? Budget;
        internal CancellationTokenRegistration CaptureCancellation;
    }
    private static readonly object Gate = new();
    private static Attachment? _current;
    private static long _negotiatedGeneration;
    internal static Attachment? Current { get { lock (Gate) return _current; } }
    internal static long NegotiatedGeneration => Volatile.Read(ref _negotiatedGeneration);

    internal static void MarkV2Negotiated(long generation)
    {
        if (generation <= 0) throw new InvalidDataException("Generación FILES v2 inválida.");
        Interlocked.Exchange(ref _negotiatedGeneration, generation);
    }

    internal static void ClearV2Negotiated(long generation)
        => Interlocked.CompareExchange(ref _negotiatedGeneration, 0, generation);

    internal static async Task AttachAsync(Context context, Stream authenticatedFiles, long generation, CancellationToken captureCt)
    {
        if (generation <= 0) throw new InvalidDataException("Generación FILES v2 inválida.");
        captureCt.ThrowIfCancellationRequested();
        NLAndroidFileStorageV2.TryCreate(context, out var storage);
        Attachment? created = null;
        ValueTask Pacer(int bytes, CancellationToken ct) => Volatile.Read(ref created!.Pacer)?.Invoke(bytes, ct) ?? ValueTask.CompletedTask;
        var engine = new NLControlFileTransferV2(authenticatedFiles, storage, generation, pacer: Pacer);
        created = new(engine, generation);
        Attachment? previous;
        lock (Gate) { previous = _current; _current = created; }
        if (previous is not null)
        {
            previous.CaptureCancellation.Unregister();
            await previous.Engine.DisposeAsync().ConfigureAwait(false);
        }
        await engine.StartAsync().ConfigureAwait(false);
        created.CaptureCancellation = captureCt.Register(() => _ = DetachAsync(generation));
        _ = engine.Completion.ContinueWith(task =>
        {
            if (task.Exception?.GetBaseException() is not ObjectDisposedException)
                Android.Util.Log.Warn("NOVORA-FILES", "El canal FILES v2 se cerró; necesita una sesión nueva.");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    internal static async Task DetachAsync(long generation)
    {
        Attachment? current;
        lock (Gate)
        {
            if (_current?.Generation != generation) return;
            current = _current; _current = null;
        }
        current.CaptureCancellation.Unregister();
        await current.Engine.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed partial class NLAndroidServiceControl
{
    public event EventHandler<string>? FileTransferStateChanged;
    private static readonly JsonSerializerOptions FileJson = new() { PropertyNameCaseInsensitive = true };
    internal sealed record FileAllocation(long Revision, long Generation, string LinkKey, NLNetworkDirection Direction,
        long BytesPerSecond, int BurstBytes, int ValidForMilliseconds, NLNetworkPolicyMode Mode);

    /// <summary>Old peers retain CONTROL file.*. A negotiated v2 endpoint never falls back after a failure.</summary>
    public async Task<string> SendFileToPcAsync(Stream source, string name, long length,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var control = Session.Current;
        if (control.Phase != NLControlSessionPhase.Connected) throw new IOException("Conecta la PC antes de enviar.");
        var endpoint = NLAndroidServiceFilesV2.Current;
        if (endpoint is null)
        {
            if (NLAndroidServiceFilesV2.NegotiatedGeneration != 0)
                throw new IOException("FILES v2 fue negociado, pero su canal no está disponible. Inicia una sesión de captura nueva.");
            return await NLControlFileSender.SendAsync(source, name, length, (action, value) =>
            {
                EnsureSession(); return Session.SendAsync(action, value);
            }, progress, ct).ConfigureAwait(false);
        }
        if (NLAndroidServiceFilesV2.NegotiatedGeneration != endpoint.Generation)
            throw new IOException("El canal FILES v2 no pertenece a la sesión negociada.");
        if (endpoint.Engine.IsClosed) throw new IOException("El canal de archivos se cerró. Inicia una sesión de captura nueva.");
        Guid id = Guid.NewGuid(); bool granted = false;
        var request = new NLFileLeaseRequestV2(id, endpoint.Generation, NLFileTransferDirectionV2.AndroidToPc);
        using var budgetStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        AndroidFilesBudget budget;
        lock (endpoint.BudgetGate) budget = endpoint.Budget ??= new AndroidFilesBudget();
        budget.State = paused => FileTransferStateChanged?.Invoke(this,
            paused ? "Pausada por estabilidad" : "Enviando a la PC");
        Task? monitor = null;
        try
        {
            FileTransferStateChanged?.Invoke(this, "Esperando turno para enviar");
            while (!granted)
            {
                EnsureSession(); ct.ThrowIfCancellationRequested();
                var reply = await Session.SendAsync("files.acquire", JsonSerializer.Serialize(request)).WaitAsync(ct).ConfigureAwait(false);
                if (!reply.Success || string.IsNullOrWhiteSpace(reply.Value)) throw new IOException(reply.Message);
                var grant = JsonSerializer.Deserialize<NLFileLeaseGrantV2>(reply.Value, FileJson)
                    ?? throw new InvalidDataException("La PC no confirmó la asignación FILES.");
                if (grant.TransferId != id || grant.Generation != endpoint.Generation || grant.Direction != request.Direction)
                    throw new InvalidDataException("La PC devolvió una asignación FILES antigua.");
                granted = grant.Granted;
                if (!granted) await Task.Delay(250, ct).ConfigureAwait(false);
            }
            EnsureSession();
            await RefreshBudgetAsync().ConfigureAwait(false); // Manual allocation must be known before any bulk frame.
            endpoint.Pacer = budget.WaitAsync;
            monitor = MonitorBudgetAsync();
            var metadata = new NLFileMetadataV2(name, length, NLControlFileStorage.Category(name) + "/" + name);
            await endpoint.Engine.SendAsync(id, metadata, source, progress, ct).ConfigureAwait(false);
            return "Archivo verificado y publicado en la PC.";
        }
        catch
        {
            // Cancelling an abandoned queue/grant is control traffic; an active transfer was already handled by core Cancel.
            try
            {
                EnsureSession();
                await Session.SendAsync("files.cancelLease", JsonSerializer.Serialize(request)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch { /* Session/channel closure invalidates all leases of this generation. */ }
            throw;
        }
        finally
        {
            budgetStop.Cancel(); endpoint.Pacer = null;
            budget.State = null;
            if (monitor is not null) { try { await monitor.ConfigureAwait(false); } catch (System.OperationCanceledException) { } }
        }

        void EnsureSession()
        {
            var now = Session.Current;
            if (now.Phase != NLControlSessionPhase.Connected || now.Generation != control.Generation ||
                (endpoint is not null && !ReferenceEquals(endpoint, NLAndroidServiceFilesV2.Current)))
                throw new IOException("La PC, la ruta o la sesión cambió; vuelve a iniciar el envío.");
        }
        async Task RefreshBudgetAsync()
        {
            EnsureSession();
            var reply = await Session.SendAsync("network.policy", JsonSerializer.Serialize(new
                { Generation = endpoint.Generation, Direction = "FromAndroid" })).WaitAsync(budgetStop.Token).ConfigureAwait(false);
            if (!reply.Success || string.IsNullOrWhiteSpace(reply.Value)) throw new IOException("No se confirmó el presupuesto FILES.");
            var allocation = JsonSerializer.Deserialize<FileAllocation>(reply.Value, FileJson)
                ?? throw new InvalidDataException("Presupuesto FILES vacío.");
            if (allocation.Generation != endpoint.Generation || allocation.Direction != NLNetworkDirection.FromAndroid ||
                allocation.Revision < 0 || allocation.BytesPerSecond < 0 || allocation.BytesPerSecond > 1_250_000_000 ||
                allocation.BurstBytes is < 1 or > 1048576 || allocation.ValidForMilliseconds is < 1 or > 2000 ||
                string.IsNullOrWhiteSpace(allocation.LinkKey) || allocation.LinkKey.Length > 512 || !Enum.IsDefined(allocation.Mode))
                throw new InvalidDataException("Presupuesto FILES antiguo o fuera de límites.");
            budget.Apply(allocation);
        }
        async Task MonitorBudgetAsync()
        {
            while (!budgetStop.IsCancellationRequested)
            {
                await Task.Delay(1000, budgetStop.Token).ConfigureAwait(false);
                try { await RefreshBudgetAsync().ConfigureAwait(false); }
                catch (System.OperationCanceledException) when (budgetStop.IsCancellationRequested) { return; }
                catch { budget.Stale(); }
            }
        }
    }

    internal sealed class AndroidFilesBudget
    {
        private readonly object _gate = new();
        private readonly TimeProvider _clock = TimeProvider.System;
        private FileAllocation? _allocation;
        private long _receivedAt, _refilledAt;
        private double _tokens;
        private bool _paused;
        internal Action<bool>? State;
        public void Apply(FileAllocation allocation)
        {
            lock (_gate)
            {
                if (_allocation is { } old && allocation.Revision < old.Revision) return;
                long now = _clock.GetTimestamp();
                if (_allocation is null) { _tokens = allocation.BurstBytes; _refilledAt = now; }
                Refill(now); _allocation = allocation; _receivedAt = now;
                _tokens = Math.Min(_tokens, allocation.BurstBytes);
            }
        }
        public void Stale() { /* Keep the last manual ceiling; WaitAsync pauses when its local TTL expires. */ }
        public async ValueTask WaitAsync(int bytes, CancellationToken ct)
        {
            int remaining = bytes;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                TimeSpan delay;
                bool paused;
                lock (_gate)
                {
                    long now = _clock.GetTimestamp(); Refill(now);
                    var allocation = _allocation;
                    if (allocation is { Mode: NLNetworkPolicyMode.Disabled or NLNetworkPolicyMode.Observe }) return;
                    paused = allocation is null || allocation.BytesPerSecond == 0 ||
                        _clock.GetElapsedTime(_receivedAt, now).TotalMilliseconds >= allocation.ValidForMilliseconds;
                    if (paused) delay = TimeSpan.FromMilliseconds(100);
                    else
                    {
                        int consumed = Math.Min(remaining, (int)Math.Min(int.MaxValue, Math.Floor(_tokens)));
                        remaining -= consumed; _tokens -= consumed;
                        delay = TimeSpan.FromMilliseconds(Math.Clamp(1000d * Math.Min(remaining, allocation!.BurstBytes) / allocation.BytesPerSecond, 1, 100));
                    }
                    if (_paused != paused) { _paused = paused; State?.Invoke(paused); }
                }
                if (remaining > 0) await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        private void Refill(long now)
        {
            if (_allocation is { BytesPerSecond: > 0 } allocation)
                _tokens = Math.Min(allocation.BurstBytes, _tokens + _clock.GetElapsedTime(_refilledAt, now).TotalSeconds * allocation.BytesPerSecond);
            _refilledAt = now;
        }
    }
}
