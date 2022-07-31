using System.Collections.Concurrent;

namespace HallPass.Helpers
{
    internal class ConcurrentFixedSizeQueue<T>
    {
        private readonly ConcurrentQueue<T> _queue = new();
        private readonly ConcurrentDictionary<T, bool> _set = new();

        public int MaxSize { get; }

        public ConcurrentFixedSizeQueue(int maxSize)
        {
            MaxSize = maxSize;
        }

        public bool TryAdd(T item)
        {
            if (_set.TryAdd(item, true))
            {
                // doesn't exist yet, so we can add it
                _queue.Enqueue(item);

                if (_queue.Count >= MaxSize)
                    _queue.TryDequeue(out _);

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
