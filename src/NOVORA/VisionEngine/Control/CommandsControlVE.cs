namespace NOVORA.VisionEngine.Control;

public sealed class CommandsControlVE
{
    private readonly ManagerControlVE _control;
    public CommandsControlVE(ManagerControlVE control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task BackAsync(ActionKeyControlVE action = ActionKeyControlVE.Down, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.BackOrScreenOnVE(action), cancellationToken);

    public Task RotateAsync(CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.SimpleVE(TypeControlVE.RotateDevice), cancellationToken);

    public Task SetDisplayPowerAsync(bool on, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.SetDisplayPowerVE(on), cancellationToken);

    public Task StartAppAsync(string packageOrAppName, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.StartAppVE(packageOrAppName), cancellationToken);

    public Task ExpandNotificationsAsync(CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.SimpleVE(TypeControlVE.ExpandNotificationPanel), cancellationToken);

    public Task CollapsePanelsAsync(CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.SimpleVE(TypeControlVE.CollapsePanels), cancellationToken);
}
