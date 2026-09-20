namespace NOVORA.VisionEngine.Control;

public sealed class VEControlCommands
{
    private readonly VEControlManager _control;
    public VEControlCommands(VEControlManager control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task BackAsync(VEControlActionKey action = VEControlActionKey.Down, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.BackOrScreenOnVE(action), cancellationToken);

    public Task RotateAsync(CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.SimpleVE(VEControlType.RotateDevice), cancellationToken);

    public Task SetDisplayPowerAsync(bool on, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.SetDisplayPowerVE(on), cancellationToken);

    public Task StartAppAsync(string packageOrAppName, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.StartAppVE(packageOrAppName), cancellationToken);

    public Task ExpandNotificationsAsync(CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.SimpleVE(VEControlType.ExpandNotificationPanel), cancellationToken);

    public Task CollapsePanelsAsync(CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.SimpleVE(VEControlType.CollapsePanels), cancellationToken);
}
