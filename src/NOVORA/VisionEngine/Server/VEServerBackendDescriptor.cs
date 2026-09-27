namespace NOVORA.VisionEngine.Server;

public sealed record VEServerBackendDescriptor(
    VEServerBackend Backend,
    bool Available,
    bool Selected,
    bool Active,
    string RuntimeDependency,
    string ProtocolVersion);

public static class VEServerBackendCatalog
{
    public static VEServerBackendDescriptor Describe(
        VEServerBackend backend,
        bool available,
        bool selected,
        bool active)
        => backend switch
        {
            VEServerBackend.Scrcpy41Compatibility => new(
                backend,
                Available: available,
                Selected: selected,
                Active: active,
                RuntimeDependency: "scrcpy-server",
                ProtocolVersion: "4.1"),
            VEServerBackend.AppControlNative => new(
                backend,
                Available: available,
                Selected: selected,
                Active: active,
                RuntimeDependency: string.Empty,
                ProtocolVersion: "NOVORA-VE-1"),
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
}
