using NOVORA.Linux.VisionEngine;

internal static class VisionSettingsChecks
{
    public static void Run()
    {
        Check(LinuxVisionSettings.ForProfile("Equilibrado").Profile == "Balanced", "Translated profile ID");
        Check(LinuxVisionSettings.ForProfile("Competitivo").Fps == 60, "Competitive FPS");
        Check(LinuxVisionSettings.ForProfile("Software").Acceleration == "Software", "Software decoder");
        string path = Path.Combine(Path.GetTempPath(), "novora-ve-settings-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new LinuxVisionSettingsStore(path);
            var expected = LinuxVisionSettings.Default with { Bitrate = "12M", MaxSize = 1280, RemoteInput = true };
            store.Save(expected);
            Check(store.Load() == expected, "Settings did not persist");
            try { store.Save(expected with { Fps = -1 }); throw new Exception("Invalid settings accepted"); }
            catch (InvalidDataException) { }
            Check(store.Load() == expected, "Invalid settings overwrote valid settings");
            var host=new NOVORA.Linux.Control.LinuxControlHost(store);
            Parallel.For(0,32,index=>host.ConfigureVisionSettings(expected with { Bitrate=$"{2+index%14}M" }));
            Check(store.Load()==host.VisionSettings,"Concurrent settings persistence diverged from live state");
        }
        finally { File.Delete(path); }
        Console.WriteLine("VisionEngine settings checks passed.");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
