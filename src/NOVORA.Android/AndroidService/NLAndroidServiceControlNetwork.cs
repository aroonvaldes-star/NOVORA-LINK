using NOVORA.Control;
using System.Text.Json;

namespace NOVORA.AndroidService;

public sealed partial class NLAndroidServiceControl
{
    private long _networkCapabilitiesGeneration = -1;

    internal static NLControlNetworkCapabilities CaptureNetworkCapabilities => new(
        StabilityTelemetry: true, LiveVideoBitrate: true, CaptureFramePacing: true,
        LiveGeometry: false, FilesWireVersions: [1, 2]);

    private void TryAnnounceNetworkCapabilities(NLControlSessionState state)
    {
        if (state.Phase != NLControlSessionPhase.Connected)
        {
            _networkCapabilitiesGeneration = -1;
            return;
        }
        // Old PCs receive no new action. The authorized snapshot advertises this negotiation.
        if (state.Busy || state.Transport is not ("USB" or "LAN") ||
            state.Snapshot?.Capabilities?.Version != 1 || _networkCapabilitiesGeneration == state.Generation)
            return;
        _networkCapabilitiesGeneration = state.Generation;
        _ = AnnounceNetworkCapabilitiesAsync(state.Generation);
    }

    private async Task AnnounceNetworkCapabilitiesAsync(long generation)
    {
        try
        {
            if (Session.Current.Generation != generation) return;
            NLControlReply reply = await Session.SendAsync("network.capabilities",
                JsonSerializer.Serialize(CaptureNetworkCapabilities));
            if (!reply.Success)
                Android.Util.Log.Warn("NOVORA-NETWORK", "PC no aceptó las capacidades de captura.");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("NOVORA-NETWORK", "No se confirmó la negociación: " + ex.Message);
        }
    }
}
