namespace NOVORA.VisionEngine.Control;

public sealed class TouchControlVE
{
    public const ulong FingerPointerIdVE = ulong.MaxValue - 1;
    private readonly ManagerControlVE _control;
    public TouchControlVE(ManagerControlVE control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task SendAsync(ActionMotionControlVE action, ulong pointerId, int x, int y, ushort screenWidth, ushort screenHeight, float pressure = 1f, uint buttons = 0, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.TouchVE(action, pointerId, x, y, screenWidth, screenHeight, pressure, 0, buttons), cancellationToken);
}
