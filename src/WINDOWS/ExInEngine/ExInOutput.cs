using NOVORA.Contracts.Input;

namespace NOVORA.ExInEngine;

internal sealed class ExInOutputRouter : INLInputOutput, INLInputUiOutput
{
    private readonly object _gate = new();
    private INLInputOutput? _target;

    public bool IsReady { get { lock (_gate) return _target?.IsReady == true; } }
    public void Bind(INLInputOutput target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            if (_target is not null && !ReferenceEquals(_target, target))
                throw new InvalidOperationException("Ya existe una salida ExIn enlazada.");
            _target = target;
        }
    }
    public void Unbind(INLInputOutput target) { lock (_gate) { if (ReferenceEquals(_target, target)) _target = null; } }
    public bool IsBoundTo(INLInputOutput target) { lock (_gate) return ReferenceEquals(_target, target); }
    private INLInputOutput? Target() { lock (_gate) return _target; }
    public Task CreateAsync(NLInputDevice device, byte[] descriptor, CancellationToken token) =>
        RequireTargetVE().CreateAsync(device, descriptor, token);
    public Task SendAsync(ushort deviceId, byte[] report, CancellationToken token) =>
        RequireTargetVE().SendAsync(deviceId, report, token);
    public Task DestroyAsync(ushort deviceId, CancellationToken token) =>
        RequireTargetVE().DestroyAsync(deviceId, token);
    public Task SendUiActionAsync(NLInputUiAction action, CancellationToken token) =>
        Target() is INLInputUiOutput uiTarget and INLInputOutput { IsReady: true }
            ? uiTarget.SendUiActionAsync(action, token)
            : Task.CompletedTask;

    private INLInputOutput RequireTargetVE()
        => Target() is { IsReady: true } target
            ? target
            : throw new InvalidOperationException("La salida ExIn no está disponible.");
}
