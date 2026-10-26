using NOVORA.AndroidUI;

var cases = new[]
{
    (Usb: false, Lan: false, Expected: NLAndroidRouteAction.Wait),
    (Usb: true, Lan: false, Expected: NLAndroidRouteAction.ConnectUsb),
    (Usb: false, Lan: true, Expected: NLAndroidRouteAction.ConnectLan),
    (Usb: true, Lan: true, Expected: NLAndroidRouteAction.Ask)
};

foreach (var test in cases)
{
    var actual = NLAndroidUIRoutePolicy.Decide(test.Usb, test.Lan);
    if (actual != test.Expected)
        throw new InvalidOperationException($"USB={test.Usb}, LAN={test.Lan}: {actual} != {test.Expected}");
}

Console.WriteLine($"{cases.Length} decisiones de transporte verificadas.");
