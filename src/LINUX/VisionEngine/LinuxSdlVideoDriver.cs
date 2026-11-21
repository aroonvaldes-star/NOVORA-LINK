using System.Runtime.InteropServices;

namespace NOVORA.Linux.VisionEngine;

/// <summary>
/// SDL video is only used by VisionEngine. On a Wayland session it runs through XWayland (X11 driver)
/// so SDL can place the VisionEngine window and read its position. The Avalonia UI stays on Wayland.
/// SDL chooses its driver once per process at video init, so every video init goes through here.
/// </summary>
internal static class LinuxSdlVideoDriver
{
    private const string Hint = "SDL_VIDEO_DRIVER";

    /// <summary>Runs SDL video initialization, preferring X11 (XWayland) and falling back to the default driver.</summary>
    public static bool InitVideo(IntPtr library, Func<bool> init)
    {
        if (!ShouldPreferX11()) return init();
        var setHint = Marshal.GetDelegateForFunctionPointer<SetHint>(NativeLibrary.GetExport(library, "SDL_SetHint"));
        setHint(Hint, "x11");
        if (init()) return true;
        // XWayland missing or refused: the default driver is still better than no VisionEngine window.
        setHint(Hint, "");
        return init();
    }

    private static bool ShouldPreferX11() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Hint)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
}
