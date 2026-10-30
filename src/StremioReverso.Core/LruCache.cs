namespace StremioReverso.Core;

public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    readonly int _capacity;
    readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
    readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    readonly object _gate = new();

    public LruCache(int capacity) => _capacity = capacity;

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.Remove(key, out var old)) _order.Remove(old);
            _map[key] = _order.AddFirst((key, value));
            if (_map.Count > _capacity)
            {
                _map.Remove(_order.Last!.Value.Key);
                _order.RemoveLast();
            }
        }
    }
}
