// NOVORA_AUTOUSB_V1
using System.Security.Cryptography;
using System.Text;
using NOVORA.Control;
using NOVORA.AndroidVpn;

namespace NOVORA.AndroidService;

public sealed partial class NLAndroidServiceControl
{
    private readonly SemaphoreSlim _automaticUsbGate = new(1, 1);
    private long _automaticUsbRequest, _automaticUsbSequence;
    private string? _automaticUsbIdentity, _automaticUsbLastDiagnostic;
    private string _automaticUsbOrigin = "Manual";

    public async Task ConnectAutomaticUsbAsync(string text)
    {
        // Delivered only through the protected bootstrap Activity and in-process handoff.
        _ = NLAndroidServiceUsbBootstrap.Parse(text);
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        long request = ++_automaticUsbRequest;
        long operation = _operation;
        await _automaticUsbGate.WaitAsync();
        try
        {
            if (_destroyed || request != _automaticUsbRequest || operation != _operation) return;
            var state = Session.Current;
            if (state.Transport == "USB" && state.Phase == NLControlSessionPhase.Connected &&
                _automaticUsbIdentity == identity) return; // Same invitation, no duplicate socket.
            NLControlTrustedPc? lanFallback = state.Transport == "LAN"
                ? _recoveryPeer ?? _connectingPeer
                : null;
            if (state.Transport == "LAN" && state.Phase is NLControlSessionPhase.Connected or NLControlSessionPhase.Connecting)
            {
                await DisconnectAsync();
                if (_destroyed || request != _automaticUsbRequest) return;
            }
            try
            {
                AcceptUsbBootstrap(text);
                _automaticUsbIdentity = identity;
                await ConnectUsbCoreAsync(automatic: true);
            }
            catch (Exception ex) when (lanFallback is not null && !_destroyed && request == _automaticUsbRequest)
            {
                await ConnectTrustedAsync(lanFallback);
                RecoveryMessage = "USB fue detectado, pero no se confirmó. La sesión LAN continúa activa.";
                Android.Util.Log.Warn("NOVORA-USB", "AUTO_USB_FALLBACK_LAN " + ex.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("NOVORA-USB", "AUTO_SERVICE_FAILED " + ex.GetType().Name + " " + Session.Current.Phase + " " + Session.Current.Message);
            throw;
        }
        finally
        {
            PublishAutomaticUsbDiagnostics();
            _automaticUsbGate.Release();
        }
    }

    // Minimal event-only technical evidence. Never logs a name, serial, secret, QR,
    // certificate, clipboard or file contents. Ordinary app operation writes no report file.
    private void PublishAutomaticUsbDiagnostics()
    {
        if (_destroyed) return;
        var state = Session.Current;
        var snapshot = state.Snapshot;
        bool confirmed = state.Phase == NLControlSessionPhase.Connected && snapshot is not null;
        var values = new
        {
            Build = "NOVORA_AUTOUSB_V1", Service = InstanceId,
            Pid = Android.OS.Process.MyPid(), Generation = state.Generation,
            Origin = _automaticUsbOrigin, Phase = state.Phase.ToString(), Transport = state.Transport,
            Confirmed = confirmed, Busy = state.Busy,
            LinkCanStart = confirmed && snapshot?.Engines?.LinkCanStart == true,
            LinkCanStop = confirmed && snapshot?.Engines?.LinkCanStop == true,
            LinkRunning = confirmed && snapshot?.Engines?.LinkRunning == true,
            LinkState = snapshot?.Engines?.LinkState ?? "",
            VpnRunning = NLAndroidVpnService.IsRunning,
            VideoCanStart = confirmed && snapshot?.Engines?.VideoCanStart == true,
            VideoCanStop = confirmed && snapshot?.Engines?.VideoCanStop == true,
            VideoRunning = confirmed && snapshot?.VideoRunning == true,
            FileSharing = confirmed && snapshot?.FileSharing == true
        };
        string content = System.Text.Json.JsonSerializer.Serialize(values);
        if (_automaticUsbLastDiagnostic == content) return;
        _automaticUsbLastDiagnostic = content;
        string line = System.Text.Json.JsonSerializer.Serialize(new
        {
            Schema = "NOVORA_USB_EVIDENCE_V1", Utc = DateTimeOffset.UtcNow,
            Sequence = ++_automaticUsbSequence, State = values
        });
        Android.Util.Log.Info("NOVORA-USB", line);
    }
}
