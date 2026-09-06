namespace NOVORA.VisionEngine.Control;

public sealed class MouseControlVE
{
    public const ulong MousePointerIdVE = ulong.MaxValue;
    private readonly ManagerControlVE _control;
    public MouseControlVE(ManagerControlVE control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task PointerAsync(ActionMotionControlVE action, int x, int y, ushort screenWidth, ushort screenHeight, uint actionButton, uint buttons, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.TouchVE(action, MousePointerIdVE, x, y, screenWidth, screenHeight, buttons == 0 ? 0f : 1f, actionButton, buttons), cancellationToken);

    public Task ScrollAsync(int x, int y, ushort screenWidth, ushort screenHeight, float horizontal, float vertical, uint buttons = 0, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.ScrollVE(x, y, screenWidth, screenHeight, horizontal, vertical, buttons), cancellationToken);
}
