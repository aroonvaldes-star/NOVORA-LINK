namespace NOVORA.VisionEngine.Control;

public sealed class VEControlKeyboard
{
    private readonly VEControlManager _control;
    public VEControlKeyboard(VEControlManager control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task KeyAsync(VEControlActionKey action, uint androidKeycode, uint repeat = 0, uint metaState = 0, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.KeycodeVE(action, androidKeycode, repeat, metaState), cancellationToken);

    public Task TextAsync(string text, CancellationToken cancellationToken = default)
        => _control.SendAsync(VEControlMessage.TextVE(text), cancellationToken);
}
