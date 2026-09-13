namespace NOVORA.VisionEngine.Integration;

public sealed class ManagerIntegrationVE
{
    private readonly object _gateVE = new();
    private StatusIntegrationVE _statusVE = new(
        CapabilitiesIntegrationVE.CreateDefaultVE(),
        DateTimeOffset.UtcNow);

    public event EventHandler<StatusIntegrationVE>? StatusChangedVE;

    public StatusIntegrationVE StatusVE
    {
        get { lock (_gateVE) return _statusVE; }
    }

    public void SetCapabilitiesVE(CapabilitiesIntegrationVE capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        StatusIntegrationVE status = new(capabilities, DateTimeOffset.UtcNow);

        lock (_gateVE)
        {
            if (_statusVE.Capabilities == capabilities)
                return;
            _statusVE = status;
        }

        StatusChangedVE?.Invoke(this, status);
    }
}
