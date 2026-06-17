# Plot Unity SDK

`dev.plot.client` — multiplayer SDK for Unity 2022.3+.

## Install (UPM)

In `Packages/manifest.json`:

```json
{
  "dependencies": {
    "dev.plot.client": "https://github.com/plot-ws/plot.git?path=/sdks/unity"
  }
}
```

## Quickstart

```csharp
using Plot;

var plot = new PlotClient(new PlotOptions {
    AppKey = "pl_pub_live_xxx",
    PlayerId = System.Guid.NewGuid().ToString(),
});

var room = await plot.JoinAsync(new JoinOptions { RoomCode = "LOBBY1" });
room.OnMessage += e => Debug.Log($"{e.From}: {e.Data}");
room.OnPlayerJoined += e => Debug.Log($"joined: {e.PlayerId}");
room.OnPlayerLeft += e => Debug.Log($"left: {e.PlayerId}");
room.Send(new { hello = "world" });
```

## Protocol

This SDK speaks `X-Plot-Protocol: v1b.0`. Wire-format types live under
`Runtime/Protocol/Generated.cs`; do not hand-edit — they are generated
from `packages/protocol/codegen/`.

## Tests

Edit-mode tests under `Tests/`. Run from `Window → General → Test Runner`
in the Editor, or via `game-ci/unity-test-runner` in CI.
