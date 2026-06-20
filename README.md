# Plot Unity SDK

`dev.plot.client` — multiplayer SDK for Unity 2022.3+.

## Install (UPM)

In `Packages/manifest.json`:

```json
{
  "dependencies": {
    "dev.plot.client": "https://github.com/plot-ws/plot-unity.git"
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

## Interpolation (v1f)

Smoothly render remote state by interpolating between server snapshots:

```csharp
// Interpolate every player's position (a vec2) at a 100ms render delay.
room.Interpolate("players.*.position", "vec2", renderDelay: 100);
room.OnFrame += frame => {
    foreach (var kv in frame.Interpolated)
        RenderAt(kv.Key, kv.Value); // kv.Key e.g. "players.<id>.position"
};

// Drive the frame loop from MonoBehaviour.Update():
void Update() => room.TickFrame();
// ...or run a background timer if you are off the Unity main thread:
room.StartFrameLoop(intervalMs: 16);

// Grow the render delay on jittery connections (clamped by maxExtraMs):
room.SetAdaptiveSmoothing(enabled: true, gain: 1.0, maxExtraMs: 100.0);
```

Supported types: `"number"`, `"vec2"`, `"vec3"`, `"quat"`. Paths support a
single-level `*` wildcard.

### Rewind / point sampling

Sample interpolated state at an arbitrary past server time — the rendering-side
analogue of the server's `ctx.rewindTo` (e.g. client-side hit detection). Pure
read: it never touches the frame loop or correction state.

```csharp
// atServerTs is in the server time domain; convert a client time with
// NowMs() - room.ServerClockOffset.
long past = (long)(now - room.ServerClockOffset - 120); // 120ms ago

// One path: returns the interpolated value (a {x,y} map for vec2), or null
// when `past` is outside the buffer's retained horizon.
var p1 = room.SampleAt("players.p1.position", "vec2", past);

// Wildcard: a Dictionary<string, object?> keyed by resolved path, or null.
var all = room.SampleAt("players.*.position", "vec2", past);

// Bind once, read many paths at the same frozen instant:
var r = room.RewindTo(past);
var shooter = r.Sample("players.p1.position", "vec2");
var target = r.Sample("players.p2.position", "vec2");
```

## Client-side prediction (v1g)

Apply local inputs immediately and reconcile against the server's
authoritative state. The engine cannot run your server handler, so you supply
a deterministic predict function `(state, input, player) => nextState`:

```csharp
room.AttachPrediction(initialState, (state, input, player) =>
    Reduce(state, input, player)); // same logic your server handler runs

// Smooth reconciliation drift on a predicted path over ~100ms:
room.Predict("players.me.position", "vec2", correctionMs: 100);

room.OnPredicted += e => Render(e.State); // on local apply AND on reconcile
room.SendPredicted(new { move = "left" }); // optimistic; carries _seq upstream

// Read the corrected (predicted + smoothed) value each frame:
var pos = room.CorrectedState["players.me.position"];
```

## Protocol

This SDK speaks `X-Plot-Protocol: v1b.0`. Wire-format types live under
`Runtime/Protocol/Generated.cs`; do not hand-edit — they are generated
from `packages/protocol/codegen/`.

## Tests

Edit-mode tests under `Tests/`. Run from `Window → General → Test Runner`
in the Editor, or via `game-ci/unity-test-runner` in CI.
