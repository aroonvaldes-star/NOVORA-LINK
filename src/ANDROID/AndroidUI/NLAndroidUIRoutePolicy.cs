namespace NOVORA.AndroidUI;

internal enum NLAndroidRouteAction
{
    Wait,
    ConnectUsb,
    ConnectLan,
    Ask
}

internal static class NLAndroidUIRoutePolicy
{
    internal static NLAndroidRouteAction Decide(bool usbPcReachable, bool lanPcDiscovered) =>
        (usbPcReachable, lanPcDiscovered) switch
        {
            (true, true) => NLAndroidRouteAction.Ask,
            (true, false) => NLAndroidRouteAction.ConnectUsb,
            (false, true) => NLAndroidRouteAction.ConnectLan,
            _ => NLAndroidRouteAction.Wait
        };
}
