#nullable enable
using System;
using System.Text.Json;

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

    public class Room
    {
        public event Action<MessageEvent>? OnMessage;
        public event Action<PresenceEvent>? OnPlayerJoined;
        public event Action<PresenceEvent>? OnPlayerLeft;

        private readonly Transport _transport;
        private readonly string _playerId;

        public Room(string playerId, Transport transport)
        {
            _playerId = playerId;
            _transport = transport;
            _transport.OnMessage += HandleIncoming;
        }

        public void Send(object data, string channel = "event")
        {
            var env = new { type = "message", channel, data };
            _transport.Send(JsonSerializer.Serialize(env));
        }

        public void Leave()
        {
            _transport.Close();
        }

        private void HandleIncoming(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var t = doc.RootElement.GetProperty("type").GetString();
            if (t == "message" && OnMessage != null)
            {
                OnMessage(new MessageEvent {
                    From = doc.RootElement.GetProperty("from").GetString() ?? "",
                    Data = doc.RootElement.GetProperty("data"),
                });
            }
            else if (t == "join" && OnPlayerJoined != null)
            {
                OnPlayerJoined(new PresenceEvent {
                    PlayerId = doc.RootElement.GetProperty("playerId").GetString() ?? "",
                });
            }
            else if (t == "leave" && OnPlayerLeft != null)
            {
                OnPlayerLeft(new PresenceEvent {
                    PlayerId = doc.RootElement.GetProperty("playerId").GetString() ?? "",
                });
            }
        }
    }
}
