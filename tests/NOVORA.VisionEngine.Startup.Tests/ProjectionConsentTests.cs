using NOVORA.AndroidVideo;
using NOVORA.Control;

internal static class ProjectionConsentTests
{
    internal static void Run()
    {
        foreach (bool lan in new[] { false, true })
        {
            NLControlSessionState connected = Connected(lan);
            var request = new NLAndroidVideoProjectionRequest<string>();
            request.Begin("service-a", 3, lan);
            Require(request.ValidateSession(null, null) == NLAndroidVideoProjectionResult.Waiting,
                "Audio permission must wait for the service to rebind before opening screen consent.");
            Require(request.ValidateSession("service-a", connected) == NLAndroidVideoProjectionResult.Ready && request.IsPending &&
                request.TryTake("service-a", connected, out _) == NLAndroidVideoProjectionResult.Waiting,
                "Audio consent alone must never start screen capture or consume the screen request.");
            request.Approve("capture-grant");
            Require(request.TryTake(null, null, out _) == NLAndroidVideoProjectionResult.Waiting && request.IsPending,
                "The first activity result must survive a detached control service.");
            Require(request.TryTake("service-a", connected with { Busy = true }, out _) == NLAndroidVideoProjectionResult.Waiting,
                "An outstanding command must not consume capture consent.");
            var updated = connected with { Snapshot = connected.Snapshot! with { Revision = 42 } };
            Require(request.TryTake("service-a", updated, out var grant) == NLAndroidVideoProjectionResult.Ready &&
                grant is { Data: "capture-grant", Generation: 3 } && grant.Lan == lan,
                "Rebinding must consume the original grant once, even after a PC status update.");
            Require(!request.IsPending && request.TryTake("service-a", updated, out _) == NLAndroidVideoProjectionResult.Waiting,
                "Repeated service/state callbacks must never start capture twice.");

            request.Begin("service-a", 3, lan);
            request.Approve("capture-grant");
            Require(request.TryTake("service-b", connected, out _) == NLAndroidVideoProjectionResult.Invalidated && !request.IsPending,
                "A restarted control service must not inherit an old projection grant.");
            request.Begin("service-a", 3, lan);
            request.Approve("capture-grant");
            Require(request.TryTake("service-a", connected with { Generation = 4 }, out _) == NLAndroidVideoProjectionResult.Invalidated,
                "Changing the authorized session must invalidate capture consent.");
            request.Begin("service-a", 3, lan);
            request.Approve("capture-grant");
            Require(request.TryTake("service-a", connected with { Phase = NLControlSessionPhase.Lost }, out _) == NLAndroidVideoProjectionResult.Invalidated,
                "A lost connection must not start capture.");
            request.Begin("service-a", 3, lan);
            request.Approve("capture-grant");
            Require(request.TryTake("service-a", connected with { Snapshot = connected.Snapshot! with { VideoRunning = true } }, out _) == NLAndroidVideoProjectionResult.Invalidated,
                "A stream started elsewhere must not receive a second projection.");
            request.Begin("service-a", 3, lan);
            request.Approve("capture-grant");
            request.Cancel();
            Require(request.TryTake("service-a", connected, out _) == NLAndroidVideoProjectionResult.Waiting,
                "Cancelled consent must never be reused.");
            request.Begin("service-a", 3, lan);
            Require(request.ValidateSession("service-b", connected) == NLAndroidVideoProjectionResult.Invalidated && !request.IsPending,
                "A service replaced during audio permission must invalidate the screen request.");
        }

        var tunnel = new NLAndroidVideoProjectionRequest<string>();
        tunnel.Begin("service-a", 3, lan: false);
        tunnel.Approve("capture-grant");
        Require(tunnel.TryTake("service-a", Connected(true) with { Tunnel = true }, out _) == NLAndroidVideoProjectionResult.Ready,
            "A protected ADB LAN tunnel must keep using the AppControl capture route.");
        tunnel.Begin("service-a", 3, lan: true);
        tunnel.Approve("capture-grant");
        Require(tunnel.TryTake("service-a", Connected(true) with { Tunnel = true }, out _) == NLAndroidVideoProjectionResult.Invalidated,
            "Native LAN consent must not be reused by an ADB tunnel.");

        var pcRequest = new NLAndroidVideoProjectionRequest<string>();
        NLControlSessionState requested = Connected(true) with
        {
            Snapshot = Connected(true).Snapshot! with
            {
                Engines = Connected(true).Snapshot!.Engines! with
                { VideoAuthorizationRequested = true, VideoAuthorizationRequestId = 17 }
            }
        };
        pcRequest.Begin("service-a", 3, lan: true, pcAuthorizationRequestId: 17);
        pcRequest.Approve("capture-grant");
        Require(pcRequest.TryTake("service-a", requested with
        {
            Snapshot = requested.Snapshot! with
            {
                Engines = requested.Snapshot!.Engines! with { VideoAuthorizationRequested = false }
            }
        }, out _) == NLAndroidVideoProjectionResult.Invalidated,
            "Cancelling the originating PC request must invalidate its open Android consent.");
        pcRequest.Begin("service-a", 3, lan: true, pcAuthorizationRequestId: 17);
        pcRequest.Approve("capture-grant");
        Require(pcRequest.TryTake("service-a", requested with
        {
            Snapshot = requested.Snapshot! with
            {
                Engines = requested.Snapshot!.Engines! with { VideoAuthorizationRequestId = 18 }
            }
        }, out _) == NLAndroidVideoProjectionResult.Invalidated,
            "A replacement PC request in the same session must not reuse an older open consent.");
        pcRequest.Begin("service-a", 3, lan: true, pcAuthorizationRequestId: 17);
        Require(pcRequest.ValidateSession("service-a", requested) == NLAndroidVideoProjectionResult.Ready && pcRequest.IsPending,
            "The active PC request must survive service rebinding during audio consent.");
        pcRequest.Approve("capture-grant");
        Require(pcRequest.TryTake("service-a", requested, out _) == NLAndroidVideoProjectionResult.Ready,
            "An active originating PC request must start after its Android consent.");
        pcRequest.Begin("service-a", 3, lan: true);
        pcRequest.Approve("capture-grant");
        Require(pcRequest.TryTake("service-a", Connected(true), out _) == NLAndroidVideoProjectionResult.Ready,
            "Manual Android startup must not require an originating PC request.");
    }

    private static NLControlSessionState Connected(bool lan) => new(
        3, NLControlSessionPhase.Connected, lan ? "LAN" : "USB", "", new(
            10, "PC", "1.4", "8000000", "Gaming", "disabled", "disabled", false, [], [], [],
            new(true, false, false, false, false, "Stopped", "", "Android")));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
