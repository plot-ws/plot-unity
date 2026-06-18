#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using Plot.Interpolation;
using Plot.Prediction;

namespace Plot.Tests
{
    public class PredictionTests
    {
        private const double Eps = 1e-9;

        // ---- InputQueue ----

        [Test]
        public void InputQueueAcceptsMonotonicSeq()
        {
            var q = new InputQueue();
            Assert.IsFalse(q.Push(new QueueEntry(1, "a")));
            Assert.IsFalse(q.Push(new QueueEntry(2, "b")));
            Assert.AreEqual(2, q.Size);
        }

        [Test]
        public void InputQueueRejectsNonIncreasingSeq()
        {
            var q = new InputQueue();
            q.Push(new QueueEntry(5, "a"));
            Assert.Throws<System.InvalidOperationException>(() => q.Push(new QueueEntry(5, "b")));
            Assert.Throws<System.InvalidOperationException>(() => q.Push(new QueueEntry(3, "c")));
        }

        [Test]
        public void InputQueueAckUpToDrains()
        {
            var q = new InputQueue();
            for (int i = 1; i <= 5; i++) q.Push(new QueueEntry(i, i));
            q.AckUpTo(3);
            var pending = q.Pending();
            Assert.AreEqual(2, pending.Count);
            Assert.AreEqual(4, pending[0].Seq);
            Assert.AreEqual(5, pending[1].Seq);
        }

        [Test]
        public void InputQueueOverflowDropsOldestAndFlags()
        {
            var q = new InputQueue();
            bool overflowed = false;
            for (int i = 1; i <= 201; i++)
            {
                overflowed = q.Push(new QueueEntry(i, i));
            }
            Assert.IsTrue(overflowed, "201st push should overflow");
            Assert.AreEqual(200, q.Size);
            // Oldest (seq 1) dropped; head should now be seq 2.
            Assert.AreEqual(2, q.Pending()[0].Seq);
        }

        [Test]
        public void InputQueueClearEmpties()
        {
            var q = new InputQueue();
            q.Push(new QueueEntry(1, "a"));
            q.Clear();
            Assert.AreEqual(0, q.Size);
        }

        // ---- CorrectionTrack ----

        [Test]
        public void CorrectionTrackZeroWithoutRecord()
        {
            var track = new CorrectionTrack(InterpType.Number, 100);
            Assert.AreEqual(0.0, StateValue.AsNumber(track.Read(0)), Eps);
        }

        [Test]
        public void CorrectionTrackDecaysLinearly()
        {
            var track = new CorrectionTrack(InterpType.Number, 100);
            track.Record(10.0, 1000);
            // elapsed 0 → full value
            Assert.AreEqual(10.0, StateValue.AsNumber(track.Read(1000)), Eps);
            // elapsed 50 → k = 1 - 50/100 = 0.5 → 5
            Assert.AreEqual(5.0, StateValue.AsNumber(track.Read(1050)), Eps);
            // elapsed 100 → zero (>= duration)
            Assert.AreEqual(0.0, StateValue.AsNumber(track.Read(1100)), Eps);
        }

        [Test]
        public void CorrectionTrackHardClearsAfter1000ms()
        {
            var track = new CorrectionTrack(InterpType.Number, 100);
            track.Record(10.0, 0);
            Assert.AreEqual(0.0, StateValue.AsNumber(track.Read(1000)), Eps);
            // After hard-clear, even an in-window read returns zero.
            track.Record(8.0, 2000);
            Assert.AreEqual(8.0, StateValue.AsNumber(track.Read(2000)), Eps);
        }

        [Test]
        public void CorrectionTrackVec3Scales()
        {
            var track = new CorrectionTrack(InterpType.Vec3, 100);
            track.Record(StateValue.FromVec3(new Vec3(10, 20, 30)), 0);
            var v = StateValue.AsVec3(track.Read(50)); // k = 0.5
            Assert.AreEqual(5.0, v.X, Eps);
            Assert.AreEqual(10.0, v.Y, Eps);
            Assert.AreEqual(15.0, v.Z, Eps);
        }

        // ---- StateValue.JsonDiffMagnitude ----

        [Test]
        public void JsonDiffMagnitudeNumbers()
        {
            Assert.AreEqual(0.0, StateValue.JsonDiffMagnitude(5.0, 5.0), Eps);
            Assert.AreEqual(3.0, StateValue.JsonDiffMagnitude(2.0, 5.0), Eps);
        }

        [Test]
        public void JsonDiffMagnitudeNestedObjects()
        {
            var a = Map(("p", Map(("x", 1.0), ("y", 2.0))));
            var b = Map(("p", Map(("x", 4.0), ("y", 2.0))));
            // only x differs by 3
            Assert.AreEqual(3.0, StateValue.JsonDiffMagnitude(a, b), Eps);
        }

        [Test]
        public void JsonDiffMagnitudeTypeMismatch()
        {
            Assert.AreEqual(1.0, StateValue.JsonDiffMagnitude("a", 1.0), Eps);
            Assert.AreEqual(1.0, StateValue.JsonDiffMagnitude(null, Map(("x", 1.0))), Eps);
        }

        // ---- Predictor ----

        // Deterministic predict: input {dx} advances pos.x by dx.
        private static object? AdvanceX(object? state, object? input, Player p)
        {
            var s = (Dictionary<string, object?>)StateValue.DeepClone(state)!;
            var pos = (Dictionary<string, object?>)s["pos"]!;
            double dx = StateValue.AsNumber(((Dictionary<string, object?>)input!)["dx"]);
            pos["x"] = StateValue.AsNumber(pos["x"]) + dx;
            return s;
        }

        [Test]
        public void PredictorApplyAdvancesPredictedState()
        {
            var predictor = new Predictor(AdvanceX, new Player("p1", 0));
            predictor.SetAuthoritative(Map(("pos", Map(("x", 0.0)))));
            predictor.Apply(new QueueEntry(1, Map(("dx", 5.0))));
            predictor.Apply(new QueueEntry(2, Map(("dx", 3.0))));
            var pos = (Dictionary<string, object?>)((Dictionary<string, object?>)predictor.PredictedState!)["pos"]!;
            Assert.AreEqual(8.0, StateValue.AsNumber(pos["x"]), Eps);
        }

        [Test]
        public void PredictorDoesNotMutateAuthoritative()
        {
            var auth = Map(("pos", Map(("x", 0.0))));
            var predictor = new Predictor(AdvanceX, new Player("p1", 0));
            predictor.SetAuthoritative(auth);
            predictor.Apply(new QueueEntry(1, Map(("dx", 5.0))));
            // Original authoritative dict untouched.
            var pos = (Dictionary<string, object?>)auth["pos"]!;
            Assert.AreEqual(0.0, StateValue.AsNumber(pos["x"]), Eps);
        }

        [Test]
        public void PredictorReconcileReplaysPendingAndReportsZeroDriftWhenConsistent()
        {
            double reportedDrift = -1;
            var predictor = new Predictor(AdvanceX, new Player("p1", 0));
            predictor.OnReconcile += e => reportedDrift = e.Drift;
            predictor.SetAuthoritative(Map(("pos", Map(("x", 0.0)))));

            // Two local inputs applied optimistically: x = 8.
            predictor.Apply(new QueueEntry(1, Map(("dx", 5.0))));
            predictor.Apply(new QueueEntry(2, Map(("dx", 3.0))));

            // Server acks seq 1 only and reports x = 5 (it applied input 1).
            // Replay of pending (seq 2, dx 3) over server x=5 → 8, matching the
            // optimistic prediction, so drift is zero.
            predictor.Reconcile(Map(("pos", Map(("x", 5.0)))), 1);
            var pos = (Dictionary<string, object?>)((Dictionary<string, object?>)predictor.PredictedState!)["pos"]!;
            Assert.AreEqual(8.0, StateValue.AsNumber(pos["x"]), Eps);
            Assert.AreEqual(0.0, reportedDrift, Eps);
        }

        [Test]
        public void PredictorReconcileReportsDriftOnMisprediction()
        {
            double reportedDrift = -1;
            var predictor = new Predictor(AdvanceX, new Player("p1", 0));
            predictor.OnReconcile += e => reportedDrift = e.Drift;
            predictor.SetAuthoritative(Map(("pos", Map(("x", 0.0)))));

            predictor.Apply(new QueueEntry(1, Map(("dx", 5.0)))); // predicted x = 5
            // Server acked seq 1 but authoritative x = 2 (e.g. collision). No
            // pending inputs remain, so replayed = server = 2; drift = |5 - 2| = 3.
            predictor.Reconcile(Map(("pos", Map(("x", 2.0)))), 1);
            var pos = (Dictionary<string, object?>)((Dictionary<string, object?>)predictor.PredictedState!)["pos"]!;
            Assert.AreEqual(2.0, StateValue.AsNumber(pos["x"]), Eps);
            Assert.AreEqual(3.0, reportedDrift, Eps);
        }

        [Test]
        public void PredictorDisableResetsToAuthoritative()
        {
            var predictor = new Predictor(AdvanceX, new Player("p1", 0));
            predictor.SetAuthoritative(Map(("pos", Map(("x", 0.0)))));
            predictor.Apply(new QueueEntry(1, Map(("dx", 5.0))));
            predictor.Disable();
            Assert.IsTrue(predictor.Disabled);
            Assert.AreEqual(0, predictor.Queue.Size);
            var pos = (Dictionary<string, object?>)((Dictionary<string, object?>)predictor.PredictedState!)["pos"]!;
            Assert.AreEqual(0.0, StateValue.AsNumber(pos["x"]), Eps);
            // Apply after disable is a no-op.
            predictor.Apply(new QueueEntry(2, Map(("dx", 9.0))));
            pos = (Dictionary<string, object?>)((Dictionary<string, object?>)predictor.PredictedState!)["pos"]!;
            Assert.AreEqual(0.0, StateValue.AsNumber(pos["x"]), Eps);
        }

        private static Dictionary<string, object?> Map(params (string, object?)[] pairs)
        {
            var m = new Dictionary<string, object?>();
            foreach (var (k, v) in pairs) m[k] = v;
            return m;
        }
    }
}
