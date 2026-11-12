namespace NOVORA.VisionEngine.Performance;

public sealed class VEPerformanceQueue<T>
{
    private sealed record VEPerformanceEntry(T Item, VEPerformancePriority Priority);
    private readonly int _capacityVE;
    private readonly LinkedList<VEPerformanceEntry> _queueVE = new();
    private readonly object _gateVE = new();

    public VEPerformanceQueue(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacityVE = capacity;
    }

    public int CapacityVE => _capacityVE;

    public int CountVE
    {
        get { lock (_gateVE) return _queueVE.Count; }
    }

    public bool TryEnqueueVE(T item, VEPerformancePriority priority)
    {
        lock (_gateVE)
        {
            if (_queueVE.Count < _capacityVE)
            {
                _queueVE.AddLast(new VEPerformanceEntry(item, priority));
                return true;
            }

            LinkedListNode<VEPerformanceEntry>? candidate = _queueVE.First;
            while (candidate is not null && candidate.Value.Priority >= priority)
                candidate = candidate.Next;

            if (candidate is null)
                return false;

            _queueVE.Remove(candidate);
            _queueVE.AddLast(new VEPerformanceEntry(item, priority));
            return true;
        }
    }

    public bool TryDequeueVE(out T item)
    {
        lock (_gateVE)
        {
            if (_queueVE.First is null)
            {
                item = default!;
                return false;
            }

            item = _queueVE.First.Value.Item;
            _queueVE.RemoveFirst();
            return true;
        }
    }

    public VEPerformanceBuffer SnapshotVE()
        => VEPerformanceBuffer.CreateVE(_capacityVE, CountVE);
}
