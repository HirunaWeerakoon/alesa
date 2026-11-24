using System;
using System.Collections.Generic;

namespace Alesa.Core
{
    /// <summary>
    /// Lightweight, high-performance binary min-heap for priority queue operations.
    /// Fully independent of .NET 6+ System.Collections.Generic.PriorityQueue to ensure
    /// 100% compatibility across all Unity runtime profiles (Mono, .NET Standard 2.1, IL2CPP).
    /// </summary>
    /// <typeparam name="TElement">The payload element type.</typeparam>
    /// <typeparam name="TPriority">The priority type (must implement IComparable).</typeparam>
    public sealed class MinHeap<TElement, TPriority> where TPriority : IComparable<TPriority>
    {
        private struct HeapEntry
        {
            public TElement Element;
            public TPriority Priority;
            public long InsertionOrder;

            public HeapEntry(TElement element, TPriority priority, long insertionOrder)
            {
                Element = element;
                Priority = priority;
                InsertionOrder = insertionOrder;
            }
        }

        private HeapEntry[] _heap;
        private int _count;
        private long _insertionCounter;

        public int Count => _count;
        public bool IsEmpty => _count == 0;

        public MinHeap(int initialCapacity = 16)
        {
            if (initialCapacity < 1) initialCapacity = 16;
            _heap = new HeapEntry[initialCapacity];
            _count = 0;
            _insertionCounter = 0;
        }

        public void Enqueue(TElement element, TPriority priority)
        {
            if (_count == _heap.Length)
            {
                Array.Resize(ref _heap, _heap.Length * 2);
            }

            var entry = new HeapEntry(element, priority, _insertionCounter++);
            _heap[_count] = entry;
            SiftUp(_count);
            _count++;
        }

        public TElement Dequeue()
        {
            if (_count == 0)
                throw new InvalidOperationException("Heap is empty.");

            TElement result = _heap[0].Element;
            _count--;

            if (_count > 0)
            {
                _heap[0] = _heap[_count];
                _heap[_count] = default;
                SiftDown(0);
            }
            else
            {
                _heap[0] = default;
            }

            return result;
        }

        public TElement Peek()
        {
            if (_count == 0)
                throw new InvalidOperationException("Heap is empty.");
            return _heap[0].Element;
        }

        public void Clear()
        {
            Array.Clear(_heap, 0, _count);
            _count = 0;
            _insertionCounter = 0;
        }

        private void SiftUp(int index)
        {
            var item = _heap[index];
            while (index > 0)
            {
                int parentIndex = (index - 1) >> 1;
                var parent = _heap[parentIndex];

                if (Compare(item, parent) >= 0)
                    break;

                _heap[index] = parent;
                index = parentIndex;
            }
            _heap[index] = item;
        }

        private void SiftDown(int index)
        {
            var item = _heap[index];
            int half = _count >> 1;

            while (index < half)
            {
                int leftChild = (index << 1) + 1;
                int rightChild = leftChild + 1;
                int bestChild = leftChild;

                if (rightChild < _count && Compare(_heap[rightChild], _heap[leftChild]) < 0)
                {
                    bestChild = rightChild;
                }

                if (Compare(item, _heap[bestChild]) <= 0)
                    break;

                _heap[index] = _heap[bestChild];
                index = bestChild;
            }
            _heap[index] = item;
        }

        private static int Compare(in HeapEntry a, in HeapEntry b)
        {
            int cmp = a.Priority.CompareTo(b.Priority);
            if (cmp != 0) return cmp;
            // Deterministic FIFO tie-breaking for equal priority
            return a.InsertionOrder.CompareTo(b.InsertionOrder);
        }
    }
}

