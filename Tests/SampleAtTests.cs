#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using Plot.Interpolation;

namespace Plot.Tests
{
    public class SampleAtTests
    {
        private const double Eps = 1e-9;

        private static Dictionary<string, object?> Map(params (string, object?)[] pairs)
        {
            var m = new Dictionary<string, object?>();
            foreach (var (k, v) in pairs) m[k] = v;
            return m;
        }

        private static Dictionary<string, object?> Vec2(double x, double y) =>
            Map(("x", x), ("y", y));

        private static SnapshotBuffer Vec2Buffer()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(100, Map(("positions", Map(("p1", Vec2(0, 0)), ("p2", Vec2(100, 0))))));
            buf.Push(200, Map(("positions", Map(("p1", Vec2(10, 20)), ("p2", Vec2(80, 40))))));
            return buf;
        }

        [Test]
        public void SampleAtPlainPathMidpoint()
        {
            var buf = Vec2Buffer();
            var v = (Dictionary<string, object?>)Sampler.SampleAt(buf, "positions.p1", InterpType.Vec2, 150)!;
            Assert.AreEqual(5.0, (double)v["x"]!, Eps);
            Assert.AreEqual(10.0, (double)v["y"]!, Eps);
        }

        [Test]
        public void SampleAtNumberOffCentre()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(0, Map(("score", 0.0)));
            buf.Push(100, Map(("score", 50.0)));
            var v = Sampler.SampleAt(buf, "score", InterpType.Number, 25);
            Assert.AreEqual(12.5, (double)v!, Eps);
        }

        [Test]
        public void SampleAtWildcardExpandsToMap()
        {
            var buf = Vec2Buffer();
            var map = (Dictionary<string, object?>)Sampler.SampleAt(buf, "positions.*", InterpType.Vec2, 150)!;
            Assert.AreEqual(2, map.Count);
            var p1 = (Dictionary<string, object?>)map["positions.p1"]!;
            var p2 = (Dictionary<string, object?>)map["positions.p2"]!;
            Assert.AreEqual(5.0, (double)p1["x"]!, Eps);
            Assert.AreEqual(10.0, (double)p1["y"]!, Eps);
            Assert.AreEqual(90.0, (double)p2["x"]!, Eps);
            Assert.AreEqual(20.0, (double)p2["y"]!, Eps);
        }

        [Test]
        public void SampleAtBeforeHorizonReturnsNull()
        {
            var buf = Vec2Buffer();
            Assert.IsNull(Sampler.SampleAt(buf, "positions.p1", InterpType.Vec2, 50));
        }

        [Test]
        public void SampleAtAfterHorizonReturnsNull()
        {
            var buf = Vec2Buffer();
            Assert.IsNull(Sampler.SampleAt(buf, "positions.p1", InterpType.Vec2, 250));
        }

        [Test]
        public void SampleAtEmptyBufferReturnsNull()
        {
            var buf = new SnapshotBuffer(500);
            Assert.IsNull(Sampler.SampleAt(buf, "positions.p1", InterpType.Vec2, 150));
        }

        [Test]
        public void SampleAtWildcardOutsideHorizonReturnsNull()
        {
            var buf = Vec2Buffer();
            Assert.IsNull(Sampler.SampleAt(buf, "positions.*", InterpType.Vec2, 9999));
        }

        [Test]
        public void SampleAtAbsentPlainPathReturnsNull()
        {
            var buf = Vec2Buffer();
            Assert.IsNull(Sampler.SampleAt(buf, "positions.ghost", InterpType.Vec2, 150));
        }

        [Test]
        public void SampleAtEndpointsExact()
        {
            var buf = Vec2Buffer();
            var newest = (Dictionary<string, object?>)Sampler.SampleAt(buf, "positions.p1", InterpType.Vec2, 200)!;
            Assert.AreEqual(10.0, (double)newest["x"]!, Eps);
            Assert.AreEqual(20.0, (double)newest["y"]!, Eps);
            var oldest = (Dictionary<string, object?>)Sampler.SampleAt(buf, "positions.p1", InterpType.Vec2, 100)!;
            Assert.AreEqual(0.0, (double)oldest["x"]!, Eps);
            Assert.AreEqual(0.0, (double)oldest["y"]!, Eps);
        }

        [Test]
        public void SampleAtWildcardEmitsPresentSide()
        {
            var buf = new SnapshotBuffer(500);
            buf.Push(100, Map(("positions", Map())));
            buf.Push(200, Map(("positions", Map(("p1", Vec2(5, 5))))));
            var map = (Dictionary<string, object?>)Sampler.SampleAt(buf, "positions.*", InterpType.Vec2, 150)!;
            Assert.AreEqual(1, map.Count);
            var p1 = (Dictionary<string, object?>)map["positions.p1"]!;
            Assert.AreEqual(5.0, (double)p1["x"]!, Eps);
            Assert.AreEqual(5.0, (double)p1["y"]!, Eps);
        }

        [Test]
        public void RewindSamplesMultiplePathsAtOneTimestamp()
        {
            var buf = Vec2Buffer();
            var r = new Rewind(buf, 150);
            Assert.AreEqual(150, r.AtServerTs);
            var p1 = (Dictionary<string, object?>)r.Sample("positions.p1", InterpType.Vec2)!;
            var p2 = (Dictionary<string, object?>)r.Sample("positions.p2", InterpType.Vec2)!;
            Assert.AreEqual(5.0, (double)p1["x"]!, Eps);
            Assert.AreEqual(90.0, (double)p2["x"]!, Eps);
            var map = (Dictionary<string, object?>)r.Sample("positions.*", InterpType.Vec2)!;
            Assert.AreEqual(2, map.Count);
        }

        [Test]
        public void RewindOutsideHorizonReturnsNullForEveryPath()
        {
            var buf = Vec2Buffer();
            var r = new Rewind(buf, 10);
            Assert.IsNull(r.Sample("positions.p1", InterpType.Vec2));
            Assert.IsNull(r.Sample("positions.*", InterpType.Vec2));
        }
    }
}
