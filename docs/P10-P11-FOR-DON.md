# P10 and P11 — for Lord Don Juan Coyote

Two new packages, both yours end to end. Wu'barrk is not taking a share of either; this is the
brief, not a split. P4 is answered and pushed (PR #8), P5 is next on his side, and these two run
on yours whenever you want them.

Numbering continues `docs/WORKSPLIT.md`: P1–P9 are the shipping mod, **P10 is version resilience**
and **P11 is the pre-1.0 shakedown**. Neither blocks 0.1.0. P10a is the one with a deadline
attached to somebody else's calendar, because playtest branches move whether we look or not.

---

## Why these two, in one paragraph

Everything this mod knows about Valheim it knows from **one build**: 0.221.12, network version 36,
player 43, world 37, decompiled 2026-09-06. And the dependency is not the API surface — a rename
would at least fail the build. The dependency is roughly **seventy method bodies**: that
`RandEventSystem.FixedUpdate` only advances `m_time` while a player is within `m_eventRange`, that
`ZNetView.Awake`'s init branch re-applies `Type` and `Distant` but never `Persistent`, that
`ZSyncTransform.m_velocityCached` starts at `negativeInfinity`, that the dedicated server pins its
reference position to a million. **A patch can change any of those without touching a signature, and
neither the build nor the 965 checks would say a word.** P10 builds the thing that says a word.
P11 is the pass where every number, every lock and every game call gets looked at once, together,
before 1.0 makes all of it public.

---

## P10a — shadow copies of the playtest builds, and a comparative decompile

### What it is

Pull the live and playtest builds of **both** the client and the dedicated server into throwaway
directories, decompile all four, and diff them against the baseline this mod was written on —
filtered down to the members we actually name, so the report is readable instead of a hundred
thousand lines of Unity noise.

### The install side

`steamcmd` is the tool. Four things to get right and one to be careful about:

- **App ids**: `892970` is the client, `896660` is the dedicated server. They are different builds
  and we depend on the difference — the reference-position pin is in the server's
  `Game.FixedUpdate` and not in the client's. Both, every time.
- **Login**: `896660` takes `+login anonymous`. `892970` needs an account that owns Valheim.
- **Branch**: `-beta public-test` for the playtest branch, no `-beta` argument for live. If a
  branch has a password, `-betapassword`.
- **`+force_install_dir` must come BEFORE `+login`**, and both before `+app_update`. steamcmd
  applies these in argument order and silently uses the default library if the directory comes
  late. That is exactly how a "shadow copy" quietly becomes an overwrite of a real install.
- **Never aim it at a working install.** Shadow directories only, one per (app, branch), and
  nothing else in them.

```bash
STEAM=~/steamcmd/steamcmd.sh          # or steamcmd.exe
SHADOW=~/valheim-shadows

# live dedicated server
"$STEAM" +force_install_dir "$SHADOW/server-live"  +login anonymous \
         +app_update 896660 validate +quit
# playtest dedicated server
"$STEAM" +force_install_dir "$SHADOW/server-test"  +login anonymous \
         +app_update 896660 -beta public-test validate +quit
# live client  (needs the owning account)
"$STEAM" +force_install_dir "$SHADOW/client-live"  +login <account> \
         +app_update 892970 validate +quit
# playtest client
"$STEAM" +force_install_dir "$SHADOW/client-test"  +login <account> \
         +app_update 892970 -beta public-test validate +quit
```

Record the `buildid` for each from its `appmanifest_<appid>.acf`, and the version from the assembly
itself — `Version.CurrentVersion`, plus `m_networkVersion`, `m_playerVersion`, `m_worldVersion`.
Those four numbers are the identity of a build; the Steam build id alone is not enough, because a
branch can be re-pushed.

### The decompile side

`ilspycmd`, whole-assembly, one directory per build, so the diff is a directory diff:

```bash
export DOTNET_ROOT=$HOME/.dotnet          # ilspycmd will not start without this
M="$SHADOW/client-test/valheim_Data/Managed"
ilspycmd -r "$M" "$M/assembly_valheim.dll" -o "$SHADOW/src/client-test" -p
```

`-p` writes a project tree (one file per type), which is what makes `diff -ru` between two versions
usable. Do it for all four, plus **the baseline** — 0.221.12 client and server, which Wu'barrk has
locally and can hand over if your copy has moved on.

The client's managed folder also holds `assembly_utils.dll` and `assembly_guiutils.dll`; the last of
those is now a real dependency since P7 (`Localization`), so it belongs in the sweep.

### The comparison

A raw diff of two Valheim builds is thousands of files. The point of this package is the filter.

**The surface manifest.** A checked-in list of every game type and member this mod names. The
starting list is Appendix A below, extracted from the sources; it wants one curation pass to drop
the false hits and add the members reached only through Harmony patch targets and `___field`
injection, which a grep will not see. Regenerate it any time with:

```bash
grep -ohE '\b(ZDOMan|ZNetScene|ZNetView|ZDO|ZDOID|ZDOVars|ZNet|ZRpc|ZRoutedRpc|ZPackage|ZoneSystem|ZSyncTransform|EnvMan|RandEventSystem|RandomEvent|Valkyrie|Character|Humanoid|MonsterAI|BaseAI|Player|Inventory|ItemDrop|ObjectDB|Localization|MessageHud|Chat|NpcTalk|Odin|Tameable|SEMan|Terminal|Game|GameCamera|Minimap)\.[A-Za-z_][A-Za-z0-9_]*' \
     --include=*.cs -r ValkyriesCargo/ | sort -u
```

**The report.** For each surface member, one of four verdicts, and only the last three get printed:

| verdict | meaning | what it costs us |
|---|---|---|
| unchanged | byte-identical decompiled body | nothing |
| body changed | same signature, different body | **this is the dangerous one.** A recorded engine fact may now be false |
| signature changed | renamed, re-typed, or accessibility moved | the build breaks, or a patch silently stops applying |
| gone | the member no longer exists | as above |

Only the "body changed" row is new information — the other three announce themselves. Every one of
them wants a human reading the diff against the claim in `CLAUDE.md` "Engine facts the code relies
on today" and `docs/DESIGN.md` §0, and either confirming the fact or correcting the document.

**Also diff, separately, client against server for the same version.** We assert exactly one
behavioural difference between the two builds (the reference-position pin). That assertion is load
bearing for the whole authored-ZDO design and it has never been checked as a *complete* list — only
as "we found one". If a playtest adds a second, the Spawner's reasoning needs revisiting.

### Deliverables

- `docs/ENGINE-BASELINE.md` — the four version numbers and the build ids of every build swept, with
  the date. This becomes the thing `CLAUDE.md`'s engine-facts section is dated against.
- `tools/fetch-builds.ps1` (or `.sh`) — the steamcmd calls above, parameterised, with the shadow
  directories hard-refused from pointing at a real install.
- `tools/decompile-builds.ps1` and `tools/diff-engine.py` — the sweep and the filtered report.
- `docs/ENGINE-SURFACE.md` — the curated manifest.
- One report per sweep, checked in, so the history of what moved is readable later.

---

## P10b — auto-detect and patch adaptives; mostly 1.0-launch ready

### What it is

P10a tells *us* what moved, offline. P10b makes **the running mod** notice and behave, so a player
who updates Valheim before we update the mod gets a mod that says what is wrong and turns off the
part that broke, instead of a mod that throws every frame or, worse, quietly does the wrong thing.

### The pieces

**1. Say what you were built for, and what you found.** At boot, read `Version.CurrentVersion`,
`m_networkVersion`, `m_playerVersion`, `m_worldVersion`; compare against the baseline constants the
DLL was compiled with; log one line either way, and put it in `cargo status`. A mismatch is not an
error — it is the first thing anyone should see in a log when a bug report arrives.

**2. Probe before you rely.** For each engine fact that a patch depends on, a probe that resolves
it once and records pass/fail. Two house rules bind here and both matter:

- **Resolution goes in its own method, never the method that does the work.** Mono resolves field
  access when the *caller* is JIT-compiled, so a try/catch in the method that uses the field never
  runs. This is already a house rule; P10b is where it gets used deliberately rather than defensively.
- **Field injection over reflection in patches** (`___m_nview`): a renamed field fails at *patch*
  time, loudly, which is exactly the signal we want.

**3. Degrade, do not throw.** A failed probe disables its one feature, logs once, and `cargo status`
names it. The mod keeps running. The existing three-strikes try/catch in every patch body is the
floor, not the ceiling — that stops the log flooding but still lets a broken feature look like a
working one from outside.

**4. Refuse rather than corrupt.** The one case where turning off is not enough: if the *sidecar
format* or the *wire format* cannot be read, refuse the operation and say so. Silent partial reads
of a market file are how someone loses a purse.

**5. What "mostly 1.0 ready" means.** Not that 1.0 is supported — it is not out and cannot be
tested. It means: when 1.0 lands, the failure mode is a clear log line and a disabled feature rather
than a crash, and P10a's sweep turns the fix into a morning's work instead of a re-derivation.
Worth pinning down early: ServerSync's version gate refuses a client on a mismatched **mod** version
at handshake, which is a different axis from the game version. Both want a line in the log.

### Where the risk actually is

Ranked by how much silence a break would come with, worst first:

1. `RandEventSystem` — `m_events` registration, the `FixedUpdate` clock rule, the 2 s broadcast, the
   name-resolved-from-the-client's-own-list behaviour. A change here breaks the visit with no
   exception at all.
2. `ZDO` authoring — `CreateNewZDO` not setting the prefab, `Persistent`/`Distant`/`Type` surviving
   `ZNetView.Awake`, the owner id being the peer uid. Same silence.
3. `ZNetScene.InActiveArea` and `ZoneSystem` zone maths — a zone-size or active-area change moves
   every waypoint. `FlightPlan` is pure and asserts against the stub, so this one at least fails a test.
4. `ZSyncTransform.OwnerSync`'s velocity caching — the flight looks stuttery on other screens and
   nothing logs.
5. `Valkyrie` — our patch skips it entirely, so a vanilla change matters less than usual, but
   `m_attachPoint` / `m_attachOffset` / `m_dropHeight` are read.
6. `Character.Damage`, `Character.InIntro`, `MonsterAI` — P5's surface; audit it after P5 lands, not
   before.

---

## P11 — config shakedown, the authority audit, embedded assets, the P4/P5 call audit

Four passes. They are one package because they share a method: read every one, against the running
game, and write down what is actually true.

### 11a. Full default config shakedown

Every key in `Config/ModConfig.cs`, one row each:

- Is the **default** the value we want a fresh install to have? (Not the value that was convenient
  while testing.)
- Is the **range** right, and does the code clamp to it independently? Several call sites re-clamp
  with their own literals — `Spawner` clamps the flight numbers, `CargoFlight` clamps speed and turn
  rate. Those literals and the `AcceptableValueRange` must agree, and today nothing checks that.
- **`Server.*` or `Client.*`** — and is that the side that actually reads it?
- Is the **description** true? It is the only documentation most players read.
- **Is it read at all?** A dead key is worse than no key.

Two known ones to settle in this pass: `MerchantLifespanSeconds` defaults to 300 with the comment
"Odin's own timer is 300", but the shipped `Odin` prefab says `m_ttl` **60** — the 300 is the field
initialiser, not the prefab. And `BodyPrefab` versus the new `Server.CustomBody`: the README still
tells people to set `BodyPrefab` to Ingvar, which is wrong for the code as built.

### 11b. Server-authoritative and ServerSync audit

The question is not "is it synced" but **"what can a client change that it should not?"**

- Every `Server.*` key: verify it is actually in the `ConfigSync` and actually locked, at runtime,
  on a live client — not by reading the binding code. The check is item 4 in `CLAUDE.md`'s verify
  list and it has never been run.
- Every RPC: `VCargo_admin`, `VCargo_reply`, `VCargo_open`, `VCargo_close`, `VCargo_deal`, `VCargo_ack`, `VCargo_claim`,
  `VCargo_dismiss`, `VCargo_dealt`. For each — who may send it, what the server validates, and what a
  hostile client gets by lying. The prices-the-player-saw check and the nonce ring are the
  interesting ones.
- The ZDO writes. `VCargo_rested` / `VCargo_comfort` are **written by the client on its own character** and
  read by the server's scheduler. That is a client asserting its own eligibility. It is probably
  fine — the worst case is an undeserved visit — but it should be a written-down decision rather
  than an accident of where comfort is computed.
- Write the trust boundary down in one place. There is no document today that says what the server
  believes and what it verifies.

### 11c. Embedded assets

End to end, on a real client: the bundle embeds, `BodyLoader.Attach` finds it, the clips come
through under the six take names, the body stands up, and the mod still works with the bundle
**absent** (`CustomBody` false, a dedicated server, an old install). Both paths, both proven.
`cargo body` is the gate Don already specified; this is the pass where its output gets pasted
somewhere permanent.

### 11d. The P4/P5 functional-call audit

**Run this after P5 lands, by Opus, against the real assembly — not the publicized one.** House
rule 5 is that a clean build proves nothing about member access, and P4 and P5 are the two packages
that touch the most game members that have never executed. Every call site: the member exists, is
public in the *real* assembly, has the signature we think, and the body does what the comment above
the call claims. PR #8's review is the model — three of its four findings were true statements about
code that compiled cleanly and passed 878 tests.

Specifically worth re-reading after P5: `Character.Damage` as a bare `InvokeRPC` forwarder,
`Character.InIntro` zeroing velocity but **not** granting immunity, `MonsterAI.MakeTame`,
`BaseAI.IsEnemy`'s decision order, `MonsterAI.UpdateAI`'s peaceful branch and its two entry
conditions, and `Chat.SetNpcText` being unguarded client UI.

---

## Order, and one dependency

- **P10a first and soon** — playtest branches move on their own schedule, and a baseline captured
  after a change is worth much less than one captured before.
- **P10b after P10a**, because the sweep tells you which probes are worth writing.
- **P11 after P5**, because 11d has nothing to audit until then. 11a and 11b can start any time.
- Nothing here blocks 0.1.0. If 0.1.0 ships first, P10a's baseline is simply taken at the tag.

The one thing these need from Wu'barrk's side: the **0.221.12 baseline decompile**, if your install
has already moved past it. He has 0.221.12 client and server locally and can hand over either the
DLLs or the decompiled tree.

---

## Appendix A — the engine surface, as extracted 2026-09-07

Seventy-six type-qualified references, from `grep` over the shipping sources. **Uncurated**: four
are false hits (`RandEventSystem.cs`, `ZNet.cs` from comments; `Terminal.CargoTerminal`;
`Heightmap.Biome` as a type), and the list cannot see members reached through Harmony patch targets
or `___field` injection, which want adding by hand from `Patches/`.

```
Character.GetAllCharacters      ZDO.GetHashZDOID            ZNet.GetWorldUID
Chat.HasFocus                   ZDO.ObjectType              ZNet.instance
Chat.instance                   ZDOID.None                  ZNet.IsAdmin
Chat.Update                     ZDOMan.CreateNewZDO         ZNet.IsDedicated
EnvMan.instance                 ZDOMan.DestroyZDO           ZNet.m_onlineBackend
EnvMan.IsDay                    ZDOMan.GetSessionID         ZNetScene.CreateObject
EnvMan.m_dayLengthSec           ZDOMan.instance             ZNetScene.InActiveArea
GameCamera.instance             ZDOVars.s_baseValue         ZNetScene.instance
GameCamera.UpdateMouseCapture   ZDOVars.s_dead              ZNetScene.RemoveObjects
Game.FixedUpdate                ZDOVars.s_playerName        ZNetView.Awake
Game.instance                   ZDOVars.s_velHash           ZNetView.GetZDO
Game.SkipIntro                  ZNet.ConnectionStatus       ZNetView.m_initZDO
Inventory.AddItem               ZNet.Disconnect             ZoneSystem.GetZone
Localization.instance           ZNet.GetConnectionStatus    ZoneSystem.instance
MessageHud.instance             ZNet.GetServerRPC           ZoneSystem.m_activeArea
MessageHud.MessageType          ZNet.GetTimeSeconds         ZoneSystem.m_zoneSize
Minimap.IsOpen                  ZNet.GetUID                 ZRoutedRpc.Everybody
Minimap.Update                  Odin.m_despawn              ZRoutedRpc.instance
ObjectDB.instance               Player.m_comfortLevel       ZRpc.Deserialize
Player.m_localPlayer            Player.TakeInput            ZRpc.Register
Player.Update                   RandEventSystem.Awake       ZRpc.Serialize
RandEventSystem.instance        RandEventSystem.RPC_SetEvent ZSyncTransform.OwnerSync
RandomEvent.m_time              SEMan.s_statusEffectRested   Valkyrie.Awake
Terminal.ConsoleCommand         Terminal.ConsoleEventArgs    Valkyrie.m_instance
```

Plus the ~62 `m_*` field names the sources mention, which are in
`grep -ohE '\bm_[a-zA-Z0-9_]+' --include=*.cs -r ValkyriesCargo/ | sort -u`.

---

*Brief written by Wu'barrk's Claude, 2026-09-07, at Thorium's request. Both packages are yours,
Lord Don Juan Coyote — this is the handover, not a claim on either.*
