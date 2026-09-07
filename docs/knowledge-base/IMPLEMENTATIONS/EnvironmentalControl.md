# Environmental Control

**The client-side "presence layer" — sky, weather, sun, grass, billboards — and why loading a zone
does not bring any of it with you.**

Companion to [ZoneAnchor.md](ZoneAnchor.md). The Zone Anchor answers *"how do I make world exist
where no player is standing?"*. This document answers the question that turns up immediately
afterwards: *"why does the anchored place look empty even though everything loaded correctly?"*

Verified against the live Valheim build as of **August 2026** from
`DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`. Line numbers are that file. Re-verify
against a current decompile before trusting a new game patch.

---

## The finding, in one line

**It is not a loading problem, and no amount of extra zone loading will fix it.** Sky, weather,
sun, ambient light, cloud domes, ash/rain/snow particles, grass, and every billboard are *not zone
contents*. They are a single global kit that Valheim builds **once**, keys to the **local player's
camera**, and teleports there every `LateUpdate`. Anchor a camera five kilometres away and it
photographs real terrain and real objects under **somebody else's sky**.

Symptom that led here: a live camera standing in the Ashlands showed ground and trees correctly,
and no ash rain, no Ashlands sky, no lava glow, and no foliage other than trees.

---

## Two ledgers

Everything you can see in Valheim falls into exactly one of these. Deciding which column a thing is
in tells you immediately whether the anchor can deliver it.

### Ledger A — genuinely per-zone. The anchor already delivers these.

| Thing | Where it actually lives |
|---|---|
| Terrain / heightmap | `ZoneSystem` builds it per zone |
| **Ashlands lava** | A **channel of the heightmap**, not an object — `hmap.GetLava(p)` (`:7594`). Terrain data, so it arrives with the terrain |
| Trees, rocks, buildings, items | ZDO-backed GameObjects, instantiated by `ZNetScene` |
| **Water** | `WaterVolume` (`:128932`) is a **per-zone component** holding its own `m_heightmap`; it self-registers into a static `Instances` list in `OnEnable`. `StaticUpdate` only pushes global wind and a time value — **no camera term anywhere in the class** |
| Distant horizon | `TerrainLod` — *keys to the main camera* (`NeedsRebuild`, `:96356`), which is why the anchor builds its own second rig |

Water and lava being in this column is worth stressing: **if they are missing at the anchor, that is
a zone-loading bug to chase, not a presence-layer problem.** They are the two things in the original
symptom list that *should* already work.

### Ledger B — one global instance, welded to the local player. Never comes from loading more zone.

| System | Line | What it keys off | What is missing at the anchor |
|---|---|---|---|
| `EnvMan.GetBiome()` | `:81762` | `Utils.GetMainCamera().transform.position` | The whole environment choice — the anchor gets the *player's* biome |
| `EnvMan.UpdateEnvironment()` | `:81690`, camera read at `:81701` | same | Weather selection, Ashlands/DeepNorth special-casing |
| `EnvMan.SetEnv()` | `:81876` | same | Fog colour/density, ambient light, skybox, cloud tint |
| `EnvMan.m_dirLight` | positioned `:81891` | `mainCamera.transform.position - forward * 3000f` | **The sun itself.** One directional light, parked over the player |
| `EnvMan.m_psystems` | `:81939-81949` | enabled/disabled globally; gated on `Player.m_localPlayer.InShelter()` | **Ash rain, rain, snow, embers** — one set, enabled or not, for the whole client |
| `EnvMan.m_clouds` / `m_rainClouds` / `m_rainCloudsDownside` | fields | mesh renderers on the shared rig | The sky dome |
| `FollowPlayer` | `:30112` | `LateUpdate` snaps `transform.position` to the main camera or the player | **The transport for all of the above.** See below |
| `Billboard` | `:101299` | `LateUpdate` does `LookAt(mainCamera.transform.position)` | Every billboarded sprite is edge-on to a second camera — i.e. **invisible** |
| `ClutterSystem` | `:102340`, `LateUpdate` `:102489` | `Player.m_localPlayer.transform.position` (or main camera in free-fly) | **Grass and small foliage**, radius `m_distance = 40f` |
| `ClutterSystem.IsHeightmapReady()` | `:102540` | `Utils.GetMainCamera().transform.position` | A *separate* camera key from the centre — see the gotcha below |

### `FollowPlayer` is the whole mechanism

```csharp
// :30112 — abridged
public class FollowPlayer : MonoBehaviour {
    public enum Type { Player, Camera, Average }
    public Type m_follow = Type.Camera;
    private void LateUpdate() {
        Camera mainCamera = Utils.GetMainCamera();
        if (!(Player.m_localPlayer == null) && !(mainCamera == null)) {
            ...
            base.transform.position = zero;   // teleported to the player/camera, every frame
        }
    }
}
```

Scene-authored — attached in the Unity scene, so nothing in the decompile *references* it and grep
will not lead you to it from the systems it moves. It is the reason the sky, the clouds and the
weather particles are permanently on top of the player: they are ordinary GameObjects being dragged
there sixty times a second.

Two consequences worth writing down:

- It early-outs entirely when `Player.m_localPlayer == null`. Nothing in the presence layer moves
  without a local player.
- Because these are *world-space* objects, a second copy placed at a distant anchor is **invisible to
  the player** — they are kilometres away. That is what makes strategy 3 below safe.

---

## Three strategies, cheapest first

### 1. Poke — for anything with a TTL

`ClutterSystem` turns out to be built exactly like `ZoneSystem`, and this is the useful discovery of
the whole investigation. Grass is **not** removed by a distance test against a single centre.
`GeneratePatches(center)` *adds* patches near a point and resets their timers (`:102562`);
`TimeoutPatches(dt)` (`:102619`) removes any patch whose timer passes **2 seconds**, with no
reference to any camera at all.

So `GeneratePatches(false, anchorPos)` is a **poke primitive for grass**, precisely analogous to
`ZoneSystem.PokeLocalZone`:

- **Purely additive.** The player's own patches keep having their timers reset by vanilla's call; an
  extra call at the anchor adds a second field without taking the first away.
- **Self-cleaning.** Stop poking and the anchored grass expires on its own in 2 s — the same
  fail-safe shape as the zone anchor's 4 s TTL.
- **Paced.** `GeneratePatch` builds at most one new patch per call (`generated` flag, `:102596`), so
  a 40 m field is ~81 patches ≈ 81 frames ≈ 1.4 s. No hitch, and it lands well inside an anchor's
  existing settle budget.

> **Gotcha.** `LateUpdate` gates on `IsHeightmapReady()` (`:102540`), which tests
> `Heightmap.HaveQueuedRebuild` at the **main camera**, not at the centre it is about to use. Calling
> `GeneratePatches` directly side-steps that check — so test the anchor's own heightmap readiness
> yourself first, or grass will generate against terrain that is still rebuilding.

Requires publicizing (`GeneratePatches` is private). `BepInEx.AssemblyPublicizer.MSBuild`, same as
the Zone Anchor.

### 2. Per-camera swap — for global render state

Unity's built-in pipeline (Valheim is BiRP — confirmed by its Post Processing Stack **v1**) fires
`Camera.onPreRender` / `Camera.onPostRender` around **each** camera's render. Global state read at
render time can therefore be swapped for one camera and restored, with the player's view untouched:

- `RenderSettings.fogColor`, `fogDensity`, `ambientLight`, `skybox`
- `EnvMan.m_dirLight` rotation, colour, intensity — the sun
- `Shader.SetGlobalVector` values

Apply the destination's `EnvSetup` in `onPreRender`, restore in `onPostRender`. Correct, cheap, and
it never flickers the player's screen.

**Does not work for particles.** A particle system has simulated history; teleporting it for one
render gives you a smear or nothing at all. Particles need strategy 3.

### 3. Own copy — for anything simulated

Instantiate the destination environment's `m_psystems` (and, if wanted, the cloud meshes) as children
of the anchored camera. They simulate continuously and correctly in place. Safe because the anchor is
far from the player, so the duplicates are out of the player's sight — **the one case to guard is an
anchor close enough to overlap the player's own area**, which the Zone Anchor already detects for a
different reason (`SnapshotAnchor.ZNetScene_CreateObjects_Prefix`).

`Billboard` is the awkward one: it is a per-object component, it uses the main camera, and a quad
cannot face two cameras at once. Options are to accept it, or to swap `Billboard` orientation inside
the `onPreRender` window of strategy 2 (`LateUpdate` has already run by then, so a bulk re-aim is
possible but touches every billboard in the scene).

**Two more things belong in strategy 3 than the first pass caught**, both found by reading `SetEnv`
line by line against the transcription rather than trusting it:

- **`EnvSetup.m_envObject`.** Vanilla owns exactly one and merely toggles it active — it is the
  thing that makes an Ashlands sky an *Ashlands* sky rather than a grey one. It stands wherever the
  player is, so an anchor needs its own clone.
- **`m_rainCloudAlpha`**, written by `SetEnv` as `m_clouds.material.SetFloat(s_rain, ...)`. Not a
  global: it lives on the cloud dome's own material, so a cloned dome silently keeps the weight of
  the *player's* weather. Drive it with a `MaterialPropertyBlock` on the clone rather than
  `.material`, which instantiates a material you then have to own and destroy.

`m_psystemsOutsideOnly` should be deliberately **ignored**: it tests whether the *player* is under a
roof, which says nothing about the place being looked at.

---

## ⚠️ Two traps that cost a full playtest each

**1. Resolve-on-attach latches OFF.** The natural place to resolve the destination's `EnvSetup` is
when the anchor goes up. That is *before the far heightmap exists*, so it legitimately returns null —
and if the per-frame retry sits behind an `if (_env == null) return;` early-out, it can never reach
itself. The result is a swap that never runs once, silently, with nothing in the log, on every shot
for the whole session. Resolve on the **settle tick** instead, which is also where it belongs: the
first successful resolve is what builds the weather copy, and the copy needs those seconds to fill.

**2. A camera that renders CONTINUOUSLY is not the same camera.** Post-processing's **eye
adaptation** is history-driven auto-exposure. A camera fired once for a photograph never adapts; one
that keeps rendering ramps over roughly a second and bleaches a correct image in front of the
viewer. If a live view and a still of the same place disagree, suspect the effects that carry
history — eye adaptation and motion blur — before suspecting the environment. Give the continuous
camera its **own copy** of the profile (`Instantiate`) with those disabled; the profile is a shared
asset, so editing it in place turns them off for the player too. The copy is a runtime asset and
must be destroyed by hand.

**Corollary, learned expensively:** a silent success and a silent no-op are indistinguishable from
outside the game. When a rendering feature "does not work", spend the round-trip on **one log line
proving whether the code ran at all** before spending it on another guess.

---

## What is verified and what is not

**Verified in the decompile:** every line number above; `WaterVolume` has no camera dependency;
lava is a heightmap channel; `ClutterSystem` removal is a pure 2 s TTL; `FollowPlayer` and
`Billboard` bodies; `EnvMan`'s four separate main-camera reads.

**Confirmed in play (TortalPortal 1.3.0):** the per-camera swap works. Log line from the dedicated
rig — `anchored sky applied at (432,-4860): env 'DeepForest Mist', biome BlackForest, weather
objects 5`, and the same for `LightRain`/Plains — correct biome, correct seeded environment, the
cloned kit standing, no errors, and no disturbance to the player's own view. The strategy is sound;
both traps above were in the plumbing around it.

**Inferred, not yet confirmed:** that `EnvMan.m_psystems` instances carry `FollowPlayer` components.
The mechanism is scene-authored, so it cannot be read out of the decompile — confirm in-game with a
hierarchy dump before building on it.

**Unexplained:** a faint **shimmer** in a continuously-rendered anchored view. It survived making the
live path byte-identical to the photograph path (`Camera.Render()` → `ReadPixels` → `Texture2D`), so
it is not a second code path and not colour-space. Untested suspects: the anchor's own pinned
`TerrainLod` z-fighting against real terrain where the two carpets overlap, and temporal
anti-aliasing on a camera with no motion history. Start there.

**Not investigated:** `Shader.SetGlobalVector(s_netRefPos, ZNet.instance.GetReferencePosition())`
(`EnvMan.Update`, `:81456`). A global shader value carrying the *player's* reference position. If any
shader uses it for distance fade or dithering, it would independently suppress or fade geometry at an
anchor, and that would look exactly like "loaded but not drawn". **Check this first if something in
Ledger A is missing** — it is the only known mechanism that could make correctly-loaded water or
lava fail to appear.

`RenderGroupSystem` (`:92750`, enum `:92744` — `Always` / `Overworld` / `Interior`) was checked and
cleared: it is interior/exterior gating, not a distance or camera concern.

---

## Origin

Found while building TortalPortal's live portal preview (v1.2.0 anchor, v1.3.x camera work). The
anchor was correct the whole time; the assumption that "the zone" included the sky was the error.
