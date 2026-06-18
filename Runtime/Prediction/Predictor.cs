#nullable enable
using System;
using Plot.Interpolation;

namespace Plot.Prediction
{
    /// <summary>Raised after a reconcile pass with the measured replay drift.</summary>
    public struct ReconcileEvent
    {
        public double Drift;

        public ReconcileEvent(double drift)
        {
            Drift = drift;
        }
    }

    /// <summary>
    /// Client-side prediction core: applies local inputs immediately, then
    /// reconciles against authoritative server state by replaying still-pending
    /// inputs. Ports packages/handler-client/src/predictor.ts.
    ///
    /// Engine deviation: the TS reference replays via the handler module
    /// (runHandler). Game engines cannot run that module, so the caller supplies
    /// a deterministic predict function (state, input, player) → nextState. The
    /// function MUST NOT mutate the state it receives; it is handed a deep clone.
    /// </summary>
    public class Predictor
    {
        private readonly Func<object?, object?, Player, object?> _predict;
        private readonly Player _localPlayer;
        private readonly InputQueue _queue = new InputQueue();
        private object? _authoritative;
        private object? _predicted;
        private bool _disabled;

        public event Action<ReconcileEvent>? OnReconcile;

        public Predictor(Func<object?, object?, Player, object?> predict, Player localPlayer)
        {
            _predict = predict ?? throw new ArgumentNullException(nameof(predict));
            _localPlayer = localPlayer;
            _authoritative = new System.Collections.Generic.Dictionary<string, object?>();
            _predicted = new System.Collections.Generic.Dictionary<string, object?>();
        }

        public void SetAuthoritative(object? state)
        {
            var norm = StateValue.Normalize(state);
            _authoritative = norm;
            _predicted = StateValue.DeepClone(norm);
        }

        public object? PredictedState => _predicted;

        public InputQueue Queue => _queue;

        public bool Disabled => _disabled;

        public void Apply(QueueEntry entry)
        {
            if (_disabled) return;
            object? next;
            try
            {
                next = _predict(StateValue.DeepClone(_predicted), entry.Input, _localPlayer);
            }
            catch (Exception)
            {
                // Handler threw on apply; drop this input.
                return;
            }
            _predicted = StateValue.Normalize(next);
            bool overflowed = _queue.Push(entry);
            if (overflowed && _queue.Size >= 200)
            {
                // Input queue overflowed; disable prediction.
                _disabled = true;
                _queue.Clear();
                _predicted = StateValue.DeepClone(_authoritative);
            }
        }

        public void Reconcile(object? serverState, long lastAckedSeq)
        {
            var norm = StateValue.Normalize(serverState);
            _authoritative = norm;
            _queue.AckUpTo(lastAckedSeq);
            object? replayed = StateValue.DeepClone(norm);
            try
            {
                foreach (var entry in _queue.Pending())
                {
                    replayed = StateValue.Normalize(
                        _predict(StateValue.DeepClone(replayed), entry.Input, _localPlayer));
                }
            }
            catch (Exception)
            {
                // Replay threw; clear queue and fall back to server state.
                _queue.Clear();
                replayed = StateValue.DeepClone(norm);
            }
            double drift = StateValue.JsonDiffMagnitude(_predicted, replayed);
            _predicted = replayed;
            OnReconcile?.Invoke(new ReconcileEvent(drift));
        }

        public void Disable()
        {
            _disabled = true;
            _queue.Clear();
            _predicted = StateValue.DeepClone(_authoritative);
        }
    }
}
