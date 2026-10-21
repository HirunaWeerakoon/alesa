using System.Collections.Generic;

namespace Alesa.Core.Graph
{
    /// <summary>
    /// Binary min-heap keyed by float priority. Unity's .NET profile has no PriorityQueue,
    /// so searches use this. Duplicates are allowed; callers skip stale entries (lazy deletion).
    /// </summary>
    public sealed class MinHeap<T>
    {
        private readonly List<(float priority, T item)> _items = new List<(float, T)>();

        public int Count => _items.Count;

        public void Push(T item, float priority)
        {
            _items.Add((priority, item));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_items[parent].priority <= _items[i].priority) break;
                Swap(i, parent);
                i = parent;
            }
        }

        public T Pop(out float priority)
        {
            var top = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, smallest = i;
                if (l < _items.Count && _items[l].priority < _items[smallest].priority) smallest = l;
                if (r < _items.Count && _items[r].priority < _items[smallest].priority) smallest = r;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }

            priority = top.priority;
            return top.item;
        }

        public void Clear() => _items.Clear();

        private void Swap(int a, int b)
        {
            var tmp = _items[a];
            _items[a] = _items[b];
            _items[b] = tmp;
        }
    }
}
