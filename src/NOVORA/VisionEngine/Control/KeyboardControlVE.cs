namespace NOVORA.VisionEngine.Control;

public sealed class KeyboardControlVE
{
    private readonly ManagerControlVE _control;
    public KeyboardControlVE(ManagerControlVE control) => _control = control ?? throw new ArgumentNullException(nameof(control));

    public Task KeyAsync(ActionKeyControlVE action, uint androidKeycode, uint repeat = 0, uint metaState = 0, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.KeycodeVE(action, androidKeycode, repeat, metaState), cancellationToken);

    public Task TextAsync(string text, CancellationToken cancellationToken = default)
        => _control.SendAsync(MessageControlVE.TextVE(text), cancellationToken);
}
