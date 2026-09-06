namespace NOVORA.VisionEngine.Performance;

public sealed class QueuePerformanceVE<T>
{
    private sealed record EntryVE(T Item, PriorityPerformanceVE Priority);
    private readonly int _capacityVE;
    private readonly LinkedList<EntryVE> _queueVE = new();
    private readonly object _gateVE = new();

    public QueuePerformanceVE(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacityVE = capacity;
    }

    public int CapacityVE => _capacityVE;

    public int CountVE
    {
        get { lock (_gateVE) return _queueVE.Count; }
    }

    public bool TryEnqueueVE(T item, PriorityPerformanceVE priority)
    {
        lock (_gateVE)
        {
            if (_queueVE.Count < _capacityVE)
            {
                _queueVE.AddLast(new EntryVE(item, priority));
                return true;
            }

            LinkedListNode<EntryVE>? candidate = _queueVE.First;
            while (candidate is not null && candidate.Value.Priority >= priority)
                candidate = candidate.Next;

            if (candidate is null)
                return false;

            _queueVE.Remove(candidate);
            _queueVE.AddLast(new EntryVE(item, priority));
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

    public BufferPerformanceVE SnapshotVE()
        => BufferPerformanceVE.CreateVE(_capacityVE, CountVE);
}
