#nullable enable
using System.Collections.Generic;

namespace Plot.Interpolation
{
    /// <summary>A single timestamped snapshot of room state.</summary>
    public struct Snapshot
    {
        public long Ts;
        public object? State;

        public Snapshot(long ts, object? state)
        {
            Ts = ts;
            State = state;
        }
    }

    /// <summary>
    /// The bracketing pair returned by <see cref="SnapshotBuffer.Lookup"/>:
    /// <c>A</c> is the left anchor and <c>B</c> is the right anchor (or null
    /// when the target is clamped to an end or only one snapshot exists).
    /// </summary>
    public struct Pair
    {
        public Snapshot A;
        public Snapshot? B;

        public Pair(Snapshot a, Snapshot? b)
        {
            A = a;
            B = b;
        }
    }

    /// <summary>
    /// Bounded ring of snapshots within a fixed time horizon. Ports
    /// packages/client/src/interpolation/snapshot-buffer.ts.
    /// </summary>
    public class SnapshotBuffer
    {
        private readonly List<Snapshot> _snapshots = new List<Snapshot>();
        private readonly long _horizonMs;

        public SnapshotBuffer(long horizonMs)
        {
            _horizonMs = horizonMs;
        }

        public void Push(long ts, object? state)
        {
            if (_snapshots.Count > 0)
            {
                var last = _snapshots[_snapshots.Count - 1];
                if (ts <= last.Ts) return;
            }
            _snapshots.Add(new Snapshot(ts, state));
            long cutoff = ts - _horizonMs;
            // Keep snapshots[0] as a left-side anchor whenever snapshots[1] is
            // still stale — drop the head only when the next snapshot is itself
            // within the horizon, so pair lookup is well-defined for any target
            // in the buffer's range.
            while (_snapshots.Count > 1 && _snapshots[1].Ts < cutoff)
            {
                _snapshots.RemoveAt(0);
            }
        }

        public Pair? Lookup(long targetTs)
        {
            if (_snapshots.Count == 0) return null;
            if (_snapshots.Count == 1) return new Pair(_snapshots[0], null);
            var first = _snapshots[0];
            var last = _snapshots[_snapshots.Count - 1];
            if (targetTs <= first.Ts) return new Pair(first, null);
            if (targetTs >= last.Ts) return new Pair(last, null);
            for (int i = 0; i < _snapshots.Count - 1; i++)
            {
                var a = _snapshots[i];
                var b = _snapshots[i + 1];
                if (a.Ts <= targetTs && targetTs < b.Ts) return new Pair(a, b);
            }
            // Defensive fallback: the loop above is exhaustive for any target in
            // (first.Ts, last.Ts) — the clamps above rule out the boundaries.
            return new Pair(last, null);
        }

        public int Size => _snapshots.Count;

        public Snapshot? Oldest => _snapshots.Count > 0 ? _snapshots[0] : (Snapshot?)null;

        public Snapshot? Newest =>
            _snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1] : (Snapshot?)null;
    }
}
