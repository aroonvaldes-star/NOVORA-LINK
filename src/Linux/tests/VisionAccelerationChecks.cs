using NOVORA.Control;
using NOVORA.Linux.VisionEngine;

internal static class VisionAccelerationChecks
{
    public static async Task RunAsync()
    {
        Require(LinuxVisionAcceleration.Select(LinuxVisionSettings.Default with { Acceleration = "Software" }) == "software",
            "Software mode not respected");

        var invalid = await LinuxVisionAcceleration.ProbeBackendAsync("invalid", CancellationToken.None);
        Require(!invalid.Available, "Invalid accelerator accepted");

        // "Forced" was removed from the UI and from the mode contract.
        Require(Enum.GetNames<NLAccelerationMode>().Length == 3 &&
                !Enum.GetNames<NLAccelerationMode>().Contains("Forced"),
            "Removed Forced mode is still part of the contract");

        string[] platforms = ["linux", "windows", "mac"];
        string[] profiles = ["Automatic", "Balanced", "High", "Software", "MobileExtreme", "MobileEconomy"];
        NLAccelerationVendor[] vendors =
        [
            NLAccelerationVendor.Unknown, NLAccelerationVendor.Nvidia,
            NLAccelerationVendor.Intel, NLAccelerationVendor.Amd, NLAccelerationVendor.Apple
        ];

        foreach (string platform in platforms)
        foreach (NLAccelerationVendor vendor in vendors)
        foreach (string profile in profiles)
        {
            string vendorName = NLAccelerationProfile.VendorName(vendor);
            var options = NLAccelerationProfile.OptionsFor(platform, vendorName, profile);
            Require(options.Count is >= 1 and <= 2,
                $"{platform}/{vendorName}/{profile}: expected one or two options, got {options.Count}");
            Require(options[^1].Mode == NLAccelerationMode.Software,
                $"{platform}/{vendorName}/{profile}: software must always be the last fallback");
            Require(!options.Any(o => o.Label.Contains("Forced", StringComparison.OrdinalIgnoreCase) ||
                                      o.Label.Contains("Forzad", StringComparison.OrdinalIgnoreCase)),
                $"{platform}/{vendorName}/{profile}: removed Forced label is still exposed");

            bool saving = profile is "Software" or "MobileExtreme" or "MobileEconomy";
            bool softwareOnly = saving || NLAccelerationProfile.HardwareBackend(vendor, platform).Length == 0;
            Require(options.Count == (softwareOnly ? 1 : 2),
                $"{platform}/{vendorName}/{profile}: unexpected hardware option");

            foreach (NLAccelerationMode mode in Enum.GetValues<NLAccelerationMode>())
            {
                var selected = NLAccelerationProfile.Select(platform, vendorName, profile, mode);
                Require(options.Any(o => o.Backend == selected.Backend && o.Mode == selected.Mode),
                    $"{platform}/{vendorName}/{profile}/{mode}: selection is outside the offered options");
            }
        }

        // Hardware is offered only for implemented vendor backends, one at most.
        Require(NLAccelerationProfile.HardwareBackend(NLAccelerationVendor.Nvidia, "linux") == "cuda", "NVIDIA Linux backend");
        Require(NLAccelerationProfile.HardwareBackend(NLAccelerationVendor.Intel, "linux") == "qsv", "Intel Linux backend");
        Require(NLAccelerationProfile.HardwareBackend(NLAccelerationVendor.Amd, "linux") == "vaapi", "AMD Linux backend");
        Require(NLAccelerationProfile.HardwareBackend(NLAccelerationVendor.Unknown, "linux").Length == 0,
            "Unknown vendor must not expose hardware");

        // Custom profile is bounded by the same platform/vendor contract.
        var custom = NLAccelerationProfile.CreateCustom("linux", "nvidia", "cuda");
        Require(custom is not null && custom.Backend == "cuda", "Valid custom profile rejected");
        var customOptions = NLAccelerationProfile.OptionsFor("linux", "nvidia", "Balanced", custom);
        Require(customOptions.Count == 2 && customOptions[0].Mode == NLAccelerationMode.Custom,
            "Custom profile must add at most one hardware option");
        Require(NLAccelerationProfile.CreateCustom("linux", "nvidia", "qsv") is null,
            "Custom profile accepted another vendor's backend");
        Require(NLAccelerationProfile.CreateCustom("linux", "nvidia", "invalid-backend") is null,
            "Custom profile accepted an unknown backend");
        Require(NLAccelerationProfile.CreateCustom("linux", "", "cuda") is null,
            "Custom profile accepted an unknown vendor");
        Require(NLAccelerationProfile.Select("linux", "nvidia", "Balanced", NLAccelerationMode.Custom).Backend == "software",
            "Custom mode without a custom profile must fall back to software");

        // Saving profiles never request hardware, whatever the detected GPU is.
        Require(LinuxVisionAcceleration.Select(LinuxVisionSettings.Default with
        {
            Acceleration = "Automatic",
            Profile = "MobileExtreme"
        }) == "software", "Saving profile requested hardware in Automatic mode");
        Require(LinuxVisionAcceleration.Options(LinuxVisionSettings.Default).Count is >= 1 and <= 2,
            "Linux UI options exceed the simplified profile contract");

        Console.WriteLine("VisionEngine acceleration selection checks passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
