# Sweep: the baseline client against the baseline dedicated server, run on Linux

**2026-09-07, Wu'barrk's Linux box.** Not a new comparison — `docs/engine-sweeps/2026-09-07-baseline-client-vs-server.md`
already did this one on Windows. This is the same sweep, same day, run through `tools/fetch-builds.sh`
/ `tools/decompile-builds.sh` on the OTHER platform, against the manifest as it stood after P10a
picked up P5's thirteen rows (`docs/ENGINE-SURFACE.md`). Two things wanted checking that only a
second machine can check: does the Linux tooling reproduce the Windows sweep's answer, and do the
thirteen new rows actually resolve in a real decompile rather than just parsing as well-formed text.

Both builds installed via Steam on this box, not shadow-fetched: `~/.steam/steam/steamapps/common/Valheim`
(app 892970, the Linux native client — `Version.GetPlatform()` resolves `Platforms.SteamLinux` here,
never checked on this platform before) and `~/.steam/steam/steamapps/common/Valheim dedicated server`
(app 896660). Decompiled with `tools/decompile-builds.sh -Baseline`'s equivalent invocation (`ilspycmd -p`,
all three assemblies, both builds).

```bash
node tools/diff-engine.js --surface docs/ENGINE-SURFACE.md \
     --from ~/valheim-shadows/src/baseline-client \
     --to   ~/valheim-shadows/src/baseline-server \
     --out  /tmp/linux-client-vs-server.md --all-types
```

```
surface 257: 251 unchanged, 6 body changed, 0 signature changed, 0 gone, 0 manifest problem(s)
types: 35 of 697 differ, 0 only in the client, 1 only in the server
```

---

## What this confirms

**The four version numbers are identical to Don's Windows baseline**: 0.221.12, net 36, player 43,
world 37, read live off THIS box's `Version.CurrentVersion` / `m_networkVersion` / `m_playerVersion`
/ `m_worldVersion` on both the client and server assembly, the same way `docs/ENGINE-BASELINE.md`
read them off the Windows ones. Different platform, same build.

**The six body-changed members are the same six, word for word.** `Game.FixedUpdate` (the reference-
position pin), `GameCamera.UpdateMouseCapture`, `Terminal.AddString`, `ZNet.Awake`, `ZNet.IsDedicated`,
`ZNet.RPC_PeerInfo` — same members, and the unified diffs under each one are the same hunks Don's
report shows (the `SteamFriends.GetPersonaName` / `AutoBackups` lines gone from `ZNet.Awake` on the
server, the `CrossPlatformMultiplayer` privilege block gone from `RPC_PeerInfo`, the Xbox display-name
block gone from `Terminal.AddString`'s other overload, `ZNet.instance.SetReferencePosition(...)`
replacing the respawn/`ZInput`/watchdog block in `Game.FixedUpdate`). Nothing here is platform-specific:
the client-vs-server split is the same split on Linux as on Windows, which is the answer the design
needed — `docs/DESIGN.md` §3.2 rests on the pin being real everywhere this mod runs, not just on the
machine that first read it.

**Zero manifest problems, on all 257 rows including the thirteen just added.** A manifest problem is
what a wrong type or member name looks like — the row would have resolved in NEITHER decompiled tree
and the tool says so explicitly (`docs/ENGINE-SURFACE.md`'s own note: "this file being wrong, not the
engine moving"). `Character.Faction`, `Character.SetTamed`, `Character.InIntro`, `Character.Damage`,
`Character.RPC_Damage`, `Humanoid.Awake`, `Humanoid.Start`, `Humanoid.UnequipAllItems`,
`MonsterAI.MakeTame`, `MonsterAI.m_alertRange`, `BaseAI.m_aggravatable`, `BaseAI.m_passiveAggresive`
and `BaseAI.m_randomMoveRange` all resolved, on a real decompile, on the machine that added them —
independent confirmation of the by-hand reading against `ilspycmd -t Character` / `-t Humanoid` /
`-t MonsterAI` / `-t BaseAI` the manifest update was built from. None of the thirteen is among the six
that differ client-to-server: P5's taming/faction/damage surface is symmetric between the two builds.

## The one new divergence from the Windows numbers, and why it is nothing

Don's Windows sweep: **2** files only in the server (`PlayFabAuthWithCustomID`, `<PrivateImplementationDetails>`).
This Linux sweep: **1** (`PlayFabAuthWithCustomID` only) — `<PrivateImplementationDetails>` exists on
BOTH the Linux client and the Linux server. Checked directly: `assembly_valheim/-PrivateImplementationDetails-.cs`
is present under this run's `baseline-client` tree where it was absent from Don's. `<PrivateImplementationDetails>`
is the C# compiler's own scratch type for switch-on-string jump tables; whether it gets emitted into a
given build depends on which methods the compiler chose to lower that way for that platform's build,
not on anything this mod reaches. It is named nowhere in `ValkyriesCargo/`, same as Don's report already
says of it. The type-file-differs count is 35 here against Don's 40 for the same reason in the other
direction — some of the twenty-one "real platform difference" files his report lists are Windows/Xbox-
only surface that plainly compiles out of a Linux build entirely rather than differing from it.

## What was not run

No `-beta public-test` shadow, same as Don's baseline: **checked live from this box, 2026-09-07**,
`steamcmd +login anonymous +app_info_update 1 +app_info_print 896660 +quit` (the same anonymous,
read-only `app_info` query `tools/fetch-builds.ps1`'s `Get-SteamBranches` uses) still answers with
exactly the same six branches Don found on Windows — `public` (buildid `21981590`, matching his
number exactly), `default_old`, `default_preal`, `default_prebw`, `default_precta`, `default_preml`
— and no `public-test`. Confirmed independently, from a second machine, on a different OS: the branch
is not there today, not a fetch that failed quietly on one platform. See `tools/fetch-builds.sh`'s
own header for the steamcmd-on-Linux notes this run produced.
