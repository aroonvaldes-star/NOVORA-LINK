using NOVORA.Control;

namespace NOVORA.AndroidVideo;

internal enum NLAndroidVideoProjectionResult { Waiting, Ready, Invalidated }

internal sealed record NLAndroidVideoProjectionGrant<T>(T Data, long Generation, bool Lan) where T : class;

/// <summary>Keeps a one-use capture grant until the activity rebinds its control service.</summary>
internal sealed class NLAndroidVideoProjectionRequest<T> where T : class
{
    private string? _serviceId;
    private long _generation;
    private bool _lan;
    private long? _pcAuthorizationRequestId;
    private T? _data;

    internal bool IsPending => _serviceId is not null;

    internal void Begin(string serviceId, long generation, bool lan, long? pcAuthorizationRequestId = null)
    {
        if (IsPending) throw new InvalidOperationException("Ya hay un permiso de captura pendiente.");
        ArgumentException.ThrowIfNullOrEmpty(serviceId);
        _serviceId = serviceId;
        _generation = generation;
        _lan = lan;
        _pcAuthorizationRequestId = pcAuthorizationRequestId;
    }

    internal void Approve(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (IsPending) _data = data;
    }

    internal NLAndroidVideoProjectionResult TryTake(
        string? serviceId, NLControlSessionState? state,
        out NLAndroidVideoProjectionGrant<T>? grant)
    {
        grant = null;
        if (!IsPending || _data is null) return NLAndroidVideoProjectionResult.Waiting;
        NLAndroidVideoProjectionResult readiness = ValidateSession(serviceId, state);
        if (readiness != NLAndroidVideoProjectionResult.Ready) return readiness;
        grant = new(_data, _generation, _lan);
        Cancel();
        return NLAndroidVideoProjectionResult.Ready;
    }

    internal NLAndroidVideoProjectionResult ValidateSession(string? serviceId, NLControlSessionState? state)
    {
        if (!IsPending || serviceId is null || state is null)
            return NLAndroidVideoProjectionResult.Waiting;
        bool validTransport = _lan ? state.UsesNativeVeLanVideo :
            state.Phase == NLControlSessionPhase.Connected &&
            (state.Transport == "USB" || state.Tunnel);
        if (_serviceId != serviceId || _generation != state.Generation || !validTransport ||
            state.Snapshot?.VideoRunning == true ||
            _pcAuthorizationRequestId is { } requestId &&
                (state.Snapshot?.Engines?.VideoAuthorizationRequested != true ||
                 state.Snapshot.Engines.VideoAuthorizationRequestId != requestId))
        {
            Cancel();
            return NLAndroidVideoProjectionResult.Invalidated;
        }
        if (state.Busy || state.Snapshot?.Engines?.VideoCanStart != true)
            return NLAndroidVideoProjectionResult.Waiting;

        return NLAndroidVideoProjectionResult.Ready;
    }

    internal void Cancel()
    {
        _serviceId = null;
        _data = null;
    }
}
