#nullable enable
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plot
{
    public class Transport
    {
        public event Action<string>? OnMessage;
        public event Action? OnClose;

        private readonly ClientWebSocket _ws = new ClientWebSocket();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public static async Task<Transport> OpenAsync(string wsUrl, string roomCode, string token)
        {
            var t = new Transport();
            t._ws.Options.SetRequestHeader("X-Plot-Protocol", "v1b.0");
            var u = new UriBuilder(wsUrl)
            {
                Query = $"roomCode={Uri.EscapeDataString(roomCode)}&token={Uri.EscapeDataString(token)}",
            };
            await t._ws.ConnectAsync(u.Uri, CancellationToken.None);
            _ = t.ReadLoop();
            return t;
        }

        public void Send(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            _ = _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Close()
        {
            _cts.Cancel();
            _ = _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closed", CancellationToken.None);
            OnClose?.Invoke();
        }

        private async Task ReadLoop()
        {
            var buf = new byte[16 * 1024];
            while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var res = await _ws.ReceiveAsync(buf, _cts.Token);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    OnClose?.Invoke();
                    break;
                }
                var text = Encoding.UTF8.GetString(buf, 0, res.Count);
                OnMessage?.Invoke(text);
            }
        }
    }
}
