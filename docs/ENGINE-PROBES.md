# The boot-time engine probes

**P10b.** P10a tells *us* what moved, offline. P10b makes the **running mod** notice and behave, so a
player who updates Valheim before we update the mod gets a mod that says what is wrong and turns off
the part that broke, instead of one that throws every frame or — worse — quietly does the wrong
thing. The brief is `docs/P10-P11-FOR-DON.md` §P10b; the build every fact here is dated against is
`docs/ENGINE-BASELINE.md` (P10a).

Three files, and one console verb:

| file | what it is |
|---|---|
| `ValkyriesCargo/Core/EngineBaseline.cs` | PURE. The build this DLL was compiled against, as constants, and the comparison against the four numbers actually running. |
| `ValkyriesCargo/Core/EngineProbes.cs` | PURE. The registry: 19 named engine facts, their risk rank, what each looks at, what turns itself off when it fails, and what became of it. |
| `ValkyriesCargo/EngineCheck.cs` | The only file that touches a game type. Reads the live version numbers and resolves every fact, once, from plugin `Awake`. |
| `cargo engine` | Prints all of it. `cargo status` carries the one-line version of it, second from the top. |

---

## 1. Say what you were built for, and what you found

`Version.CurrentVersion`, `m_networkVersion`, `m_playerVersion`, `m_worldVersion` — read at boot,
compared against the compiled-in baseline, one line in the log either way. **A mismatch is
information, not an error.** Nothing here refuses to load.

The four numbers are **copied** into `EngineBaseline` rather than referenced, and both halves of that
are load bearing:

- Vanilla's `Version` type is `internal`. Naming it is exactly what house rule 5 forbids: it compiles
  against the publicized copy and is a runtime coin toss.
- The three version numbers are `const`. A direct reference is inlined **at our compile time**, so
  the comparison would read our own baseline back to itself and answer "same build" on every Valheim
  ever released. `ReadVersions` uses reflection over the loaded assembly's metadata, which is the
  only place the live numbers exist.

`Compare` handles every shape `GameVersion.ToString()` can take: `0.221.12`, `0.221` (a dropped zero
patch), `0.221.rc3` (a release candidate, which vanilla stores as a **negative** patch and which
therefore sorts *below* its own release), and a platform prefix (`dw-0.221.12`). An unreadable
version is its own verdict and is never mistaken for a match.

---

## 2. Probe before you rely

### The shape, and why it is a pair

Every probe is two methods:

```csharp
private static void ProbeX()                       // catches
{
    try { looked = CheckX(bad); }
    catch (Exception ex) { Threw(EngineProbes.X, ex); return; }
    Record(EngineProbes.X, looked, bad);
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static int CheckX(List<string> bad) { /* touches the game type */ }
```

**Mono resolves a member access when the CALLER is JIT-compiled**, so a `try`/`catch` in the same
method as the access never runs — the exception is thrown while the method is being compiled, before
its first instruction executes. This is already a house rule ("reflection resolution in its own
method, never in the method that does the work"); P10b is where it is used deliberately rather than
defensively. The `[MethodImpl(NoInlining)]` is not decoration: an inlined `CheckX` would drag its
`typeof` token back into the catching method and put us exactly where we started.

### The registry is a veto, never a permit

`EngineProbes.Ok(name)` answers **true** for a probe that has not run, for one that could not be
probed, and for a name nobody ever registered. Only a recorded FAILURE is a no. A registry that
failed closed would turn a bug in one file into a mod that does nothing, on every machine, silently.

A probe is recorded **once**. A second `Record` for the same name is refused and reported in
`Problems`, so a feature cannot flip a verdict under itself and `cargo status` cannot disagree with
the log.

### The one named exception to house rule 5

`EngineCheck.cs` is the only file in this mod that names a private game member — by NAME, in a
string, handed to reflection, inside a probe, and **never called**. Four of them:

| member | why the probe has to name it |
|---|---|
| `ZSyncTransform.m_velocityCached` | the flight's whole dead-reckoning argument rests on it existing |
| `Character.RPC_Damage` | what the immortality patch actually patches (see §3) |
| `BaseAI.m_character` | the field whose assignment ORDER cost a shipped visit (see §4) |
| `RandEventSystem.Awake` / `Humanoid.Awake` | Harmony targets named in strings: a rename is a PatchAll failure, not a compile error |

Nothing here reads a value off a live object, writes anything, moves anything, or starts a timer. It
runs once, from plugin `Awake`, **after** the config binds (a probe's answer is printed by
`cargo status`, which reads config) and **before** `Harmony.PatchAll` (a patch must never be waiting
on a probe that has not run).

---

## 3. The probe gap, fixed

`docs/P10B-PROBE-GAP.md` (Wu'barrk's Claude, reading this branch beside P5) found a probe that was
green while the thing it stood for could be broken. `CheckCharacter` asked for:

```csharp
NeedMethod(c, "Damage", new[] { typeof(HitData) }, bad);       // P5's
```

`Character.Damage(HitData)` is **public**, and nothing in this mod depends on it. It is a thin
sender: it runs on the ATTACKER's machine, computes a weak-spot index and calls
`InvokeRPC("RPC_Damage", hit)`. No damage maths happens in it at all. `Patch_Character_RPC_Damage`
patches the **private** `Character.RPC_Damage(long, HitData)` — the victim-side choke point every hit
passes through. If a future build renamed or re-signed `RPC_Damage`, `Damage`'s public signature
could stay exactly as it is: the probe would pass, `cargo engine` would say so, and the merchant
would quietly become mortal.

Fixed:

```csharp
NeedMethod(c, "RPC_Damage", new[] { typeof(long), typeof(HitData) }, bad);
```

`NeedMethod`'s `Anywhere` flags already include `NonPublic`, so the name and the signature are the
whole change. The probe's own description string — which `cargo engine` prints verbatim, and which
told a reader the same wrong thing — is corrected with it, and so is its `Degrades`: it used to say
"nothing today; P5's surface, audited when P5 lands", and P5 has landed. **`Character.Damage` is not
probed at all now**, on the gap doc's own principle: probe the fact the patch depends on, and only
that.

Harness mutation 6 is that exact regression — putting `Damage` back in the description fails two
checks.

---

## 4. The P5 surface, probed

Everything below was verified against the decompiled **real** assembly, not the publicized one:
`scratchpad/decomp/full/assembly_valheim.decompiled.cs`, baseline 0.221.12. Line numbers are that
file's.

### `character` (rank 6) — the three P5 patches' targets

| member | real accessibility | line |
|---|---|---|
| `Character.GetAllCharacters()` | public static | — (used today, by `CargoFlight`) |
| `Character.GetSEMan()` | public | — (used today, by `ComfortReporter`) |
| `Character.InIntro()` | `public virtual bool` | 9738 |
| `Character.RPC_Damage(long, HitData)` | **private** | 8700 |
| `MonsterAI.MakeTame()` | public | 5837 |
| `BaseAI.IsEnemy(Character)` | public | — (P11d's re-read surface) |

`InIntro` is `Patch_Character_InIntro`'s target and is `virtual`, which is why a postfix on it works
at all; `RPC_Damage` is `Patch_Character_RPC_Damage`'s.

### `merchant_awake` (rank 6) — what builds the merchant

| member | real accessibility | line | why |
|---|---|---|---|
| `Humanoid.Awake()` | protected override | 12932 | `Patch_Humanoid_Awake`'s target, named in a string |
| `Humanoid.Start()` | protected override | 12947 | where `GiveDefaultItems()` is *actually* called from, for non-players |
| `Humanoid.GiveDefaultItems()` | public | 12960 | the crossbow `UnequipAllItems` takes back off |
| `BaseAI.Awake()` | protected virtual | 4012 | assigns `m_character` (line 4015) |
| `BaseAI.m_character` | **protected** `Character` | 3942 | the field `MakeTame` dereferences |
| `MonsterAI.Awake()` | protected override | 5758 | the override that has not run yet at postfix time |
| `Character.SetTamed(bool)` | public | 10599 | `MakeTame`'s first line |
| `ZNetView.IsValid()` | public | 70141 | the guard `CargoMerchant` puts in front of `SetTamed` |

### `awake_order` (rank 6) — **not probeable**

The ORDER inside those Awakes is a method body, and no reflection at boot can see it. It is
registered so that `cargo engine` names it and says "not probeable: verified by P10a's sweep",
rather than leaving a reader to assume a pass. The fact, as read:

- `MonsterAI.MakeTame()` opens with `m_character.SetTamed(tamed: true)` — decompiled line 5839.
- `BaseAI.m_character` is assigned in `BaseAI.Awake` — line 4015.
- So `MakeTame()` called from a `Humanoid.Awake` **postfix** throws a `NullReferenceException` out of
  vanilla, because vanilla's own `Awake`s have not all run. That shipped, killed the merchant in his
  own `Awake`, was found on the first integrated in-game visit (2026-09-07) and fixed in PR #22:
  `CargoMerchant.Reassert(fromAwake: true)` skips `MakeTame` and lets the staggered re-assert
  (0.5 s, 1 s, 3 s, then every 5 s) do it once vanilla is up.
- And `Humanoid.Awake` does **not** hand out `m_defaultItems`: `GiveDefaultItems()` is called from
  `Humanoid.Start` for non-players (line 12947), so the postfix runs BEFORE the crossbow arrives, not
  after — which is why the staggered re-apply, not the ordering, is what makes the unequip stick.

This is exactly the class of change P10a's comparative decompile exists to catch: the signature can
stay and the body can move.

### `merchant` (rank 6) — what the merchant does once he is standing

| member | real accessibility | line |
|---|---|---|
| `Character.m_name` | public `string` | 6883 |
| `Character.m_faction` | public `Faction` | 6887 |
| `Character.Faction.Players` | enum member | 6817 |
| `Character.IsOnGround()` | public | 9223 |
| `Humanoid.UnequipAllItems()` | public | 14114 |
| `MonsterAI.SetFollowTarget(GameObject)` | public | 6543 |
| `BaseAI.SetPatrolPoint()` | public | 4072 |
| `BaseAI.m_aggravatable` | public `bool` | 3917 |
| `BaseAI.m_passiveAggresive` | public `bool` (vanilla's spelling) | 3919 |
| `MonsterAI.m_alertRange` | public `float` | 5640 |
| `BaseAI.m_randomMoveRange` | public `float` | 3881 |
| `Player.GetClosestPlayer(Vector3, float)` | public static | 20318 |
| `ZNetScene.FindInstance(ZDOID)` | public | 69590 |
| `ZNetScene.GetPrefab(string)` | public | 69402 |
| `ZDO.GetZDOID(KeyValuePair<int,int>)` | public | 62173 |
| `Chat.SetNpcText(GameObject, Vector3, float, float, string, string, bool)` | public | 34745 |
| `Odin.m_despawn` | public `EffectList` | 116195 |
| `EffectList.Create(Vector3, Quaternion, Transform, float, int)` | public | 29968 |

`Character.Faction.Players` is asked for **by name**, never by value: vanilla's enums are ordered and
a member inserted ahead of it renumbers every one that follows, so the name is the durable half and
the number never was.

`EffectList.Create`'s last three parameters are optional in vanilla and the call site fills them in
at OUR compile time, so a vanilla that drops them is a `MissingMethodException` at the call, not a
recompile — the probe therefore asks for the full five-parameter signature, the same reasoning
`CheckInventory` already used for `Inventory.RemoveItem`.

### One more Harmony target, now probed

`RandEventSystem.Awake()` is **private** (line 91090) and `Patch_RandEventSystem_Awake` names it in a
string. Added to the rank-1 `randevent` probe for the same reason as `RPC_Damage`.

### What the P5 probes do NOT do

They **report**; they do not gate. The gate would have to live inside `Client/CargoMerchant.cs`,
`Patches/Patch_Humanoid_Awake.cs`, `Patch_Character_InIntro.cs` and `Patch_Character_Damage.cs`, and
those are Wu'barrk's files — this branch reads them and does not edit them. A failure here shows up
as a boot `LogWarning` naming the rank, the probe, the members that moved and what is lost, plus a
line in `cargo status` and the full row in `cargo engine`. The honest next step, when somebody owns
those files, is a Harmony `static bool Prepare()` on each patch class that consults its probe:
returning false skips the patch class cleanly instead of letting `PatchAll` throw. That is a
one-method change per patch and it is written down here rather than done here.

Two features **do** gate themselves today, and both are on this side of the split:
`Server/CargoEvent.cs` (rank 1: refuses to register or start the event, once, loudly) and
`Client/BodyLoader.cs` (rank 13: keeps the stand-in).

---

## 5. The donor material, and why it is not a probe

`Client/BodyLoader.cs` dresses Ingvar in a **copy of the stand-in's own material** with our albedo in
it, because a bundle baked in the Editor carries Unity's `Standard` and Valheim lights that from
direct light only. The assembly half of that path is probed, in `body` (rank 13): the `Material`
copy-constructor, `HasProperty`, `SetTexture`, `SetColor`, `DisableKeyword`,
`Material.globalIlluminationFlags`, `Renderer.sharedMaterials` (settable) and `Renderer.sharedMaterial`.

The other half is **not probed, deliberately**. That a prefab named by `Server.BodyPrefab` is in the
scene, that it carries a `SkinnedMeshRenderer`, and that the renderer's `sharedMaterial` is a
`Custom/Creature` material worth copying, are **runtime facts about a loaded world**, not facts about
an assembly:

- `EngineCheck.Run()` executes inside plugin `Awake`. `ZNetScene.instance` is null then, and stays
  null until a world loads. There is nothing to look at.
- The registry has two tiers and neither fits: a probeable fact (reflection at boot) and a
  `NotProbeable` one, whose message is `not probeable: verified by P10a's sweep`. P10a's offline
  decompile sweep reads assemblies; it cannot see a prefab's materials either, so filing this fact
  there would be a false promise, and a probe registry that lies is the exact failure P10b exists to
  prevent.
- Adding a third, world-time tier would mean a second resolution pass on a world-load hook — new
  state, new ordering, and a timer-shaped thing the house rules keep out.

**Verified instead that the code already degrades cleanly**, read end to end on
`BodyLoader.IngvarMaterial()`:

| leg | what happens |
|---|---|
| no `ZNetScene.instance` (no world yet) | `return null` — and it is re-asked, because `_dressed` is only cached on success |
| `Server.BodyPrefab` names nothing in the scene | `return null` |
| the prefab has no `SkinnedMeshRenderer`, or none with a `sharedMaterial` | `return null` |
| anything throws | caught, one `LogWarning` naming the reason, `return null` |

and in `Dress`, a null donor is not an error: the bundle's own material is kept and the albedo is
bound onto it (`_MainTex`) so Ingvar is at least the right colour under direct light. The custom body
still stands up; only the shading is the fallback. That is a supported state, it is logged, and
`cargo body` shows it — which is what a probe would have bought, without a tier that would have to
lie about how it was verified.

---

## 6. Degrade, do not throw — and refuse rather than corrupt

| thing | what a failure does |
|---|---|
| `Server/CargoEvent.cs` | rank-1 `randevent` failed → does not register and does not start the event, warns **once**, `cargo status` prints `visit: DISABLED` and why. Vanilla resolves an event name from each machine's OWN list, so a registration that quietly did nothing looks exactly like one that worked; this is the difference. |
| `Client/BodyLoader.cs` | rank-13 `body` failed → does not open the bundle, keeps the stand-in, `cargo body` and `cargo status` say why. |
| `Server/MarketStore.cs` | a sidecar whose `format` row is **newer than this build reads** is HELD: `Load` answers nothing, `Save` writes nothing (logged once, not every 30 s cadence), and `Quarantine` refuses to rename it however it is called. That file is somebody's market, written by a version that knows rows we do not — renaming it `.corrupt` and starting fresh is the loudest possible way to lose a purse. An *older* or unreadable format still quarantines, exactly as before. |

`Sidecar.PeekFormat` reads only the first `format` row and stops, so the store decides whether it may
touch the file at all before anything parses it. A format line that does not parse is `-1`, which is
corruption and not the future.

---

## 7. The mutation pass

Eleven mutations, one at a time, each reverted before the next; every one must make the harness fail.
`scratchpad/mutate-p10b.js` is the runner (it is scratch tooling and is not in the repo).

| # | mutation | checks failed |
|---|---|---|
| 1 | `Ok()` answers NO for a probe nobody registered | 1 |
| 2 | a second answer for a probe that already failed is taken | 8 |
| 3 | the status line stops counting the not-probeable facts | 3 |
| 4 | the failures are listed in reverse rank order | 2 |
| 5 | a not-probeable fact stops carrying the sweep note | 2 |
| 6 | **the Character probe stands for `Damage` again, the way the gap doc found it** | 2 |
| 7 | the Awake ORDER is registered as a probe that can pass | 5 |
| 8 | a release candidate sorts ABOVE its own release | 1 |
| 9 | the verdict names the player version before the network one | 1 |
| 10 | a format line that does not parse reads as "no format line" | 1 |
| 11 | the format this build reads is treated as newer than itself | 1 |

Every one caught; the tree restored to 1423 passing.

---

## 8. What is NOT proven

**No probe in this package has ever resolved a real member.** The harness is `net8.0` and compiles
the pure `Core` sources against stubs; it can no more load `assembly_valheim` than it can load Unity.
Everything in `EngineCheck.cs` is therefore verified two ways, both offline:

1. against the decompiled real assembly, member by member, with the line numbers in §4;
2. by a throwaway reflection tool built against the same `libs\` the mod compiles against, resolving
   each member with the SAME `BindingFlags` the probes use — every member of the new and changed
   probes came back, with the expected signature, on the first run.

Neither is a boot on a machine with a game under it. **A typo or a wrong overload in a probe shows up
as a FALSE failure that disables a working feature**, which is the one way this package can make
things worse than not having it. `CLAUDE.md`'s verify list, item 24, is that run: on a stock 0.221.12
the boot line must read `probes 15/15 ok, 4 not probeable` with no `FAILED` and no `registry:` line,
and any failure there is a bug in `EngineCheck.cs`, not in Valheim.

Also unproven, and by design: the not-probeable four (`event_clock`, `znetview_awake`,
`server_refpin`, `awake_order`) are method bodies. Nothing at runtime will ever check them. They are
P10a's job, and they are in the registry so that `cargo engine` says so out loud.
