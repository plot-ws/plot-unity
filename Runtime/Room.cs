#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Plot.Interpolation;
using Plot.Prediction;

namespace Plot
{
    public class MessageEvent
    {
        public string From = "";
        public object? Data;
    }

    public class PresenceEvent
    {
        public string PlayerId = "";
        public string[] Players = Array.Empty<string>();
    }

    /// <summary>Emitted each frame with the latest interpolated leaf values.</summary>
    public class FrameEvent
    {
        public Dictionary<string, object?> Interpolated = new Dictionary<string, object?>();
        public long Ts;
    }

    /// <summary>Emitted after a predicted input or a server reconcile.</summary>
    public class PredictedEvent
    {
        public object? State;
        public long Ts;
        public double Drift;
    }

    public class Room
    {
        public event Action<MessageEvent>? OnMessage;
        public event Action<PresenceEvent>? OnPlayerJoined;
        public event Action<PresenceEvent>? OnPlayerLeft;
        public event Action<FrameEvent>? OnFrame;
        public event Action<PredictedEvent>? OnPredicted;

        private readonly Transport _transport;
        private readonly string _playerId;

        /// <summary>Latest authoritative state (normalized object tree).</summary>
        public object? CurrentState;

        // Interpolation
        private readonly SnapshotBuffer _buffer = new SnapshotBuffer(500);
        private readonly ServerClock _clock = new ServerClock();
        private readonly List<Interpolator> _interpolators = new List<Interpolator>();
        private Timer? _frameTimer;
        private bool _adaptiveEnabled;
        private double _adaptiveGain = 1.5;
        private double _adaptiveMaxExtraMs = 200;

        // Prediction
        private Predictor? _predictor;
        private long _nextSeq;
        private long _lastAckedSeq;
        private double _lastDrift;

        private class PredictedTrack
        {
            public string Path = "";
            public InterpType Type;
            public CorrectionTrack Track = null!;
            public object? PreviousValue;
        }

        private readonly List<PredictedTrack> _predictedTracks = new List<PredictedTrack>();

        /// <summary>
        /// Per-path corrected state: the predicted value with the smoothed
        /// correction offset applied. Keyed by registered predict path.
        /// </summary>
        public Dictionary<string, object?> CorrectedState { get; } = new Dictionary<string, object?>();

        public Room(string playerId, Transport transport)
        {
            _playerId = playerId;
            _transport = transport;
            _transport.OnMessage += HandleIncoming;
        }

        public void Send(object data, string channel = "event")
        {
            var env = new Dictionary<string, object?>
            {
                ["type"] = "message",
                ["channel"] = channel,
                ["data"] = data,
                ["clientTs"] = NowMs(),
            };
            _transport.Send(JsonSerializer.Serialize(env));
        }

        public void Leave()
        {
            StopFrameLoop();
            _transport.Close();
        }

        // ---- Interpolation API ----

        public void Interpolate(string path, string type, int renderDelay = 100)
        {
            foreach (var existing in _interpolators)
            {
                if (existing.Path == path)
                {
                    throw new InvalidOperationException($"Room.Interpolate: path already registered: {path}");
                }
            }
            _interpolators.Add(new Interpolator(path, ParseType(type), _buffer, renderDelay));
        }

        /// <summary>
        /// Sample the interpolated value at a single path at an arbitrary past
        /// server timestamp — the rendering-side analogue of the server's
        /// <c>ctx.rewindTo</c>. Reuses the same SnapshotBuffer + lerp + wildcard
        /// path resolver the live frame loop uses, but is a pure read: it does
        /// not touch the frame loop or any prediction/correction state.
        ///
        /// <paramref name="atServerTs"/> is in the server time domain (same as
        /// snapshot timestamps); convert a client time with
        /// <c>clientNow - ServerClockOffset</c>. Returns the interpolated leaf
        /// value for a plain path, a <c>Dictionary&lt;string, object?&gt;</c>
        /// keyed by resolved path for a <c>*</c> wildcard, or <c>null</c> when
        /// the timestamp falls outside the buffer's retained horizon.
        /// </summary>
        public object? SampleAt(string path, string type, long atServerTs)
        {
            return Sampler.SampleAt(_buffer, path, ParseType(type), atServerTs);
        }

        /// <summary>
        /// Bind a <see cref="Rewind"/> handle to a fixed past server timestamp
        /// so callers can read several paths at one frozen time ergonomically.
        /// Thin wrapper over the same buffer lookup + lerp as
        /// <see cref="SampleAt"/>.
        /// </summary>
        public Rewind RewindTo(long atServerTs)
        {
            return new Rewind(_buffer, atServerTs);
        }

        /// <summary>Current median client→server clock offset (clientNow − serverTs).</summary>
        public double ServerClockOffset => _clock.Offset;

        /// <summary>
        /// Adaptive smoothing: when enabled, the render delay grows with measured
        /// connection jitter so a bursty link buffers more and a steady link
        /// stays responsive. Effective extra delay =
        /// clamp(gain * ServerClock.Jitter, 0, maxExtraMs), added on top of each
        /// interpolator's base renderDelay.
        /// </summary>
        public void SetAdaptiveSmoothing(bool enabled, double gain = 1.0, double maxExtraMs = 100.0)
        {
            _adaptiveEnabled = enabled;
            _adaptiveGain = gain;
            _adaptiveMaxExtraMs = maxExtraMs;
        }

        /// <summary>Current adaptive extra delay (ms) given the live jitter estimate.</summary>
        public double AdaptiveExtraDelay()
        {
            if (!_adaptiveEnabled) return 0;
            double raw = _adaptiveGain * _clock.Jitter;
            return Math.Max(0, Math.Min(_adaptiveMaxExtraMs, raw));
        }

        /// <summary>
        /// Advance interpolation/correction by one frame. <paramref name="now"/>
        /// must be in the same wall-clock domain as the server state timestamps
        /// (milliseconds since the Unix epoch). Pass -1 to use the current time.
        /// Call from MonoBehaviour.Update or rely on <see cref="StartFrameLoop"/>.
        /// </summary>
        public void TickFrame(long now = -1)
        {
            if (now < 0) now = NowMs();

            if (OnFrame != null && _interpolators.Count > 0)
            {
                double offset = _clock.Offset;
                double minDelay = double.PositiveInfinity;
                foreach (var i in _interpolators)
                {
                    if (i.RenderDelay < minDelay) minDelay = i.RenderDelay;
                }
                long target = (long)Math.Round(now - offset - minDelay - AdaptiveExtraDelay());
                var interpolated = new Dictionary<string, object?>();
                foreach (var i in _interpolators)
                {
                    foreach (var kv in i.Tick(target)) interpolated[kv.Key] = kv.Value;
                }
                OnFrame.Invoke(new FrameEvent { Interpolated = interpolated, Ts = target });
            }

            if (_predictor != null)
            {
                foreach (var t in _predictedTracks)
                {
                    object? baseVal = ReadPath(_predictor.PredictedState, t.Path);
                    object? offset = t.Track.Read(now);
                    CorrectedState[t.Path] = ApplyOffset(t.Type, baseVal, offset);
                }
            }
        }

        public void StartFrameLoop(int intervalMs = 16)
        {
            StopFrameLoop();
            _frameTimer = new Timer(_ => TickFrame(), null, intervalMs, intervalMs);
        }

        public void StopFrameLoop()
        {
            if (_frameTimer != null)
            {
                _frameTimer.Dispose();
                _frameTimer = null;
            }
        }

        // ---- Prediction API ----

        /// <summary>
        /// Attach a deterministic predict function for client-side prediction.
        /// Engine deviation from the TS attachHandler: engines cannot run the
        /// handler module, so the caller supplies (state, input, player) →
        /// nextState. The function is handed a deep clone and must not mutate it.
        /// </summary>
        public void AttachPrediction(object? initialState, Func<object?, object?, Player, object?> predictFn)
        {
            if (_predictor != null)
            {
                throw new InvalidOperationException("Room.AttachPrediction: prediction already attached");
            }
            var predictor = new Predictor(predictFn, new Player(_playerId, NowMs()));
            predictor.OnReconcile += e => { _lastDrift = e.Drift; };
            object? seed = CurrentState ?? initialState;
            predictor.SetAuthoritative(seed);
            _predictor = predictor;
        }

        public object? PredictedState => _predictor?.PredictedState;

        public void SendPredicted(object? input, string channel = "event")
        {
            var predictor = _predictor;
            if (predictor == null)
            {
                throw new InvalidOperationException("Room.SendPredicted: AttachPrediction must be called first");
            }
            long seq = ++_nextSeq;
            predictor.Apply(new QueueEntry(seq, StateValue.Normalize(input)));
            foreach (var t in _predictedTracks)
            {
                CorrectedState[t.Path] = ReadPath(predictor.PredictedState, t.Path);
            }
            var env = new Dictionary<string, object?>
            {
                ["type"] = "message",
                ["channel"] = channel,
                ["data"] = input,
                ["_seq"] = seq,
                ["clientTs"] = NowMs(),
            };
            _transport.Send(JsonSerializer.Serialize(env));
            OnPredicted?.Invoke(new PredictedEvent
            {
                State = predictor.PredictedState,
                Ts = NowMs(),
                Drift = 0,
            });
        }

        public void Predict(string path, string type, int correctionMs = 100)
        {
            if (_predictor == null)
            {
                throw new InvalidOperationException("Room.Predict: AttachPrediction must be called first");
            }
            foreach (var t in _predictedTracks)
            {
                if (t.Path == path)
                {
                    throw new InvalidOperationException($"Room.Predict: path already registered: {path}");
                }
            }
            var interpType = ParseType(type);
            _predictedTracks.Add(new PredictedTrack
            {
                Path = path,
                Type = interpType,
                Track = new CorrectionTrack(interpType, correctionMs),
                PreviousValue = ReadPath(_predictor.PredictedState, path),
            });
            CorrectedState[path] = ReadPath(_predictor.PredictedState, path);
        }

        // ---- Inbound dispatch ----

        private void HandleIncoming(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var t = root.GetProperty("type").GetString();
            switch (t)
            {
                case "message":
                    OnMessage?.Invoke(new MessageEvent
                    {
                        From = root.TryGetProperty("from", out var fromEl) ? fromEl.GetString() ?? "" : "",
                        Data = root.TryGetProperty("data", out var dataEl)
                            ? StateValue.Normalize(dataEl.Clone())
                            : null,
                    });
                    break;
                case "join":
                    OnPlayerJoined?.Invoke(new PresenceEvent
                    {
                        PlayerId = root.TryGetProperty("playerId", out var jp) ? jp.GetString() ?? "" : "",
                        Players = ReadPlayers(root),
                    });
                    break;
                case "leave":
                    OnPlayerLeft?.Invoke(new PresenceEvent
                    {
                        PlayerId = root.TryGetProperty("playerId", out var lp) ? lp.GetString() ?? "" : "",
                        Players = ReadPlayers(root),
                    });
                    break;
                case "state-snapshot":
                {
                    if (root.TryGetProperty("lastAckedSeq", out var ls) && ls.ValueKind == JsonValueKind.Number)
                    {
                        _lastAckedSeq = ls.GetInt64();
                    }
                    long ts = root.TryGetProperty("ts", out var tsEl) ? tsEl.GetInt64() : NowMs();
                    CurrentState = root.TryGetProperty("state", out var stEl)
                        ? StateValue.Normalize(stEl.Clone())
                        : null;
                    OnSnapshot(ts, CurrentState);
                    break;
                }
                case "state-patch":
                {
                    if (root.TryGetProperty("lastAckedSeq", out var ls) && ls.ValueKind == JsonValueKind.Number)
                    {
                        _lastAckedSeq = ls.GetInt64();
                    }
                    long ts = root.TryGetProperty("ts", out var tsEl) ? tsEl.GetInt64() : NowMs();
                    object? patch = root.TryGetProperty("patch", out var pEl)
                        ? StateValue.Normalize(pEl.Clone())
                        : null;
                    object? baseDoc = StateValue.DeepClone(CurrentState) ?? new Dictionary<string, object?>();
                    CurrentState = JsonPatch.Apply(baseDoc, patch);
                    OnSnapshot(ts, CurrentState);
                    break;
                }
            }
        }

        // Mirrors the TS snapshot sink: feed clock + buffer, then reconcile and
        // emit predicted on every applied (ts, state).
        private void OnSnapshot(long ts, object? state)
        {
            _clock.Observe(NowMs(), ts);
            _buffer.Push(ts, state);
            if (_predictor != null)
            {
                foreach (var t in _predictedTracks)
                {
                    t.PreviousValue = ReadPath(_predictor.PredictedState, t.Path);
                }
                _predictor.Reconcile(state, _lastAckedSeq);
                long now = NowMs();
                foreach (var t in _predictedTracks)
                {
                    object? newValue = ReadPath(_predictor.PredictedState, t.Path);
                    object? drift = ComputeDrift(t.Type, t.PreviousValue, newValue);
                    if (drift != null) t.Track.Record(drift, now);
                }
                OnPredicted?.Invoke(new PredictedEvent
                {
                    State = _predictor.PredictedState,
                    Ts = ts,
                    Drift = _lastDrift,
                });
            }
        }

        // ---- helpers ----

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string[] ReadPlayers(JsonElement root)
        {
            if (!root.TryGetProperty("players", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>(arr.GetArrayLength());
            foreach (var el in arr.EnumerateArray()) list.Add(el.GetString() ?? "");
            return list.ToArray();
        }

        private static InterpType ParseType(string type)
        {
            switch (type)
            {
                case "number": return InterpType.Number;
                case "vec2": return InterpType.Vec2;
                case "vec3": return InterpType.Vec3;
                case "quat": return InterpType.Quat;
                default: throw new ArgumentException($"unknown interpolation type: {type}");
            }
        }

        private static object? ReadPath(object? state, string path)
        {
            object? cur = state;
            foreach (var seg in path.Split('.'))
            {
                if (cur is Dictionary<string, object?> map)
                {
                    cur = map.TryGetValue(seg, out var v) ? v : null;
                }
                else
                {
                    return null;
                }
            }
            return cur;
        }

        private static object? ComputeDrift(InterpType type, object? prev, object? next)
        {
            if (prev == null || next == null) return null;
            switch (type)
            {
                case InterpType.Number:
                    return StateValue.AsNumber(prev) - StateValue.AsNumber(next);
                case InterpType.Vec2:
                {
                    var a = StateValue.AsVec2(prev);
                    var b = StateValue.AsVec2(next);
                    return StateValue.FromVec2(new Vec2(a.X - b.X, a.Y - b.Y));
                }
                case InterpType.Vec3:
                {
                    var a = StateValue.AsVec3(prev);
                    var b = StateValue.AsVec3(next);
                    return StateValue.FromVec3(new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z));
                }
                case InterpType.Quat:
                {
                    var a = StateValue.AsQuat(prev);
                    var b = StateValue.AsQuat(next);
                    return StateValue.FromQuat(new Quat(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W));
                }
                default:
                    return null;
            }
        }

        private static object? ApplyOffset(InterpType type, object? baseVal, object? offset)
        {
            if (baseVal == null) return null;
            switch (type)
            {
                case InterpType.Number:
                    return StateValue.AsNumber(baseVal) + StateValue.AsNumber(offset);
                case InterpType.Vec2:
                {
                    var a = StateValue.AsVec2(baseVal);
                    var b = StateValue.AsVec2(offset);
                    return StateValue.FromVec2(new Vec2(a.X + b.X, a.Y + b.Y));
                }
                case InterpType.Vec3:
                {
                    var a = StateValue.AsVec3(baseVal);
                    var b = StateValue.AsVec3(offset);
                    return StateValue.FromVec3(new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z));
                }
                case InterpType.Quat:
                {
                    var a = StateValue.AsQuat(baseVal);
                    var b = StateValue.AsQuat(offset);
                    return StateValue.FromQuat(new Quat(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W));
                }
                default:
                    return baseVal;
            }
        }
    }
}
