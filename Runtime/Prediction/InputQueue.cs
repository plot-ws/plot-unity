#nullable enable
using System;
using System.Collections.Generic;

namespace Plot.Prediction
{
    /// <summary>A queued, sequenced local input awaiting server acknowledgement.</summary>
    public struct QueueEntry
    {
        public long Seq;
        public object? Input;

        public QueueEntry(long seq, object? input)
        {
            Seq = seq;
            Input = input;
        }
    }

    /// <summary>
    /// Bounded queue of pending local inputs for client-side reconciliation.
    /// Ports packages/handler-client/src/input-queue.ts (cap 200, monotonic
    /// seq, ack-up-to draining).
    /// </summary>
    public class InputQueue
    {
        private const int Cap = 200;
        private readonly List<QueueEntry> _entries = new List<QueueEntry>();

        /// <summary>
        /// Append an entry. Throws if <paramref name="entry"/>.Seq is not
        /// strictly greater than the last. Returns true if the queue overflowed
        /// (oldest entry dropped to stay at the cap).
        /// </summary>
        public bool Push(QueueEntry entry)
        {
            if (_entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (entry.Seq <= last.Seq)
                {
                    throw new InvalidOperationException(
                        $"InputQueue: seq must be monotonically increasing (got {entry.Seq} after {last.Seq})");
                }
            }
            _entries.Add(entry);
            if (_entries.Count > Cap)
            {
                _entries.RemoveAt(0);
                return true;
            }
            return false;
        }

        public void AckUpTo(long seq)
        {
            while (_entries.Count > 0 && _entries[0].Seq <= seq)
            {
                _entries.RemoveAt(0);
            }
        }

        public IReadOnlyList<QueueEntry> Pending()
        {
            return _entries;
        }

        public int Size => _entries.Count;

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
