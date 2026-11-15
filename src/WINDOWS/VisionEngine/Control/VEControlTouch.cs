namespace NOVORA.VisionEngine.Control;

public sealed class VEControlTouch
{
    public const ulong FingerPointerIdVE = ulong.MaxValue - 1;
    private readonly VEControlManager _control;
    public VEControlTouch(VEControlManager control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task SendAsync(VEControlActionMotion action, ulong pointerId, int x, int y, ushort screenWidth, ushort screenHeight, float pressure = 1f, uint buttons = 0, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.TouchVE(action, pointerId, x, y, screenWidth, screenHeight, pressure, 0, buttons), cancellationToken);
}
