using NOVORA.Linux.VisionEngine;
internal static class VisionAccelerationChecks
{
    public static async Task RunAsync()
    {
        if (LinuxVisionAcceleration.Select(LinuxVisionSettings.Default with { Acceleration="Software" }) != "software") throw new Exception("Software mode not respected");
        var result = await LinuxVisionAcceleration.ProbeBackendAsync("invalid",CancellationToken.None);
        if (result.Available) throw new Exception("Invalid accelerator accepted");
        try { LinuxVisionAcceleration.Select(LinuxVisionSettings.Default with { Acceleration="Forced", HardwareBackend="invalid" }); throw new Exception("Forced acceleration silently fell back"); }
        catch (InvalidOperationException) { }
        var available = await LinuxVisionAcceleration.ProbeAsync(CancellationToken.None);
        foreach (var backend in available)
        {
            Console.WriteLine(backend);
            if(!backend.Available)
            {
                try { LinuxVisionAcceleration.Select(LinuxVisionSettings.Default with { Acceleration="Forced",HardwareBackend=backend.Name }); throw new Exception("Unavailable forced GPU silently fell back"); }
                catch(InvalidOperationException) { }
            }
        }
        Console.WriteLine("VisionEngine acceleration selection checks passed.");
    }
}
