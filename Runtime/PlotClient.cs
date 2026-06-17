#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Plot
{
    public class PlotOptions
    {
        public string AppKey = "";
        public string PlayerId = "";
        public string ApiUrl = "https://api.plot.ws";
        public string? Token;
    }

    public class JoinOptions
    {
        public string? Mode;
        public string? RoomCode;
        public int? MaxPlayers;
    }

    public class PlotClient
    {
        private readonly PlotOptions _options;
        private readonly HttpClient _http = new HttpClient();

        public PlotClient(PlotOptions options)
        {
            _options = options;
        }

        public async Task<Room> JoinAsync(JoinOptions opts)
        {
            var connectReq = new {
                appKey = _options.AppKey,
                playerId = _options.PlayerId,
                token = _options.Token,
            };
            var res = await _http.PostAsync(
                $"{_options.ApiUrl}/v1/connect",
                new StringContent(
                    JsonSerializer.Serialize(connectReq),
                    Encoding.UTF8,
                    "application/json"));
            res.EnsureSuccessStatusCode();
            var connect = JsonSerializer.Deserialize<ConnectResponse>(
                await res.Content.ReadAsStringAsync())!;
            var roomCode = opts.RoomCode ?? throw new InvalidOperationException(
                "roomCode required; matchmake-by-mode not yet wired in v1e SDK");
            var transport = await Transport.OpenAsync(connect.WsUrl, roomCode, connect.Token);
            return new Room(_options.PlayerId, transport);
        }

        private class ConnectResponse
        {
            public string Token { get; set; } = "";
            public long ExpiresAt { get; set; }
            public string WsUrl { get; set; } = "";
        }
    }
}
