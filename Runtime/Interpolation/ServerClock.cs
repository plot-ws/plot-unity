#nullable enable
using System;
using System.Collections.Generic;

namespace Plot.Interpolation
{
    /// <summary>
    /// Estimates the client/server clock offset and connection jitter from a
    /// sliding window of (clientNow - serverTs) samples. Ports
    /// packages/client/src/interpolation/server-clock.ts.
    /// </summary>
    public class ServerClock
    {
        private const int Window = 8;
        private readonly List<long> _samples = new List<long>();

        public void Observe(long clientNow, long serverTs)
        {
            _samples.Add(clientNow - serverTs);
            if (_samples.Count > Window) _samples.RemoveAt(0);
        }

        /// <summary>Median of the recent (clientNow - serverTs) samples.</summary>
        public double Offset
        {
            get
            {
                if (_samples.Count == 0) return 0;
                var sorted = new List<long>(_samples);
                sorted.Sort();
                int mid = sorted.Count >> 1;
                return sorted.Count % 2 == 1
                    ? sorted[mid]
                    : (sorted[mid - 1] + sorted[mid]) / 2.0;
            }
        }

        /// <summary>
        /// Spread of the offset window — population standard deviation of the
        /// recent samples. A proxy for connection jitter: steady links sit near
        /// 0; bursty/variable links climb. Returns 0 with fewer than two samples.
        /// </summary>
        public double Jitter
        {
            get
            {
                int n = _samples.Count;
                if (n < 2) return 0;
                double sum = 0;
                foreach (var s in _samples) sum += s;
                double mean = sum / n;
                double variance = 0;
                foreach (var s in _samples)
                {
                    double d = s - mean;
                    variance += d * d;
                }
                variance /= n;
                return Math.Sqrt(variance);
            }
        }
    }
}
