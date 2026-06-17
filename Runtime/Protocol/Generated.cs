// AUTO-GENERATED — do not edit. Run `pnpm --filter @plot/protocol codegen`.
// SCHEMA_VERSION = v1b.0
using System;
using System.Collections.Generic;

namespace Plot.Protocol
{
    public static class Version
    {
        public const string SchemaVersion = "v1b.0";
    }

    public enum Channel { State, Event, Chat, Unreliable }

    public class ConnectRequest
    {
        public string AppKey { get; set; } = "";
        public string PlayerId { get; set; } = "";
        public string? Token { get; set; }
    }

    public class ConnectResponse
    {
        public string Token { get; set; } = "";
        public long ExpiresAt { get; set; }
        public string WsUrl { get; set; } = "";
    }

    public abstract class ServerEnvelope
    {
        public string Type { get; set; } = "";
        public long Ts { get; set; }
    }

    public class JoinEnvelope : ServerEnvelope
    {
        public string PlayerId { get; set; } = "";
        public List<string> Players { get; set; } = new();
    }

    public class LeaveEnvelope : ServerEnvelope
    {
        public string PlayerId { get; set; } = "";
        public List<string> Players { get; set; } = new();
    }

    public class MessageEnvelope : ServerEnvelope
    {
        public string From { get; set; } = "";
        public string Channel { get; set; } = "event";
        public object? Data { get; set; }
    }

    public class StateSnapshotEnvelope : ServerEnvelope
    {
        public object? State { get; set; }
    }

    public class StatePatchEnvelope : ServerEnvelope
    {
        public object? Patch { get; set; }
    }

    public class ReconnectTokenEnvelope : ServerEnvelope
    {
        public string Token { get; set; } = "";
        public long ExpiresAt { get; set; }
    }

    public class ErrorEnvelope : ServerEnvelope
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class ClientMessage
    {
        public string Type { get; set; } = "message";
        public string Channel { get; set; } = "event";
        public object? Data { get; set; }
    }
}
