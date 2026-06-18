#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using Plot.Interpolation;

namespace Plot.Tests
{
    public class InterpolationTests
    {
        private const double Eps = 1e-9;

        // ---- Lerp ----

        [Test]
        public void LerpNumberMidpoint()
        {
            Assert.AreEqual(5.0, Lerp.LerpNumber(0, 10, 0.5), Eps);
            Assert.AreEqual(0.0, Lerp.LerpNumber(0, 10, 0), Eps);
            Assert.AreEqual(10.0, Lerp.LerpNumber(0, 10, 1), Eps);
        }

        [Test]
        public void LerpVec2And3Components()
        {
            var v2 = Lerp.LerpVec2(new Vec2(0, 0), new Vec2(10, 20), 0.5);
            Assert.AreEqual(5.0, v2.X, Eps);
            Assert.AreEqual(10.0, v2.Y, Eps);

            var v3 = Lerp.LerpVec3(new Vec3(0, 0, 0), new Vec3(10, 20, 30), 0.25);
            Assert.AreEqual(2.5, v3.X, Eps);
            Assert.AreEqual(5.0, v3.Y, Eps);
            Assert.AreEqual(7.5, v3.Z, Eps);
        }

        [Test]
        public void LerpQuatEndpointsExact()
        {
            var a = new Quat(0, 0, 0, 1);
            var b = new Quat(0, 0, 1, 0);
            var at0 = Lerp.LerpQuat(a, b, 0);
            Assert.AreEqual(a.X, at0.X, Eps);
            Assert.AreEqual(a.W, at0.W, Eps);
            var at1 = Lerp.LerpQuat(a, b, 1);
            Assert.AreEqual(b.Z, at1.Z, Eps);
            Assert.AreEqual(b.W, at1.W, Eps);
        }

        [Test]
        public void LerpQuatStaysUnitLength()
        {
            var a = new Quat(0, 0, 0, 1);
            var b = new Quat(0, 0, 0.7071067811865476, 0.7071067811865476);
            var mid = Lerp.LerpQuat(a, b, 0.5);
            double len = System.Math.Sqrt(mid.X * mid.X + mid.Y * mid.Y + mid.Z * mid.Z + mid.W * mid.W);
            Assert.AreEqual(1.0, len, 1e-6);
        }

        [Test]
        public void LerpQuatTakesShortPath()
        {
            // b is the negation of a: short path should converge toward -a's
            // representation, never blow up. Dot is negative → flips b.
            var a = new Quat(0, 0, 0, 1);
            var b = new Quat(0, 0, 0, -1);
            var mid = Lerp.LerpQuat(a, b, 0.5);
            double len = System.Math.Sqrt(mid.X * mid.X + mid.Y * mid.Y + mid.Z * mid.Z + mid.W * mid.W);
            Assert.AreEqual(1.0, len, 1e-6);
        }

        // ---- SnapshotBuffer ----

        [Test]
        public void SnapshotBufferBracketsTarget()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(100, Map(("v", 0.0)));
            buf.Push(200, Map(("v", 10.0)));
            buf.Push(300, Map(("v", 20.0)));
            var pair = buf.Lookup(250);
            Assert.IsNotNull(pair);
            Assert.AreEqual(200, pair!.Value.A.Ts);
            Assert.IsNotNull(pair.Value.B);
            Assert.AreEqual(300, pair.Value.B!.Value.Ts);
        }

        [Test]
        public void SnapshotBufferClampsBeforeFirstAndAfterLast()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(100, Map(("v", 0.0)));
            buf.Push(200, Map(("v", 10.0)));

            var before = buf.Lookup(50);
            Assert.AreEqual(100, before!.Value.A.Ts);
            Assert.IsNull(before.Value.B);

            var after = buf.Lookup(500);
            Assert.AreEqual(200, after!.Value.A.Ts);
            Assert.IsNull(after.Value.B);
        }

        [Test]
        public void SnapshotBufferRejectsNonMonotonic()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(200, Map(("v", 1.0)));
            buf.Push(100, Map(("v", 2.0))); // older — rejected
            buf.Push(200, Map(("v", 3.0))); // equal — rejected
            Assert.AreEqual(1, buf.Size);
        }

        [Test]
        public void SnapshotBufferEvictsBeyondHorizon()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(0, Map(("v", 0.0)));
            buf.Push(100, Map(("v", 1.0)));
            // Pushing at 700 makes cutoff = 200; snapshots[1] (ts 100) < 200, so
            // head (ts 0) is dropped. snapshots[1] is now ts 700 (the new tail)
            // after one shift, so eviction stops.
            buf.Push(700, Map(("v", 2.0)));
            Assert.AreEqual(700, buf.Newest!.Value.Ts);
            Assert.AreEqual(100, buf.Oldest!.Value.Ts);
        }

        [Test]
        public void SnapshotBufferEmptyLookupNull()
        {
            var buf = new SnapshotBuffer(500);
            Assert.IsNull(buf.Lookup(0));
        }

        // ---- ServerClock ----

        [Test]
        public void ServerClockOffsetIsMedian()
        {
            var clock = new ServerClock();
            // offsets: 10, 12, 100 → median 12 (odd count)
            clock.Observe(110, 100);
            clock.Observe(212, 200);
            clock.Observe(400, 300);
            Assert.AreEqual(12.0, clock.Offset, Eps);
        }

        [Test]
        public void ServerClockOffsetEvenCountAverages()
        {
            var clock = new ServerClock();
            clock.Observe(110, 100); // 10
            clock.Observe(220, 200); // 20
            Assert.AreEqual(15.0, clock.Offset, Eps);
        }

        [Test]
        public void ServerClockJitterIsPopulationStdDev()
        {
            var clock = new ServerClock();
            clock.Observe(110, 100); // 10
            clock.Observe(220, 200); // 20
            // mean 15, variance = ((10-15)^2 + (20-15)^2)/2 = 25, std = 5
            Assert.AreEqual(5.0, clock.Jitter, Eps);
        }

        [Test]
        public void ServerClockJitterZeroBelowTwoSamples()
        {
            var clock = new ServerClock();
            Assert.AreEqual(0.0, clock.Jitter, Eps);
            clock.Observe(110, 100);
            Assert.AreEqual(0.0, clock.Jitter, Eps);
        }

        [Test]
        public void ServerClockWindowCapsAtEight()
        {
            var clock = new ServerClock();
            for (int i = 0; i < 12; i++) clock.Observe(1000 + i, 1000); // offsets 0..11
            // Only last 8 retained: offsets 4..11, median of {4..11} = (7+8)/2 = 7.5
            Assert.AreEqual(7.5, clock.Offset, Eps);
        }

        // ---- PathResolver ----

        [Test]
        public void PathResolverPlainPath()
        {
            var a = Map(("player", Map(("x", 1.0))));
            var b = Map(("player", Map(("x", 2.0))));
            var leaves = PathResolver.Resolve("player.x", a, b);
            Assert.AreEqual(1, leaves.Count);
            Assert.AreEqual("player.x", leaves[0].Path);
            Assert.AreEqual(1.0, leaves[0].ValueA);
            Assert.AreEqual(2.0, leaves[0].ValueB);
        }

        [Test]
        public void PathResolverMissingSegmentYieldsMissing()
        {
            var a = Map(("player", Map(("x", 1.0))));
            var leaves = PathResolver.Resolve("player.y", a, a);
            Assert.AreEqual(1, leaves.Count);
            Assert.IsTrue(ReferenceEquals(leaves[0].ValueA, PathResolver.Missing));
        }

        [Test]
        public void PathResolverWildcardExpandsKeysSorted()
        {
            var a = Map(
                ("players", Map(
                    ("b", Map(("hp", 5.0))),
                    ("a", Map(("hp", 1.0))))));
            var b = Map(
                ("players", Map(
                    ("a", Map(("hp", 2.0))),
                    ("b", Map(("hp", 6.0))))));
            var leaves = PathResolver.Resolve("players.*.hp", a, b);
            Assert.AreEqual(2, leaves.Count);
            // Sorted ordinal: a before b
            Assert.AreEqual("players.a.hp", leaves[0].Path);
            Assert.AreEqual(1.0, leaves[0].ValueA);
            Assert.AreEqual(2.0, leaves[0].ValueB);
            Assert.AreEqual("players.b.hp", leaves[1].Path);
            Assert.AreEqual(5.0, leaves[1].ValueA);
            Assert.AreEqual(6.0, leaves[1].ValueB);
        }

        [Test]
        public void PathResolverRejectsMultiWildcard()
        {
            Assert.Throws<System.ArgumentException>(
                () => PathResolver.Resolve("a.*.b.*", Map(), Map()));
        }

        // ---- Interpolator ----

        [Test]
        public void InterpolatorLerpsNumberBetweenPair()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(0, Map(("hp", 0.0)));
            buf.Push(100, Map(("hp", 100.0)));
            var interp = new Interpolator("hp", InterpType.Number, buf);
            var outv = interp.Tick(50);
            Assert.AreEqual(50.0, (double)outv["hp"]!, Eps);
        }

        [Test]
        public void InterpolatorClampedSingleSideEmitsA()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(0, Map(("hp", 42.0)));
            var interp = new Interpolator("hp", InterpType.Number, buf);
            var outv = interp.Tick(999);
            Assert.AreEqual(42.0, (double)outv["hp"]!, Eps);
        }

        [Test]
        public void InterpolatorLerpsVec3()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(0, Map(("pos", Map(("x", 0.0), ("y", 0.0), ("z", 0.0)))));
            buf.Push(100, Map(("pos", Map(("x", 10.0), ("y", 20.0), ("z", 30.0)))));
            var interp = new Interpolator("pos", InterpType.Vec3, buf);
            var outv = interp.Tick(50);
            var pos = (Dictionary<string, object?>)outv["pos"]!;
            Assert.AreEqual(5.0, (double)pos["x"]!, Eps);
            Assert.AreEqual(10.0, (double)pos["y"]!, Eps);
            Assert.AreEqual(15.0, (double)pos["z"]!, Eps);
        }

        [Test]
        public void JsonPatchAppliesReplaceAddRemove()
        {
            var doc = Map(("hp", 10.0), ("name", "a"));
            var patch = new List<object?>
            {
                Map(("op", "replace"), ("path", "/hp"), ("value", 20.0)),
                Map(("op", "add"), ("path", "/mp"), ("value", 5.0)),
                Map(("op", "remove"), ("path", "/name")),
            };
            var result = (Dictionary<string, object?>)JsonPatch.Apply(doc, patch)!;
            Assert.AreEqual(20.0, (double)result["hp"]!, Eps);
            Assert.AreEqual(5.0, (double)result["mp"]!, Eps);
            Assert.IsFalse(result.ContainsKey("name"));
        }

        private static Dictionary<string, object?> Map(params (string, object?)[] pairs)
        {
            var m = new Dictionary<string, object?>();
            foreach (var (k, v) in pairs) m[k] = v;
            return m;
        }
    }
}
