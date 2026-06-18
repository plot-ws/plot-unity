#nullable enable

namespace Plot.Prediction
{
    /// <summary>
    /// Identity of the local player for prediction replay. Ports the handler
    /// <c>Player</c> type ({ id, joinedAt }).
    /// </summary>
    public struct Player
    {
        public string Id;
        public long JoinedAt;

        public Player(string id, long joinedAt)
        {
            Id = id;
            JoinedAt = joinedAt;
        }
    }
}
