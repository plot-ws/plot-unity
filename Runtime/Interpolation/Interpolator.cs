#nullable enable
using System;
using System.Collections.Generic;

namespace Plot.Interpolation
{
    /// <summary>Supported interpolation value types.</summary>
    public enum InterpType
    {
        Number,
        Vec2,
        Vec3,
        Quat,
    }

    /// <summary>
    /// Resolves a path against the bracketing snapshot pair for a target time
    /// and lerps each leaf by type. Ports
    /// packages/client/src/interpolation/interpolator.ts.
    /// </summary>
    public class Interpolator
    {
        private readonly SnapshotBuffer _buffer;

        public string Path { get; }
        public InterpType Type { get; }
        public int RenderDelay { get; }

        public Interpolator(string path, InterpType type, SnapshotBuffer buffer, int renderDelay = 100)
        {
            Path = path;
            Type = type;
            _buffer = buffer;
            RenderDelay = renderDelay;
        }

        public Dictionary<string, object?> Tick(long targetTs)
        {
            var out_ = new Dictionary<string, object?>();
            var pair = _buffer.Lookup(targetTs);
            if (pair == null) return out_;
            var p = pair.Value;

            if (p.B == null)
            {
                // Single snapshot or clamped to one end — emit resolved A values.
                foreach (var leaf in PathResolver.Resolve(Path, p.A.State, p.A.State))
                {
                    if (!ReferenceEquals(leaf.ValueA, PathResolver.Missing))
                    {
                        out_[leaf.Path] = leaf.ValueA;
                    }
                }
                return out_;
            }

            var b = p.B.Value;
            double t = (double)(targetTs - p.A.Ts) / (b.Ts - p.A.Ts);
            double tClamped = Math.Max(0, Math.Min(1, t));

            foreach (var leaf in PathResolver.Resolve(Path, p.A.State, b.State))
            {
                bool aMissing = ReferenceEquals(leaf.ValueA, PathResolver.Missing);
                bool bMissing = ReferenceEquals(leaf.ValueB, PathResolver.Missing);
                if (aMissing && bMissing) continue;
                if (aMissing)
                {
                    out_[leaf.Path] = leaf.ValueB;
                    continue;
                }
                if (bMissing)
                {
                    out_[leaf.Path] = leaf.ValueA;
                    continue;
                }
                out_[leaf.Path] = LerpByType(Type, leaf.ValueA, leaf.ValueB, tClamped);
            }
            return out_;
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
}
