# The World Clock — frozen on an empty server, and the ten-line patch that keeps it flowing

**Nothing keyed to Valheim's world time can EVER happen on a dedicated server with zero players
online — crops don't age, pickables don't respawn, production catch-up has nothing to credit —
while every real-frame-time system keeps running, so an away-automation mod looks perfectly alive
as it accomplishes nothing.**

Root-caused and fixed in LetItGrow 0.0.9 (2026-08-26) after a full evening chasing phantom causes;
the mechanism is decompile-verified and the fix is live-verified end-to-end. Written to be lifted
whole into any BepInEx/Harmony mod. Verified against the live Valheim build as of **August 2026**
via `BepInEx.AssemblyPublicizer.MSBuild` on `assembly_valheim`.

---

## The mechanism, verbatim from the decompile

```csharp
private void UpdateNetTime(float dt)   // ZNet, called every Update
{
    if (IsServer())
    {
        if (GetNrOfPlayers() > 0)      // m_players.Count — connected characters
        {
            m_netTime += dt;           // the ONLY place server world time advances
        }
    }
    else
    {
        m_netTime += dt;               // clients always advance (server corrects them)
    }
}
```

The moment the last player logs out, `m_netTime` stops. `ZNet.GetTime()` / `GetTimeSeconds()` are
direct reads of it, and everything "timestamp vs now" in the game is measured against them.

## What silently stops (and what deceptively doesn't)

| Frozen with the clock | Keyed via |
|---|---|
| Plant aging → maturity (`Plant.TimeSincePlanted`) | `s_plantTime` ZDO ticks vs `ZNet.GetTime()` |
| Pickable respawns (berries, flowers, barley stubble) | `picked_time` vs `GetTimeSeconds()` |
| Smelter/fermenter/beehive/sap catch-up on load | start/last timestamps vs `m_netTime` |
| Day/night cycle, ripening, anything "after N game-seconds" | `m_netTime` |

| Keeps running regardless — the trap | Runs on |
|---|---|
| `InvokeRepeating` ticks, coroutines, `Time.time` deadlines | Unity real frame time |
| `SlowUpdate` passes (e.g. `Plant.SUpdate` health checks) | frame time |
| Harmony patches, physics scans, RPCs, ZDO writes | frame time |

That second table is why the freeze is so hard to see: LetItGrow's away-tending visited a farm 17
times, scanned 41 plants, claimed ownership, ran health checks, logged clean passes — and a probe
finally showed every plant's `sincePlanted` pinned at **4 seconds across real hours and multiple
restarts** (`growTime 121s`). Every gate was green except "is it old enough," and it never would be.

**The refinement to AwayFromHome's clock table**: that doc's "timestamps accrue while unloaded"
column (hunger decay, gestation, growup age) is true — *while at least one player is online
somewhere in the world*. An empty server records nothing for anyone. And `ProductionCatchUp` can
only credit time the clock actually recorded, so away-production halts at last-logout without this
patch, no matter how correct the catch-up math is.

## The fix

Postfix `ZNet.UpdateNetTime` and add `dt` in exactly the one case vanilla skips. Config-gated,
server-side by construction (the skipped branch only exists on the server):

```csharp
[HarmonyPatch(typeof(ZNet), "UpdateNetTime")]
public static class ZNet_UpdateNetTime_Patch
{
    [HarmonyPostfix]
    public static void Postfix(ZNet __instance, float dt)
    {
        if (!ConfigManager.AwayTendingTimeFlows.Value) return;   // your toggle
        if (!__instance.IsServer() || __instance.GetNrOfPlayers() > 0) return;
        __instance.m_netTime += dt;                              // publicized field
    }
}
```

Port source: `Let It Grow/Patches/WorldClockPatches.cs` — synced setting **"Time Flows While
Empty"**, default on.

`m_netTime` is part of the world save payload, so credited time **persists across server
restarts** (verified: ages carried through a SIGINT stop/start). While the server *process* is
down, no time passes — this keeps the clock honest, it does not fabricate downtime.

## Consequences to know before adopting

- **Day/night advances while empty.** Players who log back in arrive later in the world's day —
  exactly as if someone had kept playing. Nights pass in real time (no one online to sleep them).
- **Smelters/fermenters catch up on next zone load**, bounded by their own fuel/ore and by
  `Smelter`'s real 3600-seconds-per-gap production cap (decompile-verified in `AwayFromHome.md`'s
  production addendum) — so this cannot create infinite backlog processing.
- **No raids or wild spawns happen** — those systems require players regardless of the clock.
- **Singleplayer and listen servers are unaffected** (the host counts as a player), and pure
  clients never reach the patched branch. ServerSync the toggle anyway for authority hygiene.
- Anything a mod keys to `ZNet.GetTime()` inherits the fix automatically — that is the point of
  patching the clock rather than crediting individual systems.

## Verification recipe

1. Empty server, a planted crop, a debug line printing `TimeSincePlanted()` per pass.
2. Without the patch: the value pins near 0 forever (it counts only player-connected seconds).
3. With the patch: it climbs in real time; the crop matures; restart the server and the age
   carries through the save.
