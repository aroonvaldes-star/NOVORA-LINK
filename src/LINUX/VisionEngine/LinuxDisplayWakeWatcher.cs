using System.Runtime.InteropServices;

namespace NOVORA.Linux.VisionEngine;

/// <summary>
/// Blocks on the display connection file descriptor (Wayland or X11) and calls <c>wake</c>
/// when the compositor or X server has data. Replaces a fixed-interval SDL event timer:
/// input is handled as soon as the display reports it, with no periodic polling.
/// The 500 ms poll timeout only lets the thread observe <see cref="Dispose"/>.
/// <c>wake</c> returns false when a pump is already queued; the thread then backs off so a
/// stalled UI thread does not turn the level-triggered poll into a busy loop. If the display
/// connection reports an error, <c>failed</c> is raised once and the thread exits.
/// </summary>
internal sealed class LinuxDisplayWakeWatcher : IDisposable
{
    private const short PollIn = 0x001;
    private const short PollErrorMask = 0x008 | 0x010 | 0x020; // POLLERR | POLLHUP | POLLNVAL
    private const int EIntr = 4;

    private readonly int _fd;
    private readonly Func<bool> _wake;
    private readonly Action _failed;
    private volatile bool _stop;
    private Thread? _thread;

    private LinuxDisplayWakeWatcher(int fd, Func<bool> wake, Action failed)
    {
        _fd = fd;
        _wake = wake;
        _failed = failed;
    }

    /// <summary>Returns a watcher for the SDL window's display connection, or null when neither Wayland nor X11 exposes one.</summary>
    internal static LinuxDisplayWakeWatcher? TryCreate(IntPtr waylandDisplay, IntPtr x11Display, Func<bool> wake, Action failed)
    {
        int fd = -1;
        if (waylandDisplay != IntPtr.Zero)
            fd = WaylandDisplayFd(waylandDisplay);
        else if (x11Display != IntPtr.Zero)
            fd = X11ConnectionNumber(x11Display);
        return fd >= 0 ? new LinuxDisplayWakeWatcher(fd, wake, failed) : null;
    }

    internal void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "NOVORA-VE-DisplayWake" };
        _thread.Start();
    }

    private void Run()
    {
        var pfd = new PollFd { Fd = _fd, Events = PollIn };
        while (!_stop)
        {
            pfd.Revents = 0;
            int ready = poll(ref pfd, 1, 500);
            if (ready < 0)
            {
                if (Marshal.GetLastPInvokeError() == EIntr) continue;
                Fail();
                return;
            }
            if (ready == 0) continue;
            if ((pfd.Revents & PollErrorMask) != 0)
            {
                Fail();
                return;
            }
            // El fd sigue legible hasta que la UI lee SDL: si la bomba ya está en cola, no girar en vacío.
            if ((pfd.Revents & PollIn) != 0 && !_stop && !_wake())
                Thread.Sleep(4);
        }
    }

    private void Fail()
    {
        if (!_stop) _failed();
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread is not null && _thread != Thread.CurrentThread)
            _thread.Join(TimeSpan.FromSeconds(2));
    }

    private static int WaylandDisplayFd(IntPtr display)
    {
        IntPtr lib = NativeLibrary.Load("libwayland-client.so.0");
        var get = Marshal.GetDelegateForFunctionPointer<WlDisplayGetFd>(NativeLibrary.GetExport(lib, "wl_display_get_fd"));
        return get(display);
    }

    private static int X11ConnectionNumber(IntPtr display)
    {
        IntPtr lib = NativeLibrary.Load("libX11.so.6");
        var get = Marshal.GetDelegateForFunctionPointer<XConnectionNumber>(NativeLibrary.GetExport(lib, "XConnectionNumber"));
        return get(display);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(ref PollFd fds, nuint nfds, int timeout);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WlDisplayGetFd(IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int XConnectionNumber(IntPtr display);
}
