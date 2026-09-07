# Jotunn integration + headless-safe automation facts

**Applies to every project in `WubarrkCODING`.** Written 2026-08-24 while building `Let It Grow`'s
farming-automation expansion (first time this workspace added Jotunn to a project that uses the
plain `..\libs-Tools\*.dll` HintPath reference style instead of the `BepInEx.Core`-NuGet style
`DvergrAllies`/`MistsofAvalor`/`BlightedHeart` use). Everything below was verified against a real
`dotnet build` on this machine, or against `DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`
(line numbers cited where used).

---

## 1. `dotnet` is installed but NOT on PATH on this machine

`/home/rohan/.dotnet/dotnet` (SDK 8.0.424) exists and builds real BepInEx mod csproj files fine —
`which dotnet`/`dotnet --version` fail because it's simply not exported to `PATH` in this shell
environment. Every build command in this workspace's docs assumes PowerShell on Windows; on this
Linux box, invoke it by full path: `/home/rohan/.dotnet/dotnet build Foo.csproj -c Debug`.

## 2. Adding the `JotunnLib` NuGet package to a HintPath-style project breaks it

`Njord`/`Fatty`/`Let It Grow` (before this) reference `BepInEx.dll`/`0Harmony.dll`/etc. directly via
`<Reference><HintPath>..\libs-Tools\X.dll</HintPath></Reference>`, never via NuGet. Adding
`<PackageReference Include="JotunnLib" Version="2.*" />` on top of that reference style produces a
build that reports **0 errors but is actually broken**:

- `MSB3243`: "No way to resolve conflict between BepInEx, Version=5.4.23.3... and BepInEx" (same for
  0Harmony) — the NuGet package pulls in its own copies of BepInEx/0Harmony that collide with the
  HintPath ones, and MSBuild picks one arbitrarily rather than failing loudly.
- `MSB3245`: `Could not locate the assembly "assembly_valheim_publicized"` (and `assembly_utils_publicized`,
  `assembly_guiutils_publicized`, `gui_framework_publicized`, `SoftReferenceableAssets_publicized`,
  `HarmonyXInterop`, `Mono.Cecil`, `MonoMod.Utils`, `UnityEngine.ProfilerModule`, `BepInEx.Preloader`,
  and several more) — the NuGet package assumes the `BepInEx.Core` + `BepInEx.AssemblyPublicizer.MSBuild`
  + `UnityEngine.Modules` NuGet-driven project shape (which auto-generates `_publicized` copies with
  specific names) that `DvergrAllies.csproj` uses. It does **not** know about this project's inline
  `<Publicize>true</Publicize>` HintPath convention, and none of those assemblies exist anywhere on
  disk in this shape.

**Fix, proven working**: drop the `JotunnLib` package entirely and reference the DLL directly instead,
exactly like `MistsofAvalor.csproj` already does:
```xml
<Reference Include="Jotunn">
  <HintPath>..\libs-Tools\Jotunn.dll</HintPath>
  <Private>false</Private>
</Reference>
```
No separate `HarmonyXInterop`/`Mono.Cecil`/`MonoMod.Utils` references are needed alongside it —
whatever `Jotunn.dll` needs from those is merged/embedded inside it already (same category of thing
as `JetBrains.Annotations`, below). `[BepInDependency(Jotunn.Main.ModGuid)]` and
`[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]` (the latter
needs `using Jotunn.Utils;`, **not** `using BepInEx;` — it is a Jotunn type despite living right next
to BepInEx's own `[BepInPlugin]`) both resolve fine off the plain `Jotunn.dll` reference.

## 3. Compiling `libs-Tools\ServerSync.cs` needs more than BepInEx+0Harmony+assembly_valheim

Every project that does `<Compile Include="..\libs-Tools\ServerSync.cs" />` (Njord, Fatty, Let It
Grow) needs this **full** reference set for it to compile clean, discovered by adding references one
compiler-error at a time rather than copying Njord's list blind:

- `assembly_utils.dll` — `SyncedList` (the type of `ZNet.m_adminList`) and the
  `string.GetStableHashCode()` extension both live here, not in `assembly_valheim.dll`. (This matches
  `VALHEIM-API-REFERENCE/README.md`'s own "two things live in assembly_utils.dll, unverified" note —
  now verified: they compile clean once referenced, no reimplementation needed.)
- `Unity.TextMeshPro.dll` — `TMP_Text`, used by ServerSync's admin-UI code paths.
- `UnityEngine.UI.dll` — `MaskableGraphic`, same reason.
- The obvious ones: `BepInEx.dll`, `0Harmony.dll`, `assembly_valheim.dll` (Publicized), `UnityEngine.dll`,
  `UnityEngine.CoreModule.dll`.

`JetBrains.Annotations`' `[PublicAPI]` attribute (used throughout `ServerSync.cs`) needs **no explicit
reference at all** — it resolves transitively through one of the above (almost certainly merged into
`BepInEx.dll` or `0Harmony.dll` at build time by whoever produced those DLLs), confirmed by a clean
`--no-incremental` build with no separate `JetBrains.Annotations.dll` anywhere in `libs-Tools`.

## 4. Common UnityEngine modules a mod hits the moment it does anything beyond Harmony patches

Beyond the always-needed `UnityEngine.dll`/`UnityEngine.CoreModule.dll`:
- **`GUI`/`GUILayout`/`GUI.Window`** (any custom in-game IMGUI panel) → `UnityEngine.IMGUIModule.dll`.
- **`Physics.OverlapSphere`/`Physics.CheckSphere`/`Collider`** (any nearby-object scan) →
  `UnityEngine.PhysicsModule.dll`.
- **`Input.GetKeyDown`/`Input.GetMouseButton`** → `UnityEngine.InputLegacyModule.dll` (already noted
  in `Fatty`'s own project instructions from the Feast Ledger drag fix; re-confirmed here for a second,
  unrelated feature — this is clearly a recurring first-hit-costs-a-build-cycle gap, not a one-off).
- **`AssetBundle`/`AssetUtils.LoadAssetBundleFromResources`** → `UnityEngine.AssetBundleModule.dll`.

None of these are optional extras — a project that adds a GUI panel, a radius scan, or a hotkey and
doesn't yet have the matching module reference gets a same-shaped `CS0246`/error wall each time
(`GUILayout`/`Physics`/`Input` "does not exist in the current context"), not a subtle runtime failure.
Cheap to add all four up front to any new mod that will plausibly grow a UI or automation feature.

## 5. `Pickable.RPC_Pick` can be called directly — no `Player`/`Humanoid` needed at all

`Pickable.Interact(Humanoid character, bool repeat, bool alt)` (`assembly_valheim.decompiled.cs:59699`)
is what the "E" key calls, but it only touches `character` for two things: a tar-stuck message and a
Farming-skill XP roll (`character is Player player`). The actual harvest — spawning the item drop(s)
and marking the object picked — is entirely inside **`RPC_Pick(long sender, int bonus)`** (`:59734`),
which never reads its `sender` for anything beyond the owner check (`m_nview.IsOwner()`) already
required to call it at all, and treats `bonus` as a plain skill-bonus multiplier (0 = no bonus).

**Once `assembly_valheim` is Publicized, `RPC_Pick` is an ordinary public instance method** — call it
directly (`pickable.RPC_Pick(0L, 0)`) to auto-harvest something with **zero** `Player`/`Humanoid`
reference anywhere in the call, as long as you already own (or have claimed) the `Pickable`'s ZDO.
This matters because vanilla's own auto-harvest precedent, `Piece.OnPlaced()`'s harvest-radius sweep
(`:117597`), calls `Interact(Player.m_localPlayer, false, false)` — which is fine for that code (it
only ever runs client-side, right when a player places something) but is the **wrong template to copy
for a persistent, server-tickable automation object**, per the next fact.

## 6. `Player.m_localPlayer` is null on a true dedicated server — even with players connected

`Player.m_localPlayer` is a *client-local* concept: the character the running client instance itself
controls. A dedicated (non-listen) server process never has one of these, ever, regardless of how
many remote clients are connected — this matches the already-documented fact in `Fatty`'s own project
notes ("`Player` objects do not exist headless at all... `Player.GetAllPlayers()` walks the local
instance list") — but that phrasing undersells it: **it's not that there are zero `Player` instances
on a dedicated server with people connected; it's that `m_localPlayer` specifically stays null while
`Player.GetAllPlayers()` correctly returns the connected characters' server-side networked replicas.**

Any tick-driven automation component (a persistent `ZNetView`'d object doing periodic work via
`InvokeRepeating`, e.g. a farm scarecrow, a feeding trough, anything modeled on `AwayFromHome`'s
`KeeperFeeder`/`KeeperSupply` pattern) that needs to call an instance method genuinely scoped to
`Player` (like `PlacePiece`, which stamps a creator id) should resolve `Player.GetAllPlayers()
.FirstOrDefault()` instead of `Player.m_localPlayer`. This works identically on a listen server, a
client's own single-player game, and a true dedicated server with anyone connected; it only comes up
empty when the world is *completely* unpopulated, which is the one case where "pause this tick and
retry the next one" is the correct, honest behavior anyway (there is no `Humanoid` anywhere to act
through, full stop — this is not a workaround-able gap, it's what "the world has nobody in it" means
for any player-instance-scoped vanilla API).

## 7. [SUPERSEDED 2026-08-24] The bundled `libs-Tools/Editor/Unity` copy is a dead end — but a fresh native install is NOT

The original finding here (no exec bit, no license, no Windows player module on the *bulk-copied*
`libs-Tools/Editor/Unity` binary) is still accurate **for that specific copied binary** — don't try to
resurrect it, `chmod +x` alone won't fix the missing license. But the broader conclusion this section
originally drew — "any Unity Editor step has to happen on a licensed Windows machine" — turned out to
be **wrong** once the user set up a genuinely fresh Arch Linux machine from scratch. On a fresh box,
Unity's own official CLI tool gets you a fully working, headless, license-activated Editor entirely
natively on Linux, no Windows machine involved at all:

1. Install the CLI (`~/.local/bin/unity`, distinct from the Unity Hub GUI) and sign in:
   `unity auth login`, then `unity license activate --personal` (or whatever license the user holds).
2. `unity install <version> -y --accept-eula` — installs the actual Editor.
3. `unity install-modules -e <version> -m windows-mono -y --accept-eula` — needed *only* if a build
   script targets `BuildTarget.StandaloneWindows64` (true for both `AvalorBundleBuilder.cs` and
   `LetItGrowBundleBuilder.cs`, since both match a Windows Valheim client). List available modules
   first with `unity install-modules -e <version> -l`.
4. **The one real Arch-specific blocker**: the Editor binary fails with `error while loading shared
   libraries: libxml2.so.2: cannot open shared object file` — confirmed via
   `ldd /path/to/Editor/Unity`. Root cause: Arch's current `libxml2` package ships SONAME `.so.16`; the
   Unity Linux Editor binary was built against the older `.so.2` ABI. Fix: `sudo pacman -S
   libxml2-legacy` (this is in Arch's official `extra` repo, not the AUR — no AUR helper needed). This
   is a sudo-gated system package install a Claude Code session cannot run non-interactively (no TTY
   for the password prompt) — hand the exact command back to the user to run themselves.
5. `unity projects create <name> --path <dir> --editor-version <version> --template
   com.unity.template.3d` now works and creates a real project with `Library/`, `Packages/`,
   `ProjectSettings/` etc.
6. Add any needed packages by editing `Packages/manifest.json` directly (e.g.
   `"com.unity.cloud.gltfast": "6.19.0"` — version copied from the known-working `AwayFromHome/Unity`
   reference project's own manifest; no scoped registry needed, it's a plain Unity registry package).
7. Copy `.glb`/script assets into `Assets/`, then run headlessly with **`unity run <project> --
   -executeMethod <Class>.<Method> -logFile <path>`** — do **not** also pass `-batchmode`/`-nographics`/
   `-quit` yourself, the `run` subcommand already manages those and errors out
   ("conflicts with a reserved Unity flag managed by this command") if you do.

**First launch is slow** (package resolution + importing every asset, e.g. two ~25-30MB `.glb` files)
but exit code 0 with real log output is the normal, expected outcome — not a hang.

See §8 for a real compile bug this surfaced, and its fix — worth checking any time `BuildAssetBundles`
reports success but produces a suspiciously small/empty bundle file.

## 8. `BuildAssetBundles` silently "succeeds" with a 0-byte bundle if Player scripts fail to compile — and `com.unity.collections@2.6.6` has exactly this bug for a Release `StandaloneWindows64` target

`UnityEditor.BuildPipeline.BuildAssetBundles(...)` has to compile **Player** scripts for the target
platform before it can pack anything — even a bundle containing zero MonoBehaviours, like a bundle of
just a `Mesh` + a `Texture2D`. If that Player-script compile fails, `BuildAssetBundles` returns without
writing a real bundle file, but does **not** throw — a bundle-builder script that doesn't check the
return value (or the resulting file's size) will happily report false success. Symptom actually seen:
`EditorUtility.DisplayDialog`/`Debug.Log` from `LetItGrowBundleBuilder.BuildKit()` printed "Packed 2
meshes + 2 albedo textures... DONE" while the on-disk `letitgrow_kit` file was 0 bytes.

The actual compile failure, reproduced on Unity 6000.0.61f1 with `com.unity.collections` version
`2.6.6` (installed transitively — `com.unity.cloud.gltfast` depends on it for `Unity.Mathematics`/job
system usage) targeting a Release (non-development) `BuildTarget.StandaloneWindows64` build:

```
Library/PackageCache/com.unity.collections@.../Unity.Collections/NativeList.cs(850,24): error CS7036:
There is no argument given that corresponds to the required formal parameter 'safety' of
'NativeArray<T>.ReadOnly.ReadOnly(void*, int, ref AtomicSafetyHandle)'
```

Root cause: `NativeList.AsReadOnly()`/`AsParallelReader()` in that package version branch on
`#if ENABLE_UNITY_COLLECTIONS_CHECKS` and call a 2-arg `NativeArray<T>.ReadOnly` constructor in the
`#else` (checks-off) branch — the branch Release Standalone player builds take by default. Only the
3-arg (checks-on) constructor overload exists in the reference assemblies actually used for this
compile, so the 2-arg call fails to resolve. This is a genuine upstream package/Editor skew, not
anything specific to this workspace's code — the same failure would hit *any* project on this Editor
version building a `StandaloneWindows64` AssetBundle/Player with this collections version, on any OS.
**Correction 2026-08-26**: an earlier version of this note attributed the reference-assembly skew to
"the `windows-mono` support module" — checked directly against `modules.json` on the machine that
actually hit and fixed this, and `windows-mono` was never installed there (`installed: None, selected:
False`) when the successful build ran. See fact 9 below — the module isn't involved at all.

**Fix, proven working**: force `ENABLE_UNITY_COLLECTIONS_CHECKS` onto the `Standalone` scripting-define
group for the duration of the build, via the modern (Unity 2021.2+) API, then restore whatever was
there before:
```csharp
using UnityEditor.Build;
NamedBuildTarget nbt = NamedBuildTarget.Standalone;
string prevDefines = PlayerSettings.GetScriptingDefineSymbols(nbt);
bool hadDefine = prevDefines.Split(';').Contains("ENABLE_UNITY_COLLECTIONS_CHECKS");
if (!hadDefine)
    PlayerSettings.SetScriptingDefineSymbols(nbt, string.IsNullOrEmpty(prevDefines)
        ? "ENABLE_UNITY_COLLECTIONS_CHECKS" : prevDefines + ";ENABLE_UNITY_COLLECTIONS_CHECKS");
try { manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64); }
finally { if (!hadDefine) PlayerSettings.SetScriptingDefineSymbols(nbt, prevDefines); }
```
This forces the branch with the working 3-arg overload. Combine with actually checking the return
value (`manifest == null`) and the output file's size before reporting success — both
`AvalorBundleBuilder.cs` and (now-fixed) `LetItGrowBundleBuilder.cs` should get this treatment if
either is copied forward into a new bundle-builder script.

**Unrelated but adjacent gotcha, same session**: creating a Unity project *inside* a `dotnet`
SDK-style mod project's own directory (e.g. `<mod>/Unity/`) breaks `dotnet build` for the mod —
`Unity/Library/PackageCache/**/*.cs` (Unity's own package sources, thousands of files) get swept into
the mod DLL's compile by the SDK's default recursive glob, producing a wall of CS0246/CS1069 errors
from code that has nothing to do with the mod. Fix: add `<Compile Remove="Unity\**" />` (plus matching
`EmbeddedResource Remove`/`None Remove`) to the mod's `.csproj`. Cheaper to just create the Unity
project as a sibling directory instead, if starting fresh.

## 9. `BuildAssetBundles(..., BuildTarget.StandaloneWindows64)` works with NO Windows player module installed at all — don't chase a module-install failure as the fix for fact 8's CS7036

Verified directly on the machine/Editor from fact 7/8 (Unity 6000.0.61f1, Hub-managed install at
`~/Unity/Hub/Editor/6000.0.61f1`), by reading its `modules.json` and its `PlaybackEngines/
WindowsStandaloneSupport` directory at the moment a genuinely successful `StandaloneWindows64`
AssetBundle build had just completed (confirmed via a distinct `-logFile` from the earlier failed
attempt, `ExitCode: 0` throughout, correct output byte count):
- `modules.json`: `"windows-mono": { "installed": None, "selected": False }` — the module was never
  actually installed.
- `Editor/Data/PlaybackEngines/WindowsStandaloneSupport/` existed but was a genuinely **empty
  directory** (4.0K, zero files inside) — a stub left over from a prior failed/partial install attempt,
  not a working module.
- The build log still showed a full, successful `StandaloneWindows64_CodeGen` script compilation stage
  (Unity.Collections, UnityEngine.UI, Unity.Burst, etc. all compiled clean against that target's
  defines) and produced a correctly-sized bundle.

**Conclusion**: `BuildPipeline.BuildAssetBundles` for a given `BuildTarget` only needs that target's
scripting/CodeGen reference assemblies to exist (shipped with the base Editor install for scripting
purposes) — it does not need the platform's actual Player runtime files that a full Player/`.exe` build
would need, and does not need that platform's Hub module to show as "Installed". **Do not spend time
fighting a Hub-CLI module-install failure (e.g. Hub fetching the wrong OS's package archive) as a
prerequisite for building a `StandaloneWindows64` AssetBundle on Linux** — it's very likely unnecessary.
If a `StandaloneWindows64` AssetBundle build fails with fact 8's `CS7036`, apply fact 8's fix
(`ENABLE_UNITY_COLLECTIONS_CHECKS` forced on during the build) first, regardless of module-install
state. This has NOT been checked for full Player builds (an actual `.exe`) — that case may genuinely
need the module; this fact is scoped to `BuildAssetBundles` only.

---

## Facts added 2026-08-26 from LetItGrow 0.0.8 (server-authoritative farm automation, all live-verified on the LetItGrowDedi testbed)

**10. CORRECTION to the `Pickable.RPC_Pick` direct-call pattern (fact cited above).** Calling
`pickable.RPC_Pick(0L, 0)` directly SILENTLY DOES NOTHING unless the calling machine owns the
pickable's ZDO - its first line is `if (!m_nview.IsOwner() || m_picked) return`. With a player
standing in the area, ownership follows proximity, so most pickables belong to that player's client
and the server's "harvest" was a no-op that still counted in the caller's own bookkeeping. The
correct universal call is `pickable.m_nview.InvokeRPC("RPC_Pick", 0)` - routed to whoever owns it,
executes inline when the caller is the owner (empty server / force-loaded zone). Live-verified both
ways. (`LetItGrow/Farming/ScarecrowController.HarvestPass`.)

**11. Headless piece placement needs NO Player at all (plants at least).** `Player.PlacePiece` is
just `Instantiate + SetCreator + place effect` plus player-specific extras (crafting-station
discovery, PrivateArea setup, attack anim, WearNTear.OnPlaced) that a Plant piece doesn't have. So
`Object.Instantiate(piece.gameObject, pos, rot)` + `piece.GetComponent<Piece>().SetCreator(id)` +
`piece.m_placeEffect.Create(...)` replants on a COMPLETELY EMPTY dedicated server - the old
`Player.GetAllPlayers().FirstOrDefault()` crutch (and its "pauses when server is empty" caveat) is
unnecessary. Live-verified: 8 barley planted with zero players connected.
(`ScarecrowController.PlantCrop`.)

**12. Plants NEVER grow during server-side force-load visits without help - three vanilla gates.**
`Plant.SUpdate` (a) only calls `Grow()` when `m_nview.IsOwner()` - a crop planted by a player still
belongs to that player's machine after they log off, so the server may health-check it but never
grow it; (b) throttles to one check per 10s; (c) refuses to grow for the first 10s after the
component spawns (`time - m_spawnTime > 10f`). All three race a typical away-tending dwell of ~15s.
Live-verified: 8 mature-by-world-clock barley sat as saplings through 17 keeper visits;
`SlowUpdater` itself DOES run on a dedicated server (UpdateHealth ticked ~100 times in 2 min).
Deterministic fix: during the automation tick, claim the plant (`view.ClaimOwnership()`, act next
tick), call `plant.UpdateHealth(plant.TimeSincePlanted())` then `plant.Grow()` when overdue - Grow
returns the grown GameObject so the same tick can harvest it. (`ScarecrowController.GrowthPass`.)

**13. TerrainComp force-load race: cultivation reads as bare dirt, and the broken compiler is
INVISIBLE to vanilla's own registry.** When a zone is force-loaded, `TerrainComp.Awake` can run
before its `Heightmap` exists; it then returns early - BEFORE `s_instances.Add`, before RPC
registration, before `Initialize()` - and nothing ever retries, so `Heightmap.IsCultivated()` reads
the unmodified terrain for the whole visit (observed live: 933/933 cultivated cells rejected as
"not cultivated"). `TerrainComp.FindTerrainCompiler`/`s_instances` cannot find these broken ones -
search with `Object.FindObjectsByType<TerrainComp>(FindObjectsSortMode.None)` and, for any with
`!m_initialized` whose heightmap is NOW findable, simply call `comp.Awake()` again: every step it
reached the first time was skipped by the early return, so nothing doubles up. The race recurs on
every force-load of the zone; repair each visit. This is the same root cause behind AwayFromHome's
suppressed `TerrainComp.Load` null-derefs. Also: `ZNetScene.IsAreaReady(pos)` is necessary but NOT
sufficient for terrain-dependent scans - it proves ZDOs spawned, not that terrain ops applied.
(`ScarecrowController.RepairAndCheckTerrain`.)

**14. Standalone IMGUI panels: the click-passthrough item-throwing trap and the modal fix.** An
OnGUI panel is unusable on its own in Valheim (cursor stays camera-locked unless a VANILLA window is
visible), so panels silently train users to open their inventory underneath - and IMGUI clicks fall
straight through to the uGUI inventory stacked below, grabbing items and dropping them on the
ground ("using the menu throws my items out"). Modal fix, three patches + one rule:
postfix `GameCamera.UpdateMouseCapture` (free cursor while panel open), postfix `Player.TakeInput`
and `PlayerController.TakeInput` (`__result &= !panelOpen`), and close the panel the INSTANT any
vanilla UI opens - never coexist with `InventoryGui`. (`LetItGrow/Patches/GuiInputPatches.cs`.)

**15. `Inventory.HaveEmptySlot()` is the wrong gate for deposits.** It refuses a deposit when the
only free capacity is room on an existing stack. Use `inv.CanAddItem(item, item.m_stack)` (stack-
aware) before `AddItem`. Caused "harvested crops left on the ground" alongside the pick-ownership
bug in fact 10.

**16. Per-piece settings on a server-authoritative object: full-state routed-RPC pattern (ported
from AwayFromHome/SiteRegistry).** Clients must never write the ZDO (non-owner writes don't
replicate and the server overwrites them). Client sends ONE ZPackage with the complete desired
settings block; the server validates sender (piece creator via `ZDOVars.s_creator` matched through
`peer.m_characterID` -> character ZDO's `s_playerID`, or adminlist via
`ZNet.IsAdmin(peer.m_socket.GetHostName())`, host always authorized), re-clamps every field against
its own synced config, `zdo.SetOwner(ZDOMan.GetSessionID())` then writes, and replies with a toast
RPC (reply handler must verify the sender IS the server). Full-state beats per-field deltas: a lost
packet can't half-apply. (`LetItGrow/Farming/ScarecrowSettingsRpc.cs`.)

**17. Never copy a plugin DLL over a RUNNING server's file.** Mono memory-maps assemblies and JITs
methods lazily from disk; replacing the file under a live process throws
`BadImageFormatException: Method has zero rva` when a not-yet-JITted method is first called. Stop
the server, then copy, then start. (SIGTERM saves the world; a post-save mono teardown segfault
(exit 139) is cosmetic - verify the `worlds_local` mtimes if unsure.)

**18. Vanilla crop-death rules worth pre-checking before ANY automated planting** (they all end in
`m_destroyIfCantGrow` silently deleting the plant at grow time, i.e. burned seeds): biome
(`plant.m_biome & heightmap.GetBiome(pos)`), roof (`Physics.Raycast(pos, up, 100f,` mask
`Default|static_solid|piece`)), heat/cold (AshLands / Mountain+DeepNorth without
`ShieldGenerator.IsInsideShield`), and MUTUAL grow-space - vanilla evaluates each plant's own
`m_growRadius` sphere against every OTHER collider, so legal spacing is
`growRadius + neighborColliderExtent`, in BOTH directions: a new plant must not sit inside an
existing healthy plant's sphere either. Measure the collider extent off the prefab's collider
shapes; a fixed margin guess is how a whole field withers. (`ScarecrowController.Rescan`,
`HasGrowSpace`, `WouldCrowdNeighbor`, `GetColliderExtent`.)

**19. The world clock is FROZEN on an empty dedicated server - nothing measured against it can
ever happen while nobody is online.** `ZNet.UpdateNetTime` only advances `m_netTime` when
`GetNrOfPlayers() > 0` (client/singleplayer branches always advance). Everything that measures
elapsed time via `ZNet.GetTime()`/`GetTimeSeconds()` therefore pauses the moment the last player
logs out: plant age (`Plant.TimeSincePlanted` - so a crop planted during a 0-player away-tend
visit stays seconds old FOREVER and no growth gate, vanilla or modded, can ever pass), pickable
respawns, smelter/fermenter catch-up, day/night. Observed live: LetItGrow's growth probe showed
`sincePlanted` pinned at 4s across real hours and multiple restarts while ticks, ownership, and
health checks all ran fine - the maturity comparison was the failing gate, and the clock was why.
Real-frame-time machinery (`InvokeRepeating`, coroutines, `Time.time`) keeps running regardless,
which makes the freeze easy to miss: the automation LOOKS alive. Fix pattern: postfix
`ZNet.UpdateNetTime` and add `dt` in exactly the one skipped case (`IsServer() &&
GetNrOfPlayers() == 0`), config-gated; `m_netTime` is part of the world save, so credited time
persists. Any away-automation mod that plants/ages/ferments on an empty server needs this or a
per-object time-credit equivalent. (`LetItGrow/Patches/WorldClockPatches.cs`, "Time Flows While
Empty".)

**20. ZDO ownership assignment is CENTRALIZED on the server, and near a player it is a 2-second
treadmill.** `ZDOMan.Update` runs `ReleaseZDOS` only when `IsServer()`; every 2s it hands each ZDO
near a player to that player's client unless the current owner's active area covers the sector -
and a dedicated server's own reference position never covers anything, so a server-side claim on a
watched object is re-stolen within 2s of EVERY reclaim. A claim-then-act-next-tick loop (tick >=
2s) can lose the race forever: observed live as a farm whose automation went silent for exactly
the minutes a player stood at it. Fixes by object class: (a) prefab-scoped `ZDO.SetOwner` prefix
pinning server ownership, for objects whose ZDO the server must write (`LetItGrow/Patches/
ZdoOwnershipPatches.cs`); (b) claim-and-act in the SAME tick for transient objects like drops
(`ClaimOwnership` is locally instant); (c) **NEVER pin a Container** - vanilla's open handshake
ends in `GetZDO().SetOwner(opener)` inside `Container.RPC_RequestOpen`, so a pinned chest is
granted-but-never-owned and simply refuses to open (live-verified both ways). Containers get
claim-at-write-time, skipping any whose ZDO `ZDOVars.s_inUse` reads 1 (readable without
ownership).

**21. A dedicated server NEVER instantiates the world around its peers - server-only mod logic
does not run just because a player is standing there.** The nearby client simulates the zone
(vanilla's model); the server's `ZNetScene` creates objects only around its own reference
position and whatever a mod force-loads. So a server-authoritative MonoBehaviour (clients stand
down by design) only exists during force-load visits - "a player is at the site" is precisely
when it must KEEP force-loading, not when it can rest. Pattern: the rotation keeper PARKS while
any peer is within range of the site (`LetItGrow/Farming/ScarecrowKeeper.HoldSite`, 64m, capped
per stay). Vanilla components (livestock, smelters) don't need this - the client runs them.

**22. Never bake a ServerSync'd value into anything at registration time.** Piece registration
(ObjectDB/ZNetScene Awake) runs before a joining client has RECEIVED the server's config, so each
machine bakes its own local file's value - live-verified as a client rendering a 24-slot grid
over a silo the server ran at 64 slots: the automation tidied produce into slots the client never
drew, indistinguishable from item deletion. Apply synced values per-instance in `Start()` (after
Awake order settles) and re-apply on the entry's `SourceConfig.SettingChanged` (fires when the
server's value lands mid-join). Inventory resize is safe live (`m_width`/`m_height` + `Changed()`)
if you never shrink below an occupied `m_gridPos`. (`LetItGrow/Farming/FarmSilo.cs`.)

**23. A custom IMGUI panel's cursor over game-streaming: SKIP vanilla's lock, don't undo it.**
`GameCamera.UpdateMouseCapture` locks the cursor every frame no vanilla UI is open; unlocking
after it (postfix) leaves a within-frame Locked->None flip-flop that a streaming host (virtual
gamepad remote-play setups) can latch, keeping the pointer captured - reported as "mouse stuck at
screen center", reproduced with the postfix-only fix in place. Prefix-skip the method entirely
while the panel is open, set `lockState = None` + `visible = true` UNCONDITIONALLY (vanilla's
`visible = ZInput.IsMouseActive()` idiom leaks gamepad-mode's hidden cursor into the panel), and
re-assert from `OnGUI` so the frame's last writer wins against other mods. Diagnose remotely with
a 1Hz probe logging `Cursor.lockState` + `Event.current.mousePosition` - if guiMouse tracks the
physical mouse, events reach the game and any remaining pin is the streaming layer's.
(`LetItGrow/Patches/GuiInputPatches.cs`.)

**24. A `Container`'s `Inventory` is a CACHE - claim-then-write through it DESTROYS other
people's deposits (the 70-Barley lesson, LetItGrow 0.1.1, 2026-08-27).** `GetInventory()` does
NOT refresh from the ZDO; vanilla reloads only on its own paths (the open handshake, the owner's
update tick) by comparing `m_lastRevision` against `zdo.DataRevision` and calling `Load()`. So
fact 20's claim-at-write-time discipline has a mandatory second half: **reload before you write.**
A player deposits into a chest (their client owns it while browsing and saves the new "items"
payload), automation later claims the chest and calls `AddItem`/`RemoveItem` on its own stale
in-memory copy - `Changed()` → owner save → the ZDO now holds the PRE-deposit contents and the
player's items are gone without any error. Symptoms in the field: freshly deposited stacks vanish
after the next automation write, and UI that counts stock server-side (crop pickers) doesn't see
deposits at all. The guard, before EVERY read or write of a container the mod doesn't exclusively
own: `if (container.m_lastRevision != view.GetZDO().DataRevision) container.Load();` (publicized
private members; this is exactly vanilla's own `CheckForChanges` gate, free when nothing moved).
Corollary the same bug hid: **an unclaimed debit doesn't replicate** - `RemoveItem` without
ownership lives only in local RAM and the items resurrect on the next reload (silent duping while
a player stood at the farm watching seeds get "consumed"). Every mutation needs the full ritual:
reload → skip if `s_inUse == 1` → claim → mutate. (`LetItGrow/Farming/ScarecrowController.cs`
`FreshInventory`/`RemoveFromSilos`/`DepositIntoSilos`, plus the same pattern client-side in
`GridPlacementController.Confirm`'s chest seed-source.)

**25. NEVER call a Unity lifecycle method (`Awake`/`Start`) by hand on a Harmony-patched
component - you re-run every OTHER mod's hooks on it (LetItGrow 0.1.2, live Wonderland log,
2026-09-01).** `TerrainComp.Awake` early-returns when its `Heightmap` isn't there yet (the
force-load race of fact 21's world), and LetItGrow's repair "just called `comp.Awake()` again".
That also re-ran HearthBelow's `TerrainComp.Awake` postfix, which does `ZNetView.Register(...)` -
`m_functions.Add` on a `Dictionary<int,...>` - and the SECOND registration throws
`ArgumentException: An item with the same key has already been added. Key: <rpc hash>` out of the
patched method into the caller: 2,970 aborted Scarecrow ticks in one day, one lost tick per raced
compiler per visit, on a server that had run "clean" for weeks on the testbed (which has no
HearthBelow). A Harmony-wrapped method is not a function you can re-enter; it is a pipeline of
third-party side effects. Replicate the vanilla body with publicized members instead (here:
`m_hmap = hmap; s_instances.Add; Register("ApplyOperation") guarded by
`m_nview.m_functions.ContainsKey(name.GetStableHashCode())`; `Initialize(); CheckLoad();`), and
from a repair path never perform vanilla's destructive branch (`Found another terrain compiler,
removing it` → `ZNetScene.Destroy`) - leave a duplicate inert. (`ScarecrowController.RepairTerrainComp`.)

**26. `ZNetScene.OutsideActiveArea` gates exactly three things - `WearNTear.UpdateWear`,
`SpawnArea`, `StaticPhysics` - and NOT monster AI. So a creature your claim pass owns at an
anchored site runs its full `MonsterAI` for the length of the visit.** (Decompile-verified,
2026-09-01: those are the only call sites.) A dedicated server's reference position is
`(1e6, 0, 1e6)` (`Game.FixedUpdate`, unconditional in the server build), so server-owned pieces
never structurally collapse and server-owned nests never spawn - but a server-owned greydwarf
hunts, paths and swings. `MonsterAI.UpdateAI` attacks a priority static target with no player
around at all, and random pieces within 10 m once alerted at a player. A blanket "claim every
persistent ZDO in the ring" (fact 20's discipline, as AwayFromHome and LetItGrow ≤0.1.1 both did)
therefore turns every keeper visit into a short live-AI window at the farm, and a Farm Silo is a
100-HP `piece_chest_wood` clone. Claim what the job needs - pieces, `Pickable`/`Plant` (growth is
owner-run), `ItemDrop` (`ZNetScene.Destroy` only deletes the ZDO when you own it) - and skip any
prefab carrying `Character` (`ZNetScene.GetPrefab(zdo.GetPrefab()).GetComponent<Character>()`,
cached per hash). Unowned creatures stay exactly as vanilla leaves them where no player is: frozen.
(`LetItGrow/Farming/OwnershipClaim.IsCreature`; AwayFromHome's own pass still claims everything -
its call, since livestock simulation IS its job.)

**27. `WearNTear.Destroy(HitData, bool)` is the single funnel every piece death goes through,
and vanilla logs nothing there - so a "my chest exploded at random" report is unanswerable
without a prefix on it.** Creature hits, player hits, structural collapse
(`!HaveSupport()` → 100% damage in `UpdateWear`), hammer removal (`RPC_Remove`) and scripted
removal all end in it, always on the piece's OWNER (server during away-tending, the nearby
player's client otherwise - so log on both sides). The two fields that separate the causes:
`hitData` (null → collapse or removal; else `GetAttacker()?.m_name`, `GetTotalDamage()`,
`m_hitType`) and `GetSupport()` vs `GetMinSupport()` (below → collapse). Add
`Heightmap.FindHeightmap(pos) != null` and `Heightmap.HaveQueuedRebuild(pos, 64f)` and you can tell
"greydwarf" from "ground wasn't there yet" from one line. Patch by explicit signature
(`typeof(HitData), typeof(bool)`) - `Destroy` collides with `UnityEngine.Object.Destroy` by name.
(`LetItGrow/Patches/PieceDestroyDiagnostics.cs`, 2026-09-01.)

**28. Corollary to fact 17, client side: never copy a DLL into a Gale profile while its client is
launching.** Mono maps the assembly lazily; a rewrite mid-load surfaces as
`ExecutionEngineException: String conversion error: Illegal byte sequence` and
`InvalidProgramException: Invalid IL code in <garbage method name>` inside unrelated patched
methods (`ZNet.Awake`, `ZNetScene.Awake`), then a SIGABRT core dump - looking exactly like a
corrupt build. It isn't; the on-disk file is fine. (2026-09-01: a LetItGrow 0.1.2 copy landed
70 s into another session's Test-icles client launch. On a shared rig, ping the other session
before writing to any profile it may be loading.)

