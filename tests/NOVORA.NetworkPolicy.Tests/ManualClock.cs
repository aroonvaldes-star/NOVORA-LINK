internal sealed class ManualClock : TimeProvider
{
    private readonly object _gate=new();
    private readonly List<Timer> _timers=[];
    private long _now;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() { lock(_gate)return _now; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    { var timer=new Timer(this,callback,state); timer.Change(dueTime,period); return timer; }
    public void Advance(long milliseconds)
    {
        List<Timer> due;
        lock(_gate) { _now+=milliseconds; due=_timers.Where(t=>t.Next<=_now).ToList(); foreach(var t in due) { if(t.Period<0) { t.Next=long.MaxValue; _timers.Remove(t); } else t.Next=_now+t.Period; } }
        foreach(var timer in due) timer.Fire();
    }
    private sealed class Timer(ManualClock owner, TimerCallback callback, object? state) : ITimer
    {
        public long Next=long.MaxValue,Period=-1;
        private bool _disposed;
        public bool Change(TimeSpan dueTime,TimeSpan period)
        {
            lock(owner._gate)
            {
                if(_disposed)return false;
                owner._timers.Remove(this); Period=period==Timeout.InfiniteTimeSpan?-1:(long)Math.Ceiling(period.TotalMilliseconds);
                Next=dueTime==Timeout.InfiniteTimeSpan?long.MaxValue:owner._now+(long)Math.Ceiling(dueTime.TotalMilliseconds);
                if(Next!=long.MaxValue) owner._timers.Add(this); return true;
            }
        }
        public void Fire() { lock(owner._gate)if(_disposed)return; callback(state); }
        public void Dispose() { lock(owner._gate) { _disposed=true;owner._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose();return ValueTask.CompletedTask; }
    }
}
