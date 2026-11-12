namespace NOVORA.VisionEngine.Integration;

public sealed class VEIntegrationManager
{
    private readonly object _gateVE = new();
    private VEIntegrationStatus _statusVE = new(
        VEIntegrationCapabilities.CreateDefaultVE(),
        DateTimeOffset.UtcNow);

    public event EventHandler<VEIntegrationStatus>? StatusChangedVE;

    public VEIntegrationStatus StatusVE
    {
        get { lock (_gateVE) return _statusVE; }
    }

    public void SetCapabilitiesVE(VEIntegrationCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        VEIntegrationStatus status = new(capabilities, DateTimeOffset.UtcNow);

        lock (_gateVE)
        {
            if (_statusVE.Capabilities == capabilities)
                return;
            _statusVE = status;
        }

        StatusChangedVE?.Invoke(this, status);
    }
}
