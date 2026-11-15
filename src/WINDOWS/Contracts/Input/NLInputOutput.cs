namespace NOVORA.Contracts.Input;

public sealed record NLInputDevice(
    ushort DeviceId,
    string Name,
    ushort VendorId,
    ushort ProductId);

public enum NLInputUiAction
{
    None,
    Up,
    Down,
    Left,
    Right,
    Select,
    Back
}

public interface INLInputOutput
{
    bool IsReady { get; }
    Task CreateAsync(NLInputDevice device, byte[] descriptor, CancellationToken cancellationToken);
    Task SendAsync(ushort deviceId, byte[] report, CancellationToken cancellationToken);
    Task DestroyAsync(ushort deviceId, CancellationToken cancellationToken);
}

public interface INLInputUiOutput
{
    Task SendUiActionAsync(NLInputUiAction action, CancellationToken cancellationToken);
}
