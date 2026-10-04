namespace NOVORA.LinkClient;

internal enum NovoraVeTransportSelection { Usb, Lan }

internal static class NovoraVeTransportPolicy
{
    internal static NovoraVeTransportSelection Resolve(string? storedValue) =>
        string.Equals(storedValue, "LAN", StringComparison.OrdinalIgnoreCase)
            ? NovoraVeTransportSelection.Lan
            : NovoraVeTransportSelection.Usb;
}
