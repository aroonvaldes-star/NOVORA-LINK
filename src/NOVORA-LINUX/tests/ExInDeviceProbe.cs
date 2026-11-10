using NOVORA.ExInEngine;
using NOVORA.Integration;
using NOVORA.Linux.ExIn;
using NOVORA.Service;
using System.Runtime.InteropServices;

/// <summary>
/// Physical diagnostic: runs the same ExIn stack as the app (SDL controllers, ADB, Android HID)
/// against one phone and prints each step and the exact failure.
/// Run: dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- --exin-device [adb-serial]
/// </summary>
internal static class ExInDeviceProbe
{
    public static async Task RunAsync(string serial)
    {
        var paths = new NLServiceNovoraPaths();
        Console.WriteLine($"== ADB: {paths.Adb}");
        Console.WriteLine($"== SDL3: {(NativeLibrary.TryLoad("libSDL3.so.0", out IntPtr sdl) ? "libSDL3.so.0 disponible" : "NO disponible")}");
        if (sdl != IntPtr.Zero) NativeLibrary.Free(sdl);

        var adb = new NLServiceADB(paths);
        await using var engine = new ExInCoreEngine(paths);
        engine.Manager.StatusChangedVE += (_, status) => Console.WriteLine($"[estado] {status.State} · {status.Message}");

        Console.WriteLine("== Dispositivos ADB:");
        IReadOnlyList<NLServiceADBDevice> devices = [];
        try { devices = await adb.GetDevicesAsync(forceRefresh: true); }
        catch (Exception ex) { Console.WriteLine("   GetDevicesAsync falló: " + ex); }
        foreach (NLServiceADBDevice device in devices)
            Console.WriteLine($"   {device.Serial} · {device.State} · usb={device.IsUsb} · conectado={device.Connected}");

        Console.WriteLine("== Depuración inalámbrica anunciada por mDNS:");
        try
        {
            foreach (string endpoint in await adb.GetWirelessDebuggingEndpointsAsync())
                Console.WriteLine("   " + endpoint);
        }
        catch (Exception ex) { Console.WriteLine("   mdns services falló: " + ex.Message); }

        Console.WriteLine("== Inicializando ExIn (SDL)…");
        try { await engine.InitializeAsync(); }
        catch (Exception ex) { Console.WriteLine("   InitializeAsync falló: " + ex); }
        Console.WriteLine($"   estado: {engine.Status.State} · {engine.Status.Message}");
        Console.WriteLine($"   mandos detectados: {engine.Status.ConnectedGamepads}");
        foreach (ExInControllerChoice choice in engine.Manager.ControllersVE)
            Console.WriteLine($"   · {choice.Label}");

        string? target = string.IsNullOrWhiteSpace(serial)
            ? devices.FirstOrDefault(device => device.Connected)?.Serial
            : serial;
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.WriteLine("== Sin teléfono ADB conectado. Conecta el cable o la depuración inalámbrica.");
            return;
        }

        await using var session = new ExInControlSession(engine, paths, adb);
        Console.WriteLine($"== Conectando HID con {target}…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await session.StartAsync(target, timeout.Token);
            Console.WriteLine($"   StartAsync terminó · IsRunning={session.IsRunning} · Serial={session.Serial}");
        }
        catch (Exception ex) { Console.WriteLine("   StartAsync falló: " + ex); }
        await Task.Delay(TimeSpan.FromSeconds(5));
        Console.WriteLine($"== Final · IsRunning={session.IsRunning} · estado={engine.Status.State} · {engine.Status.Message}");
        await session.StopAsync();
    }
}
