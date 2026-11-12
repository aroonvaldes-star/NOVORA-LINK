// NOVORA_DYNAMIC_CONNECTION_V1
using Android.Content;
using Android.OS;
using NOVORA.Control;

namespace NOVORA.AndroidService;

/// <summary>
/// Dynamic connectivity: a PC that reached this phone through authorized ADB (cable or paired
/// wireless debugging) becomes a remembered PC, and its LAN address is prepared so the control
/// session can continue over Wi-Fi when the cable is removed. The credential is the same one that
/// «Recordar PC» stores; it is only requested on an ADB-bootstrapped session, never over plain LAN.
/// </summary>
public sealed partial class NLAndroidServiceControl
{
    /// <summary>
    /// Backoff for automatic recovery: 1, 2, 4, 8 s and then every 15 s while foreground, with up to
    /// 20% jitter so several phones do not retry in lockstep after the PC or the router restarts.
    /// </summary>
    internal static TimeSpan RecoveryDelay(int attempt, double jitter = double.NaN)
    {
        double seconds = attempt switch { <= 0 => 1, 1 => 2, 2 => 4, 3 => 8, _ => 15 };
        double factor = double.IsNaN(jitter) ? Random.Shared.NextDouble() : Math.Clamp(jitter, 0, 1);
        return TimeSpan.FromSeconds(seconds * (1 + 0.2 * factor));
    }

    private async Task PrepareLanFallbackAsync(string fingerprint, long operation)
    {
        try
        {
            NLControlSessionState state = Session.Current;
            if (_destroyed || operation != _operation || state.Phase != NLControlSessionPhase.Connected) return;

            NLControlTrustedPc? credential = GetSavedPcs().FirstOrDefault(peer =>
                string.Equals(peer.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            if (credential is null)
            {
                NLControlReply reply = await Session.SendAsync("trust.enroll", SafeName(Build.Model ?? "Android"));
                if (!reply.Success || reply.TrustedPc is not { } enrolled) return;
                if (operation != _operation || Session.Current.Generation != state.Generation) return;
                enrolled.Validate(true);
                if (!string.Equals(enrolled.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)) return;
                _trustedStore.Save(enrolled);
                credential = enrolled;
            }

            NLControlTrustedPc? lan = credential.Transport == "LAN" ? credential : null;
            if (lan is null)
            {
                // A LAN address is only a hint: discovery identifies the PC by its pinned certificate.
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                IReadOnlyList<NLControlLanPeer> discovered = await NLControlLanDiscovery.SearchAsync(deadline.Token);
                lan = (credential with { Transport = "LAN" }).AtDiscoveredLanAddress(discovered);
                if (lan is null) return; // Not on the same network now; retried on the next ADB session.
                if (operation != _operation) return;
                _trustedStore.Save(lan);
            }

            if (operation != _operation || Session.Current.Phase != NLControlSessionPhase.Connected) return;
            _recoveryPeer = lan;
            RememberAutomaticLanPc(lan.Fingerprint);
            Android.Util.Log.Info("NOVORA-CONTROL", "DYNAMIC_LAN_FALLBACK_READY");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The ADB session keeps working; only the Wi-Fi fallback is unavailable.
            Android.Util.Log.Warn("NOVORA-CONTROL", "DYNAMIC_LAN_FALLBACK_UNAVAILABLE " + ex.GetType().Name);
        }
    }

    /// <summary>Same preference the main screen uses to reconnect a saved LAN PC on launch.</summary>
    private void RememberAutomaticLanPc(string fingerprint)
    {
        ISharedPreferences? preferences = GetSharedPreferences("novora.lan", FileCreationMode.Private);
        if (preferences is null || preferences.GetString("automaticFingerprint", null) is not null) return;
        using ISharedPreferencesEditor editor = preferences.Edit()!;
        editor.PutString("automaticFingerprint", fingerprint);
        editor.Apply();
    }
}
