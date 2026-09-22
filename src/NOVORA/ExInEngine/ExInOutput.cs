namespace NOVORA.ExInEngine;

public interface IExInOutput
{
    bool IsReady { get; }
    Task CreateAsync(ExInDevice device, byte[] descriptor, CancellationToken cancellationToken);
    Task SendAsync(ushort deviceId, byte[] report, CancellationToken cancellationToken);
    Task DestroyAsync(ushort deviceId, CancellationToken cancellationToken);
}

public interface IExInUiOutput
{
    Task SendUiActionAsync(ExInUiAction action, CancellationToken cancellationToken);
}

internal sealed class ExInOutputRouter : IExInOutput, IExInUiOutput
{
    private readonly object _gate = new();
    private IExInOutput? _target;

    public bool IsReady { get { lock (_gate) return _target?.IsReady == true; } }
    public void Bind(IExInOutput target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            if (_target is not null && !ReferenceEquals(_target, target))
                throw new InvalidOperationException("Ya existe una salida ExIn enlazada.");
            _target = target;
        }
    }
    public void Unbind(IExInOutput target) { lock (_gate) { if (ReferenceEquals(_target, target)) _target = null; } }
    public bool IsBoundTo(IExInOutput target) { lock (_gate) return ReferenceEquals(_target, target); }
    private IExInOutput? Target() { lock (_gate) return _target; }
    public Task CreateAsync(ExInDevice device, byte[] descriptor, CancellationToken token) =>
        RequireTargetVE().CreateAsync(device, descriptor, token);
    public Task SendAsync(ushort deviceId, byte[] report, CancellationToken token) =>
        RequireTargetVE().SendAsync(deviceId, report, token);
    public Task DestroyAsync(ushort deviceId, CancellationToken token) =>
        RequireTargetVE().DestroyAsync(deviceId, token);
    public Task SendUiActionAsync(ExInUiAction action, CancellationToken token) =>
        Target() is IExInUiOutput uiTarget and IExInOutput { IsReady: true }
            ? uiTarget.SendUiActionAsync(action, token)
            : Task.CompletedTask;

    private IExInOutput RequireTargetVE()
        => Target() is { IsReady: true } target
            ? target
            : throw new InvalidOperationException("La salida ExIn no está disponible.");
}
