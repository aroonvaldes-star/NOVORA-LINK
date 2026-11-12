namespace NOVORA.VisionEngine.Control;

public sealed class VEControlMouse
{
    public const ulong MousePointerIdVE = ulong.MaxValue;
    private readonly VEControlManager _control;
    public VEControlMouse(VEControlManager control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task PointerAsync(VEControlActionMotion action, int x, int y, ushort screenWidth, ushort screenHeight, uint actionButton, uint buttons, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.TouchVE(action, MousePointerIdVE, x, y, screenWidth, screenHeight, buttons == 0 ? 0f : 1f, actionButton, buttons), cancellationToken);

    public Task ScrollAsync(int x, int y, ushort screenWidth, ushort screenHeight, float horizontal, float vertical, uint buttons = 0, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.ScrollVE(x, y, screenWidth, screenHeight, horizontal, vertical, buttons), cancellationToken);
}
