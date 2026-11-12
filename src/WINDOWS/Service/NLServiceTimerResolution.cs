using System.Runtime;
using System.Runtime.InteropServices;

namespace NOVORA.Service;

/// <summary>
/// Raises the Windows timer resolution to 1 ms and prefers low-latency GC while a real-time
/// engine (VisionEngine video, ExIn output) runs. Measured on this PC: an 8 ms wait fired at
/// p50 12.5 ms with the default resolution and 8.3 ms with 1 ms. Reference counted, so the
/// system setting is restored when the last engine stops.
/// </summary>
public static class NLServiceTimerResolution
{
    private static readonly object Gate = new();
    private static int _holders;
    private static GCLatencyMode _previousLatencyMode;

    public static IDisposable Acquire()
    {
        lock (Gate)
        {
            if (_holders++ == 0)
            {
                if (OperatingSystem.IsWindows()) TimeBeginPeriod(1);
                _previousLatencyMode = GCSettings.LatencyMode;
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            }
        }
        return new Holder();
    }

    private static void Release()
    {
        lock (Gate)
        {
            if (_holders == 0 || --_holders != 0) return;
            if (OperatingSystem.IsWindows()) TimeEndPeriod(1);
            GCSettings.LatencyMode = _previousLatencyMode;
        }
    }

    private sealed class Holder : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) Release();
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
