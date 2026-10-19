using System.Threading.Channels;

namespace NOVORA.Service
{
    public sealed class NLServiceNovoraPaths { }
    public sealed record Device(string Serial, bool Connected, bool IsWifiConnection);
    public sealed class NLServiceADB
    {
        public Device[] Devices = [new("usb-A", true, false)];
        public Dictionary<string,string> Identity = new() { ["usb-A"] = "phone-A" };
        public List<NOVORA.Integration.ExInHidOutput> Outputs = [];
        public bool BlockLookup;
        public TaskCompletionSource LookupBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartServerAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task<string> GetHardwareSerialAsync(string serial, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Identity[serial]); }
        public async Task<Device[]> GetDevicesAsync(CancellationToken token, bool forceRefresh)
        {
            if (BlockLookup) { LookupBlocked.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return Devices;
        }
    }
}
namespace NOVORA.ExInEngine
{
    public enum ExInInputMode { Game, Ui }
    public sealed record ExInModeResult(bool Success, ExInInputMode Mode, int Devices, string Message, string? Code = null);
    public sealed class ManagerFixture { public ExInInputMode ModeVE => ExInInputMode.Game; }
    public sealed class ExInCoreEngine
    {
        public ManagerFixture Manager { get; } = new();
        public bool IsEnabledVE { get; set; } = true;
        public int FailAttachCount, AttachAttempts;
        public bool BlockAttach;
        public TaskCompletionSource AttachBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NOVORA.Integration.ExInHidOutput? Bound;
        public async Task AttachOutputAsync(NOVORA.Integration.ExInHidOutput output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); AttachAttempts++; Bound = output;
            if (FailAttachCount-- > 0) throw new IOException("HID provider unavailable.");
            if (BlockAttach) { AttachBlocked.TrySetResult(); await Task.Delay(Timeout.Infinite,token); }
        }
        public Task DetachOutputAsync(NOVORA.Integration.ExInHidOutput output) { if (ReferenceEquals(Bound,output)) Bound=null; return Task.CompletedTask; }
        public Task<ExInModeResult> ReactivateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(new ExInModeResult(true,ExInInputMode.Game,1,"ready")); }
    }
}
namespace NOVORA.Integration
{
    public sealed class ExInHidOutput : IAsyncDisposable
    {
        public string Serial { get; }
        public bool Disposed, FailedState;
        public bool IsReady => !Disposed && !FailedState;
        internal event EventHandler? Failed;
        internal ExInHidOutput(NOVORA.Service.NLServiceADB adb, NOVORA.Service.NLServiceNovoraPaths paths, string serial) { Serial=serial; adb.Outputs.Add(this); }
        public void Fail() { FailedState=true; Failed?.Invoke(this,EventArgs.Empty); }
        public ValueTask DisposeAsync() { Disposed=true; return ValueTask.CompletedTask; }
    }
}
internal sealed class ControlledTime : TimeProvider
{
    private readonly Channel<ControlledTimer> _timers = Channel.CreateUnbounded<ControlledTimer>();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ControlledTimer(callback,state,_timers.Writer); timer.Change(dueTime,period); return timer;
    }
    public async Task<bool> TickAsync(TimeSpan? limit = null)
    {
        using var timeout = new CancellationTokenSource(limit ?? TimeSpan.FromSeconds(1));
        try
        {
            while (true)
            {
                ControlledTimer timer=await _timers.Reader.ReadAsync(timeout.Token);
                if (timer.Disposed) continue;
                timer.Fire();
                // A newly registered delay marks completion of the attempt, including error handling.
                await _timers.Reader.WaitToReadAsync(timeout.Token);
                return true;
            }
        }
        catch(OperationCanceledException) { return false; }
    }
    private sealed class ControlledTimer(TimerCallback callback, object? state, ChannelWriter<ControlledTimer> writer) : ITimer
    {
        public bool Disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) { if (Disposed) return false; if (dueTime!=Timeout.InfiniteTimeSpan) writer.TryWrite(this); return true; }
        public void Fire() { if (!Disposed) callback(state); }
        public void Dispose() => Disposed=true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
