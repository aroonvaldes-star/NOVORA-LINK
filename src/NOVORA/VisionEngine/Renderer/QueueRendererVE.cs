namespace NOVORA.VisionEngine.Renderer;

public sealed class QueueRendererVE<T> : IDisposable
    where T : class, IDisposable
{
    private readonly object _gateVE = new();
    private readonly Queue<T> _itemsVE = new();
    private readonly int _capacityVE;
    private bool _disposedVE;

    public QueueRendererVE(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacityVE = capacity;
    }

    public int CapacityVE => _capacityVE;

    public int CountVE
    {
        get
        {
            lock (_gateVE)
                return _itemsVE.Count;
        }
    }

    public int EnqueueVE(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_gateVE)
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);

            int dropped = 0;
            while (_itemsVE.Count >= _capacityVE)
            {
                _itemsVE.Dequeue().Dispose();
                dropped++;
            }

            _itemsVE.Enqueue(item);
            return dropped;
        }
    }

    public bool TryTakeLatestVE(out T? item, out int dropped)
    {
        lock (_gateVE)
        {
            ObjectDisposedException.ThrowIf(_disposedVE, this);

            item = null;
            dropped = 0;

            if (_itemsVE.Count == 0)
                return false;

            while (_itemsVE.Count > 1)
            {
                _itemsVE.Dequeue().Dispose();
                dropped++;
            }

            item = _itemsVE.Dequeue();
            return true;
        }
    }

    public int DrainVE()
    {
        lock (_gateVE)
        {
            int count = 0;
            while (_itemsVE.Count > 0)
            {
                _itemsVE.Dequeue().Dispose();
                count++;
            }
            return count;
        }
    }

    public void Dispose()
    {
        lock (_gateVE)
        {
            if (_disposedVE)
                return;

            while (_itemsVE.Count > 0)
                _itemsVE.Dequeue().Dispose();

            _disposedVE = true;
        }
    }
}
