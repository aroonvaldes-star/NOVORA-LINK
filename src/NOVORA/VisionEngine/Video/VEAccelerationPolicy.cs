namespace NOVORA.VisionEngine.Video;

public static class VEAccelerationPolicy
{
    public static IReadOnlyList<VEAccelerationBackend> BuildCandidatesVE(
        VEAccelerationRequest request,
        VEAccelerationCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(request.Backend)) throw new ArgumentOutOfRangeException(nameof(request));

        var candidates = new List<VEAccelerationBackend>();
        void Add(VEAccelerationBackend backend)
        {
            if (capabilities.SupportsVE(backend) && !candidates.Contains(backend)) candidates.Add(backend);
        }

        if (request.Backend == VEAccelerationBackend.Automatic)
        {
            Add(VEAccelerationBackend.D3D11VA);
            Add(VEAccelerationBackend.NvidiaNvdec);
            Add(VEAccelerationBackend.Intel);
        }
        else if (request.Backend != VEAccelerationBackend.Software)
        {
            Add(request.Backend);
        }

        Add(VEAccelerationBackend.Software);
        return candidates;
    }
}
