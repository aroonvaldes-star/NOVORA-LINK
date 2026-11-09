using System.Buffers.Binary;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Avalonia;
using NOVORA.Linux.VisionEngine;

internal static class VisionInputChecks
{
    public static Task RunAsync()
    {
        Check(LinuxSdlDevices.ResolveId([new(7,"Same"),new(9,"Same")],9,"Same","monitor")==9,
            "Current exact output selection must disambiguate duplicate names.");
        try { LinuxSdlDevices.ResolveId([new(7,"Same"),new(9,"Same")],99,"Same","monitor"); throw new Exception("Ambiguous stale selection accepted"); }
        catch(InvalidOperationException) { }
        var input = new LinuxVisionInput();
        var messages = new List<JsonElement>();
        input.Frame += bytes =>
        {
            Check(BinaryPrimitives.ReadInt32BigEndian(bytes) == bytes.Length - 4, "Input framing must match AppControl.");
            using JsonDocument json = JsonDocument.Parse(bytes.AsMemory(4));
            messages.Add(json.RootElement.Clone());
        };
        input.UpdateViewport(new VideoSession(1080, 1920, 90, 1024), 500, 500, 1000, 1000);
        Check(!input.Button(1, true, 10, 10), "Letterbox clicks must be ignored.");
        Check(input.Button(1, true, 250, 250), "Touchpad tap should hit the image.");
        JsonElement down = messages[^1];
        Check(down.GetProperty("ScreenWidth").GetInt32() == 1920 && down.GetProperty("ScreenHeight").GetInt32() == 1080,
            "Rotated image must use visible geometry.");
        Check(Math.Abs(down.GetProperty("X").GetInt32() - 960) <= 2 && Math.Abs(down.GetProperty("Y").GetInt32() - 540) <= 2,
            "HiDPI touchpad center must map to Android center.");
        input.Motion(300, 270);
        Check(messages[^1].GetProperty("MotionAction").GetInt32() == 2, "Touchpad drag must preserve pointer.");
        input.ReleaseAll();
        Check(messages[^1].GetProperty("MotionAction").GetInt32() == 3, "Focus loss must cancel active drag.");
        int before = messages.Count;
        input.Motion(300, 270);
        Check(messages.Count == before, "Focus release must not leave a pressed pointer.");
        input.Scroll(250, 250, .125f, -.25f, false);
        Check(messages[^1].GetProperty("HorizontalScroll").GetSingle() == .125f &&
            messages[^1].GetProperty("VerticalScroll").GetSingle() == -.25f, "Fractional two-finger scroll must be preserved.");
        input.Scroll(250, 250, .125f, -.25f, true);
        Check(messages[^1].GetProperty("VerticalScroll").GetSingle() == .25f, "Flipped touchpad direction must normalize.");
        input.Text("á🙂日本語");
        Check(messages[^1].GetProperty("Text").GetString() == "á🙂日本語", "UTF-8 text must survive framing.");
        input.Key(80, 0x00c0, true, false); // SDL left arrow + ctrl
        Check(messages[^1].GetProperty("Keycode").GetInt32() == 21 &&
            messages[^1].GetProperty("MetaState").GetInt32() == 0x1000, "Android keycodes/modifiers must match contract.");
        input.ReleaseAll();
        Check(messages[^1].GetProperty("KeyAction").GetInt32() == 1, "Focus loss must release held keys.");
        Console.WriteLine("VisionEngine keyboard/touchpad geometry, wire and focus checks passed.");
        return Task.CompletedTask;
    }

    public static void RunNative()
    {
        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
        string? oldDriver = Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER");
        string? oldRenderer = Environment.GetEnvironmentVariable("NOVORA_SDL_RENDERER");
        NativeEnvironment.Set("SDL_VIDEO_DRIVER", "dummy");
        Environment.SetEnvironmentVariable("NOVORA_SDL_RENDERER", "software");
        IntPtr library = IntPtr.Zero, text = IntPtr.Zero;
        try
        {
            using var renderer = new LinuxSdlVideoRenderer(LinuxVisionSettings.Default with { VSync = false });
            renderer.UpdateSession(new VideoSession(64, 64, 0, 1024));
            renderer.Present(new byte[64 * 64 * 4], 64, 64);
            library = NativeLibrary.Load("libSDL3.so.0");
            T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var windows = Load<GetWindows>("SDL_GetWindows");
            var getId = Load<GetWindowId>("SDL_GetWindowID");
            var free = Load<Free>("SDL_free");
            var push = Load<PushEvent>("SDL_PushEvent");
            IntPtr list = windows(out int count);
            Check(list != IntPtr.Zero && count == 1, "Native input test requires its own SDL window.");
            uint id;
            try { id = getId(Marshal.ReadIntPtr(list)); }
            finally { free(list); }
            var messages = new List<JsonElement>();
            renderer.Input += frame =>
            {
                using JsonDocument json = JsonDocument.Parse(frame.AsMemory(4));
                messages.Add(json.RootElement.Clone());
            };
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            bool closed = false;
            renderer.Closed += () => { closed = true; stop.Cancel(); };
            void Send(uint type, Action<IntPtr>? fill = null)
            {
                IntPtr e = Marshal.AllocHGlobal(128);
                try
                {
                    Marshal.Copy(new byte[128], 0, e, 128);
                    Marshal.WriteInt32(e, unchecked((int)type));
                    Marshal.WriteInt32(e, 16, unchecked((int)id));
                    fill?.Invoke(e);
                    Check(push(e), "SDL_PushEvent failed.");
                }
                finally { Marshal.FreeHGlobal(e); }
            }
            static void Float(IntPtr e, int offset, float value) => Marshal.WriteInt32(e, offset, BitConverter.SingleToInt32Bits(value));
            text = Marshal.StringToCoTaskMemUTF8("á🙂");
            Send(0x303, e => Marshal.WriteIntPtr(e, 24, text));
            Send(0x403, e => { Float(e, 24, .125f); Float(e, 28, -.25f); Float(e, 36, 32); Float(e, 40, 32); });
            Send(0x401, e => { Marshal.WriteByte(e, 24, 1); Float(e, 28, 32); Float(e, 32, 32); });
            Send(0x400, e => { Float(e, 28, 40); Float(e, 32, 40); });
            int stage = 0;
            Exception? failure = null;
            var control = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
            {
                try
                {
                    if (stage++ == 0)
                    {
                        Check(messages.Any(m => m.GetProperty("Type").GetInt32() == 1 && m.GetProperty("Text").GetString() == "á🙂"),
                            "SDL text event did not reach AppControl input.");
                        Check(messages.Any(m => m.GetProperty("Type").GetInt32() == 3 && m.GetProperty("VerticalScroll").GetSingle() == -.25f),
                            "Native fractional touchpad wheel event was lost.");
                        Check(messages.Any(m => m.GetProperty("Type").GetInt32() == 2 && m.GetProperty("MotionAction").GetInt32() == 2),
                            "Native touchpad drag was lost.");
                        Send(0x20f);
                    }
                    else
                    {
                        Check(messages.Any(m => m.GetProperty("Type").GetInt32() == 2 && m.GetProperty("MotionAction").GetInt32() == 3),
                            "SDL focus loss did not cancel the remote drag.");
                        Send(0x210);
                    }
                }
                catch (Exception ex) { failure = ex; stop.Cancel(); }
            });
            control.Start();
            try { Dispatcher.UIThread.MainLoop(stop.Token); }
            finally { control.Stop(); }
            if (failure is not null) throw failure;
            Check(closed, "Window close was not processed while no new video frames arrived.");
            Console.WriteLine("VisionEngine native SDL text/touchpad/focus/window-close checks passed without incoming video frames.");
        }
        finally
        {
            if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text);
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
            NativeEnvironment.Set("SDL_VIDEO_DRIVER", oldDriver);
            Environment.SetEnvironmentVariable("NOVORA_SDL_RENDERER", oldRenderer);
        }
    }

    public static void RunOutputsNative()
    {
        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
        NativeEnvironment.Set("SDL_VIDEO_DRIVER", "dummy");
        NativeEnvironment.Set("SDL_AUDIO_DRIVER", "dummy");
        Environment.SetEnvironmentVariable("NOVORA_SDL_RENDERER", "software");
        LinuxOutputDevice monitor = LinuxSdlDevices.Monitors().First(device => device.Id != 0);
        LinuxOutputDevice audio = LinuxSdlDevices.AudioOutputs().First(device => device.Id != 0);
        for (int repetition = 0; repetition < 4; repetition++)
        {
            Check(LinuxSdlDevices.Monitors().Any(device => device.Id == monitor.Id && device.Name == monitor.Name),
                "Enumerating monitors invalidated the previously selected display instance ID.");
            Check(LinuxSdlDevices.AudioOutputs().Any(device => device.Id == audio.Id && device.Name == audio.Name),
                "Enumerating audio outputs invalidated the previously selected audio instance ID.");
        }
        using (var renderer = new LinuxSdlVideoRenderer(LinuxVisionSettings.Default with { MonitorId = monitor.Id, VSync = false }))
            renderer.Present(new byte[64 * 64 * 4], 64, 64);
        using (var player = new LinuxSdlAudioPlayer(audio.Id)) player.Play(new byte[1920]);
        // Persisted SDL instance IDs can change on restart; the saved name must select the current instance.
        Check(LinuxSdlDevices.ResolveMonitorId(uint.MaxValue, monitor.Name) == monitor.Id,
            "A saved monitor name did not resolve after the old process ID became invalid.");
        Check(LinuxSdlDevices.ResolveAudioDeviceId(uint.MaxValue, audio.Name) == audio.Id,
            "A saved audio output name did not resolve after the old process ID became invalid.");
        using (var renderer = new LinuxSdlVideoRenderer(LinuxVisionSettings.Default with
            { MonitorId = uint.MaxValue, MonitorName = monitor.Name, VSync = false }))
            renderer.Present(new byte[64 * 64 * 4], 64, 64);
        using (var player = new LinuxSdlAudioPlayer(uint.MaxValue, audio.Name)) player.Play(new byte[1920]);
        Console.WriteLine("VisionEngine selected monitor/audio IDs survive repeated enumeration and open successfully.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetWindows(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetWindowId(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool PushEvent(IntPtr e);

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
