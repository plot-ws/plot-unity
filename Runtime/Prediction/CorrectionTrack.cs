#nullable enable
using Plot.Interpolation;

namespace Plot.Prediction
{
    /// <summary>
    /// Smooths a recorded prediction-vs-server drift to zero over a fixed
    /// duration so corrections are not applied as a hard snap. Ports
    /// packages/handler-client/src/correction-track.ts.
    ///
    /// The drift value and the value returned by <see cref="Read"/> are
    /// normalized state values whose type matches <see cref="CorrectionType"/>:
    /// a double for Number, or a { x, y[, z][, w] } map for the vector types.
    /// </summary>
    public class CorrectionTrack
    {
        private readonly InterpType _type;
        private readonly long _durationMs;
        private object? _value;
        private long _startedAt;
        private bool _hasRecord;

        public CorrectionTrack(InterpType type, long durationMs)
        {
            _type = type;
            _durationMs = durationMs;
            _value = Zero(type);
        }

        public void Record(object? drift, long now)
        {
            _value = StateValue.Normalize(drift);
            _startedAt = now;
            _hasRecord = true;
        }

        public object? Read(long now)
        {
            if (!_hasRecord) return Zero(_type);
            long elapsed = now - _startedAt;
            // Hard-clear well after the correction has fully decayed.
            if (elapsed >= 1000)
            {
                _hasRecord = false;
                return Zero(_type);
            }
            if (elapsed >= _durationMs) return Zero(_type);
            if (elapsed <= 0) return _value;
            double k = 1 - (double)elapsed / _durationMs;
            return Scale(_type, _value, k);
        }

        private static object Zero(InterpType type)
        {
            switch (type)
            {
                case InterpType.Number:
                    return 0.0;
                case InterpType.Vec2:
                    return StateValue.FromVec2(new Vec2(0, 0));
                case InterpType.Vec3:
                    return StateValue.FromVec3(new Vec3(0, 0, 0));
                case InterpType.Quat:
                    return StateValue.FromQuat(new Quat(0, 0, 0, 0));
                default:
                    return 0.0;
            }
        }

        private static object Scale(InterpType type, object? v, double k)
        {
            switch (type)
            {
                case InterpType.Number:
                    return StateValue.AsNumber(v) * k;
                case InterpType.Vec2:
                {
                    var a = StateValue.AsVec2(v);
                    return StateValue.FromVec2(new Vec2(a.X * k, a.Y * k));
                }
                case InterpType.Vec3:
                {
                    var a = StateValue.AsVec3(v);
                    return StateValue.FromVec3(new Vec3(a.X * k, a.Y * k, a.Z * k));
                }
                case InterpType.Quat:
                {
                    var a = StateValue.AsQuat(v);
                    return StateValue.FromQuat(new Quat(a.X * k, a.Y * k, a.Z * k, a.W * k));
                }
                default:
                    return 0.0;
            }
        }
    }
}
