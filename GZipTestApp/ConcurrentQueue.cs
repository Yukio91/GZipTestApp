using System.Collections.Generic;
using System.Threading;

namespace GZipTestApp
{
    public class ConcurrentQueue<T>
    {
        private readonly Queue<T> _queue = new Queue<T>();
        private readonly object _locker = new object();

        private readonly int _maxSize;

        private bool _closing;

        public ConcurrentQueue(int maxSize)
        {
            _maxSize = maxSize;
        }

        public int Count
        {
            get
            {
                lock (_locker)
                    return _queue.Count;
            }
        }

        public void Enqueue(T item)
        {
            lock (_locker)
            {
                while (_queue.Count >= _maxSize)
                {
                    Monitor.Wait(_locker);
                }

                _queue.Enqueue(item);
                if (_queue.Count == 1)
                {
                    // wake up any blocked dequeue
                    Monitor.PulseAll(_locker);
                }
            }
        }

        public T Dequeue()
        {
            lock (_locker)
            {
                while (_queue.Count == 0)
                {
                    Monitor.Wait(_locker);
                }

                T item = _queue.Dequeue();
                if (_queue.Count == _maxSize - 1)
                {
                    // wake up any blocked enqueue
                    Monitor.PulseAll(_locker);
                }

                return item;
            }
        }

       
        public bool TryDequeue(out T value)
        {
            lock (_locker)
            {
                while (_queue.Count == 0)
                {
                    if (_closing)
                    {
                        value = default(T);

                        return false;
                    }

                    Monitor.Wait(_locker);
                }
                
                value = _queue.Dequeue();
                if (_queue.Count == _maxSize - 1)
                {
                    // wake up any blocked enqueue
                    Monitor.PulseAll(_locker);
                }

                return true;
            }
        }

        public void Close()
        {
            lock (_locker)
            {
                _closing = true;

                Monitor.PulseAll(_locker);
            }
        }
    }
}