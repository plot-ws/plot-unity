using NUnit.Framework;
using System.Text.Json;

namespace Plot.Tests
{
    public class ProtocolRoundtrip
    {
        [Test]
        public void JoinEnvelopeRoundtrips()
        {
            var json = JsonSerializer.Serialize(new {
                type = "join",
                playerId = "p1",
                players = new[] { "p1" },
                ts = 1L,
            });
            using var doc = JsonDocument.Parse(json);
            Assert.AreEqual("join", doc.RootElement.GetProperty("type").GetString());
            Assert.AreEqual("p1", doc.RootElement.GetProperty("playerId").GetString());
            Assert.AreEqual(1, doc.RootElement.GetProperty("players").GetArrayLength());
        }

        [Test]
        public void MessageEnvelopeRoundtrips()
        {
            var json = JsonSerializer.Serialize(new {
                type = "message",
                from = "p2",
                channel = "chat",
                data = new { hi = true },
                ts = 5L,
            });
            using var doc = JsonDocument.Parse(json);
            Assert.AreEqual("message", doc.RootElement.GetProperty("type").GetString());
            Assert.AreEqual("p2", doc.RootElement.GetProperty("from").GetString());
            Assert.AreEqual("chat", doc.RootElement.GetProperty("channel").GetString());
        }

        [Test]
        public void ClientMessageDefaultsChannel()
        {
            var msg = new {
                type = "message",
                channel = "event",
                data = new { x = 1 },
            };
            var json = JsonSerializer.Serialize(msg);
            using var doc = JsonDocument.Parse(json);
            Assert.AreEqual("event", doc.RootElement.GetProperty("channel").GetString());
        }
    }
}
