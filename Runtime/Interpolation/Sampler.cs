#nullable enable
using System;
using System.Collections.Generic;

namespace Plot.Interpolation
{
    /// <summary>
    /// Point sampling of interpolated state at an arbitrary past server
    /// timestamp — the client-side analogue of the server's
    /// <c>ctx.rewindTo</c>. Shares the same SnapshotBuffer + lerp + wildcard
    /// path resolver the live frame loop uses, but is a pure read: it does not
    /// touch the frame loop or any prediction/correction state. Ports
    /// packages/client/src/interpolation/sampler.ts.
    /// </summary>
    public static class Sampler
    {
        /// <summary>
        /// Sample the interpolated value at a single path at the given server
        /// timestamp. Returns the interpolated leaf value for a plain path, a
        /// <c>Dictionary&lt;string, object?&gt;</c> keyed by resolved path for a
        /// <c>*</c> wildcard, or <c>null</c> when the buffer is empty or
        /// <paramref name="atServerTs"/> falls outside the retained horizon
        /// <c>[Oldest.Ts, Newest.Ts]</c>.
        /// </summary>
        public static object? SampleAt(
            SnapshotBuffer buffer,
            string path,
            InterpType type,
            long atServerTs)
        {
            var oldest = buffer.Oldest;
            var newest = buffer.Newest;
            if (oldest == null || newest == null) return null;
            // Outside the retained horizon — the live frame loop would clamp
            // here, but a deliberate point sample must not report a clamped
            // endpoint as if it were the value at the requested time.
            if (atServerTs < oldest.Value.Ts || atServerTs > newest.Value.Ts) return null;

            bool isWildcard = path.IndexOf('*') >= 0;
            var pair = buffer.Lookup(atServerTs);
            if (pair == null) return isWildcard ? new Dictionary<string, object?>() : null;
            var p = pair.Value;

            var outMap = new Dictionary<string, object?>();
            if (p.B == null)
            {
                // Single snapshot or exactly on an endpoint — emit A values.
                foreach (var leaf in PathResolver.Resolve(path, p.A.State, p.A.State))
                {
                    if (!ReferenceEquals(leaf.ValueA, PathResolver.Missing))
                    {
                        outMap[leaf.Path] = leaf.ValueA;
                    }
                }
                return Collapse(outMap, path, isWildcard);
            }

            var b = p.B.Value;
            double t = (double)(atServerTs - p.A.Ts) / (b.Ts - p.A.Ts);
            double tClamped = Math.Max(0, Math.Min(1, t));
            foreach (var leaf in PathResolver.Resolve(path, p.A.State, b.State))
            {
                bool aMissing = ReferenceEquals(leaf.ValueA, PathResolver.Missing);
                bool bMissing = ReferenceEquals(leaf.ValueB, PathResolver.Missing);
                if (aMissing && bMissing) continue;
                if (aMissing)
                {
                    outMap[leaf.Path] = leaf.ValueB;
                    continue;
                }
                if (bMissing)
                {
                    outMap[leaf.Path] = leaf.ValueA;
                    continue;
                }
                outMap[leaf.Path] = LerpByType(type, leaf.ValueA, leaf.ValueB, tClamped);
            }
            return Collapse(outMap, path, isWildcard);
        }

        private static object? Collapse(
            Dictionary<string, object?> outMap, string path, bool isWildcard)
        {
            if (isWildcard) return outMap;
            return outMap.TryGetValue(path, out var v) ? v : null;
        }

        private static object LerpByType(InterpType type, object? a, object? b, double t)
        {
            switch (type)
            {
                case InterpType.Number:
                    return Lerp.LerpNumber(StateValue.AsNumber(a), StateValue.AsNumber(b), t);
                case InterpType.Vec2:
                    return StateValue.FromVec2(
                        Lerp.LerpVec2(StateValue.AsVec2(a), StateValue.AsVec2(b), t));
                case InterpType.Vec3:
                    return StateValue.FromVec3(
                        Lerp.LerpVec3(StateValue.AsVec3(a), StateValue.AsVec3(b), t));
                case InterpType.Quat:
                    return StateValue.FromQuat(
                        Lerp.LerpQuat(StateValue.AsQuat(a), StateValue.AsQuat(b), t));
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }

    /// <summary>
    /// A lightweight handle bound to a fixed past server timestamp. Lets
    /// callers read several paths at one frozen time without repeating the
    /// timestamp (e.g. hit detection across multiple entities at a shot's
    /// server time). Thin wrapper over <see cref="Sampler.SampleAt"/>.
    /// </summary>
    public class Rewind
    {
        private readonly SnapshotBuffer _buffer;

        /// <summary>The server timestamp this handle samples at.</summary>
        public long AtServerTs { get; }

        public Rewind(SnapshotBuffer buffer, long atServerTs)
        {
            _buffer = buffer;
            AtServerTs = atServerTs;
        }

        /// <summary>Sample one path at this handle's bound timestamp.</summary>
        public object? Sample(string path, InterpType type)
        {
            return Sampler.SampleAt(_buffer, path, type, AtServerTs);
        }
    }
}
