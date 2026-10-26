using NOVORA;

try
{
    NLUIWindowMain.VerifyStartupControls();
    NLUIWindowMain.VerifyExInRouteSelection();
    ProjectionConsentTests.Run();
    Console.WriteLine("PASS: PC native LAN controls and one-use Android capture consent across service rebinding.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    return 1;
}

namespace NOVORA
{
    // The production button logic is source-linked above. These collaborators
    // supply only device/authorization state, without opening WPF or ADB.
    public partial class NLUIWindowMain
    {
        private readonly TestViewModel _viewModel = new();
        private readonly TestButton MainActionButton = new();
        private readonly TestButton RefreshDevicesButton = new();
        private readonly TestButton WifiAdbButton = new();
        private TestControl? _androidLanControl;
        private bool _closing, _visionCommandApplyingVE, _pcVideoAuthorizationRequested;
        private bool _running;

        private bool IsVisionEngineRunningVE() => _running;
        private void UpdateLinkEngineButtonLE() { }

        internal static void VerifyStartupControls()
        {
            var window = new NLUIWindowMain();
            window.UpdateRuntimeButtons();
            Require(!window.MainActionButton.IsEnabled, "Disconnected PC must not start capture.");
            window._androidLanControl = new() { IsAuthorized = false };
            window.UpdateRuntimeButtons();
            Require(!window.MainActionButton.IsEnabled, "An unauthenticated LAN listener is not a source.");
            window._androidLanControl.IsAuthorized = true;
            window.UpdateRuntimeButtons();
            Require(window.MainActionButton.IsEnabled, "Authorized native LAN must start without an ADB device.");
            window._pcVideoAuthorizationRequested = true;
            window.UpdateRuntimeButtons();
            Require(window.MainActionButton.Content == "CANCELAR", "Pending authorization must remain cancellable.");
            window._visionCommandApplyingVE = true;
            window.UpdateRuntimeButtons();
            Require(!window.MainActionButton.IsEnabled, "A busy engine must not accept a second start.");
            window._visionCommandApplyingVE = false;
            window._closing = true;
            window.UpdateRuntimeButtons();
            Require(!window.MainActionButton.IsEnabled, "A closing PC must not start capture.");
            window._closing = false;
            window._androidLanControl.IsAuthorized = false;
            window._viewModel.Device.Connected = true;
            window._viewModel.Device.IsWifiConnection = true;
            window.UpdateRuntimeButtons();
            Require(window.MainActionButton.IsEnabled, "The existing ADB Wi-Fi route must remain available.");
            window._running = true;
            window._viewModel.Device.Connected = false;
            window._pcVideoAuthorizationRequested = false;
            window.UpdateRuntimeButtons();
            Require(window.MainActionButton.IsEnabled, "An active stream must remain stoppable when ADB disappears.");
        }

        internal static void VerifyExInRouteSelection()
        {
            Require(ResolveExInSerialVE(" SM-TEST ", "USB-OTHER", "LAN:10.0.0.2", "OLD") == "SM-TEST",
                "Selected physical ADB device remains preferred.");
            Require(ResolveExInSerialVE(null, "USB-TEST", "LAN:10.0.0.2") == "USB-TEST",
                "Authorized USB remains usable.");
            Require(ResolveExInSerialVE(null, null, "10.0.0.2:5555") == "10.0.0.2:5555",
                "ADB over Wi-Fi is a valid route.");
            Require(ResolveExInSerialVE(null, null, "LAN:10.0.0.2") is null,
                "Synthetic VE LAN route never becomes an ADB device.");
            Require(ResolveExInSerialVE(" LAN:10.0.0.2 ", "USB-TEST", null) == "USB-TEST",
                "Synthetic route is excluded before candidate priority.");
            Require(ResolveExInSerialVE(null, null, " lan:10.0.0.2 ") is null,
                "Synthetic route matching ignores case.");
            Require(ResolveExInSerialVE(null, null, "LAN:10.0.0.2", "10.0.0.2:5555") == "10.0.0.2:5555",
                "Brief device enumeration loss preserves the running ExIn route.");
            Require(ResolveExInSerialVE(" ", null, null) is null,
                "No known ADB route remains empty.");
            Require(ResolveExInSerialVE(null, null, null, "LAN:10.0.0.2") is null,
                "The current route must also exclude synthetic LAN identities.");
            Console.WriteLine("PASS: 9 ExIn ADB route selection checks.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class TestViewModel { internal TestDevice Device { get; } = new(); }
        private sealed class TestDevice { internal bool Connected, IsWifiConnection; }
        private sealed class TestControl { internal bool IsAuthorized; }
        private sealed class TestButton { internal bool IsEnabled; internal string Content = ""; }
    }
}
