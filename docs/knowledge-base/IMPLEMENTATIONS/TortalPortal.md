# TortalPortal — Technical Report

## Overview

`TortalPortal` (at `c:/WubarrkCODING/TortalPortal`) is a **standalone** Valheim BepInEx/Harmony mod — no Jotunn, no AssetBundle, no dependency on `MistsofAvalor` despite the visually similar rune VFX. It replaces vanilla portal behaviour with a map-backed destination picker, PIN-locked portals, per-portal camera snapshots used as destination previews, a server-published portal index, live map markers you can travel by clicking, and a procedural rune gateway VFX.

**Scope of this file:** this covers the **whole mod** as of **v1.1.2**, revised **2026-08-02**. The 2026-07-30 revision documented the UI layer only; this one adds the rune-VFX generator and its 1.1.x rework, the vanilla-VFX strip/restore and frame tint, the portal index, map integration and click-n-go, favourites, mounted teleport, the security/PIN and filter patches, placement limiting, config migration and the scatter console commands. The one system still deliberately undocumented is the procedural rune *texture* generator (`VFX/TortalRuneTexture.cs`) — it is a verbatim port from Wings of the Valkyrie and is written up there.

Build/deploy shape, for context: `net48`, `BepInEx.AssemblyPublicizer.MSBuild` publicizing `assembly_valheim`, all references via `..\libs-Tools\*.dll` (including `Splatform.dll`, needed only because `Minimap.AddPin` takes an optional `PlatformUserID`), a PostBuild `copy /Y` into `HexiumDist\plugins\`, and a manual copy into the Gale test profile.

---

> ## 📌 v1.2.0 ADDENDUM (2026-08-02, **RELEASED**: tag `v1.2.0`, merged to `main`, folded) — the Zone Anchor
>
> v1.2.0 adds the mod's biggest system since the index: the **zone anchor** — remote snapshots of
> portals nobody visits (walked 4–6 per cycle by the backfill), live destination previews in the
> album (right-drag look, wheel zoom, frame-counted deadman lifecycle), the sector-subscription RPC
> + ghost generation that make both work on dedicated servers, a pinned second `TerrainLod` for
> photograph horizons, and six synced NaN-proof entries (`EnableRemoteSnapshots`,
> `EnableLivePreviews`, `CameraResolution` dropdown, `AnchorZoneRing`, `AnchorSettleBudgetSeconds`,
> `AnchorSettleRadius`).
> New files: `SnapshotAnchor.cs`, `SectorSubscription.cs`, `LivePreview.cs`; plus the anchor pass in
> `PlacementPatch.cs` and `tortal_scatterclear`'s admin-gated server sweep in `ScatterCommand.cs`.
>
> **The technique is written up as its own reusable implementation — read
> [ZoneAnchor.md](ZoneAnchor.md), not this file, for the how-to.** A redistributable copy ships in
> the repo root as `ZONE-ANCHOR-IMPLEMENTATION.md`. Also new in 1.2.0: gold default VFX colour
> (`FFD24C`, brightness `0.35`), JPG q85 snapshots, 10.5 s backfill cadence, the scatter commands
> disclosed as world-seeding tools (README "Seeding a World"), and the `ConfigSync` floor **raised
> to `1.2.0`** — the far pass rides a server RPC older builds never registered, so a mixed table
> plays differently, which is the raise-the-floor rule firing as written. Final regression on the
> dedicated testbed 2026-08-02: 16/16 far photographs, 0 skipped, 5/5 live previews went live.
> Released: tag `v1.2.0` on `release/v1.2.0`, merged to `main` (PR #3), folded as
> `TortalPortal-v1.2.0.zip`; hex web deliberately not run.

> ## 📌 2026-09-05 ADDENDUM — dedicated-server audit of 1.4.6 (during the Wonderland OOM investigation)
>
> Question: does TortalPortal's anchor run on the dedicated server, given it does not take part in the
> AwayFromHome/LetItGrow seal? **No.** `PlacementPatch.InitializeBackgroundSnapshot` returns on
> `ZNet.IsDedicated()` (line ~292), and `SnapshotAnchor.Set` / `AnchorPresence.Set` are only reached from that
> pass and from `LivePreview` (UI-driven); `AnchorPresence` additionally gates on `Player.m_localPlayer`. So the
> `CreateObjects` prefix in `SnapshotAnchor` is dormant server-side and the seal question is moot there.
> Worth knowing for the **client** side: `SnapshotAnchor` still carries every anchor bug the other two mods
> have since fixed — whole ring + `m_activeDistantArea` band fed in one go, no terrain gating, no
> de-duplication — but a client anchoring on its own machine cannot touch server memory.
>
> What DOES run on the server, and its cost:
> - **`SectorSubscription`** (`ZDOMan.CreateSyncList` postfix): only while a client is mid-far-shot or
>   viewing a live preview; renewal-driven, expires after 15 s; ghost-generates ungenerated zones (permanent
>   vegetation/location ZDOs, same as vanilla around a peer) and appends the 5x5 block + distant band to
>   that peer's sync list. No `ZNetScene` instantiation, so no churn; bounded.
> - **`PortalRegistry.ServerIndexRoutine`**: `GetAllPortals()` every `PortalIndexRefreshSeconds` (live: **10 s**)
>   = three synchronous `GetAllZDOsWithPrefabIterative` walks of the entire ZDO index (~500k ZDOs on
>   Wonderland) plus a fresh `ZPackage`/string encode each time. Allocation churn and CPU, not retention.
>   **Recommend 10 → 60 on a large world**; the index is only republished when the blob changes anyway.
>   `PortalExport` (60 s) does one more walk.
> - **`PortalBehaviorPatch.TeleportWorld_Awake_Postfix`** builds the full procedural gateway
>   (`TortalPortalRuneVFX`: four `ParticleSystem`s and a dozen child objects, `Update` computing player
>   proximity every frame) on the **dedicated server too** — there is no headless gate. A portal inside a
>   keeper ring gets this built and torn down every visit. `OnDestroy → Teardown` frees it, and the
>   texture/material caches are static and bounded, so it is wasted work rather than a leak. **Recommend
>   an `IsDedicated()` early-return in `ApplyVFXMode`** (nothing on a server can see it).
> - **`TraderRepair.Trader_Start_Postfix`**: `Watched.Add(trader)`, and `WatchRoutine` only does
>   `Watched.Remove(trader)` when `trader != null` at the end — a trader destroyed inside the 30 s watch
>   (which is exactly what happens to one standing in an anchored ring when the visit ends) is never removed,
>   so `Watched` keeps the dead component forever. Tiny (one managed wrapper per trader instantiation,
>   traders are rare), but it is unbounded and the same pattern is copied into AwayFromHome's
>   `TraderRepair.cs`. Fix: `Watched.Remove(trader)` unconditionally (a fake-null key still hashes by
>   reference), or sweep null keys.
> - `SnapGuard` purges dead keys on every pass; `PlayerNames` harvest is peer-driven; `AnchorPresence` and
>   `LivePreview` never run headless. None of these is a candidate for the ~1 GB/hour growth — that was the
>   anchor flip-flop in AwayFromHome/LetItGrow (see [ZoneAnchor.md](ZoneAnchor.md), addendum 2026-09-05).

> ## 📌 v1.3.0 ADDENDUM (2026-08-03, **on `test/v1.2.0`, UNCOMMITTED**) — icon library, the anchor's missing sky, and presence
>
> **Squashed.** This was built as 1.3.0 → 1.4.2 across a long iteration on the far-camera, then
> collapsed back to a **single 1.3.0** on the user's call: too many real fixes to keep chasing the
> camera through point releases. One version, one changelog entry, one fold. `ConfigSync` floor
> deliberately **stays `1.2.0`** throughout — the new synced values are append-only and an older
> client ignores them and falls back to its own marker, so a mixed table looks worse, not different.
>
> - **v1.3.0 — the icon library.** `TortalPinLibrary.cs`: an admin drops PNGs into the server's
>   `TortalPortalPins\`, a `CustomSyncedValue<string>` catalogue (`pincatalogue`) advertises them at
>   login, and each client fetches only what it lacks over a chunked `ZRoutedRpc` pair
>   (`TortalPortal_PinFetch` / `TortalPortal_PinChunk`, 16 KB per frame), content-addressed into
>   `pincache\<hash>.png`. **A free upgrade for players from a dedicated server pack** — no client
>   install. Server never decodes an image (headless, untrusted input): it validates by parsing the
>   PNG signature + IHDR only. Plus a grid icon picker with a pull-out, `tortal_pinreload`, and the
>   first artwork the mod has ever shipped (eight icons — whose filenames are now **API**, because
>   they are written into ZDOs).
>   *Rig finding:* the first fetch of a session is sent during the login handshake, before routed
>   RPCs flow, and was never retried — so the alphabetically-first icon never arrived. Requests are
>   timestamped and swept, not held in a plain "have I asked?" set.
> - **v1.3.1** — Favorite toggle inside the portal configuration window (the current portal is
>   excluded from the destination list, so home base was the one gate you could not star from);
>   `DefaultPinIcon` as a dropdown instead of a magic filename; **`Minimap.UpdatePins` reads
>   `m_icon` exactly once**, inside the element-creation branch, so a live marker needs
>   `m_iconElement.sprite` repointed too; the cursor fight moved from a postfix to a **prefix** on
>   `GameCamera.UpdateMouseCapture` (vanilla re-locks every frame and Unity snaps the pointer to
>   screen centre on lock, so correcting afterwards ran lock-snap-release 60×/s); and photographs
>   shot through the game's own **Post Processing Stack v1** profile, which lives on a
>   `PostProcessingBehaviour` beside `CameraEffects` and which `Camera.CopyFrom` does not bring.
> - **`AnchorEnvironment.cs`, the anchor's missing sky.** A camera in the Ashlands
>   photographed real ground with no ash rain, no ashen sky and no foliage but trees. **Not a
>   loading bug:** sky, sun, weather, cloud domes and grass are one global kit welded to the local
>   player's camera by `FollowPlayer`. Fixed three ways — `Camera.onPreRender`/`onPostRender`
>   bracket the anchored camera alone and swap fog/sun/ambient/shader-globals to the destination's
>   own `EnvSetup` (chosen by vanilla's seeded roll, so it is the *same* weather); particles, the
>   env object and cloud domes get their own copy with `FollowPlayer` stripped; and grass is
>   **poked**, because `ClutterSystem` turns out to expire patches on a 2 s TTL with no camera test
>   at all. New synced entry `AnchoredEnvironment`.
>   *Rig finding, and the expensive one:* the whole mechanism shipped **latched off**. The
>   environment is resolved when the anchor goes up, before the far heightmap exists, so the first
>   resolve legitimately returns null — and the per-frame retry sat **behind** an `_env == null`
>   early-out and could never reach itself. Silent, no log line, every feed wearing the player's own
>   sky. Cost two rounds of playtesting to find. **Diagnose by instrumenting, not by inferring: a
>   silent success and a silent no-op are indistinguishable from outside.**
> - **The horizon was never requested.** `FindSectorObjects(zone, near, distant, list)` was being
>   called with `distant = 0` in both the client anchor and the server send path — and its distant
>   pass loops `near+1 .. near+distant`, so zero fetches **nothing**, not "a little". Fully loaded,
>   correctly lit destinations that still read as empty stage sets. Written up in
>   [ZoneAnchor.md](ZoneAnchor.md), which had recorded the `0` as correct.
> - **`AnchorPresence.cs` — mobs at the far end.** `SpawnSystem` gates on a **`Player`**, not on a
>   loaded zone: `GetPlayersInZone` returning empty is an early return, and `FindBaseSpawnPoint`
>   throws its dart around one of those players. Two postfix/prefix patches supply a presence and
>   move the centre to the anchor, plus a prefix claiming zone ownership. Deliberately **not** a
>   fabricated `Player` object (needs a `ZNetView`, replicates as a ghost to every other client).
>   Synced entry `AnchoredSpawning`, **default off** — the creatures are real and persist — and
>   live views only, never the unattended background walk.
> - **Favorites were keyed by `ZDOID`, which is not stable.** `ZDO.Load` opens with
>   `m_uid.SetID(++ZDOID.m_loadID)`: **every ZDO is renumbered on every world load.** Stars worked
>   all session and were orphaned by the next login, with the file on disk intact the whole time.
>   Re-keyed to portal **position**. Worth remembering generally — a `ZDOID` is a session handle,
>   not an identity, and anything persisted against one is a time bomb.
>
> **The general technique is written up separately — read
> [EnvironmentalControl.md](EnvironmentalControl.md)** and [ZoneAnchor.md](ZoneAnchor.md), the
> latter now carrying the distant-ring warning and a new *Mechanism 6 — Presence*.
> Still open: a faint **shimmer** in the live feed (unexplained; the feed is now the snapshot
> pipeline on a timer, so it is not a second code path); whether `_NetRefPos` fades geometry at an
> anchor; and a flagged-not-fixed report that the portal menu triggers ~1–1.2 m *in front of* the
> gate regardless of `MenuOpenDistance` — suspected `m_proximityRoot` forward offset on the prefab.
> **The live view is shipped experimental and default-off** for the shimmer and a multi-second
> warm-up; the stored photograph is the finished feature.

> ## ✅ RESOLVED in v1.2.0 — `VFXPatch.cs` deleted
>
> The hazard that stood here (the 1.0.0-era `VFXPatch`: still registered on every launch, a hard-coded dev-box fallback path one file away from firing, and an irreversible particle strip that would have undone the v1.1.1 `TortalVanillaVFXState` fix) was **removed in the v1.2.0 release**: file deleted, `typeof(VFXPatch)` out of `Plugin.PatchTypes`, `InitBundle()` out of `Awake`. The user's hand-written dead-code eulogy is preserved in history as commit `b83e3b3`, the deletion in `3a8a63e`. The "no dependency on MistsofAvalor" claim is now true in the source, not just in spirit.

Patch registration is per-class (`_harmony.PatchAll(patchType)` in a loop, each in its own `try/catch`), so a patch class that fails to apply disables its own feature and nothing else. Every patch body is itself wrapped, and prefixes return `true` on exception so the failure mode is "vanilla behaviour" rather than "no behaviour".

---

### TortalUITheme — Procedural Gilt-Frame IMGUI Theme ⭐

- **Purpose:** A complete IMGUI look-and-feel — an ornate gold picture frame (slim polished double rails, acanthus corner flourishes, a palmette crest centred on the top and bottom rails) plus a matching set of `GUIStyle`s — generated **entirely at runtime from code**, with zero shipped texture, shader or font assets. Built to a reference image of a real gilt frame. This is the most reusable system in the project and the natural successor to the Njord/WingsoftheValkyrie "procedural rune texture" lineage, applied to UI chrome instead of world VFX.

- **Key files:** `TortalPortal/UI/TortalUITheme.cs` (single file, ~830 lines, `internal static class`).

- **Architecture:**

  **The two-buffer painter.** The core idea, and the reason the output looks like metal rather than flat line art: shapes are painted into **two** float buffers — `_a` (coverage/alpha) and `_z` (height, 0..1) — and are **not** coloured as they are drawn. Colour is resolved once, at `Bake()`, by embossing the *height field* against a single fixed light over the top-left shoulder:

  ```
  s     = (Z(x-1,y) - Z(x+1,y)) + (Z(x,y-1) - Z(x,y+1))
  shade = clamp01(0.42 + 0.50*s + 0.18*z)
  colour = shade < 0.5 ? lerp(GoldDeep, Gold, shade*2)
                       : lerp(Gold, GoldBright, (shade-0.5)*2)
  ```

  Every primitive writes a dome (`z = sqrt(1 - (d/r)^2)`) rather than a flat stamp, so strokes get a lit flank and a shadowed flank automatically, at any width, without per-shape shading code.

  **Why baking the light last matters.** The painter exposes `MirrorX`, `MirrorY` and `Transpose` flags applied inside a single `Put()` write. `MirrorX/MirrorY` generate the other three corner tiles from one design; `Transpose` (reflect about the diagonal, requires a square tile) repeats an edge's ornament on the adjoining edge. Because all three only **move pixels** and the emboss runs afterwards on the final buffer, *the lamp never moves*. A mirrored corner is still lit from the top-left, which is what makes four independently generated corner textures and four independently generated edge textures read as one object lit by one light. Baking colour at paint time would have required per-variant light handling.

  **Primitives** (all reduce to `Disc` except `Lozenge` and the rail writers, so `Put()`'s transform handling only has to be correct in two places):
  - `Disc(cx, cy, r)` — antialiased dome, `a = clamp01(r + 0.5 - d)`.
  - `Lozenge(cx, cy, r)` — diamond, using the L1 distance `|dx| + |dy|`.
  - `Taper(a, b, w0, w1)` — straight stroke, discs at ~3 samples/px, width lerped.
  - `Bezier(a, b, c, w0, w1)` — quadratic, same stamping.
  - `Spiral(eye, r0, growth, t0, t1, phase, w0, w1, mirror)` — **logarithmic** spiral, `r = r0 * e^(growth·t)`, stroked from the eye outward with a width taper. This is the volute the whole frame hangs on; an arc stack does not look the same.
  - `RailPixel(x, y, d, half)` — one pixel of a rail given its distance from the centreline.
  - `RailElbow(mid, half, centre)` — a rail mitred round a corner (below).

  **Frame anatomy** — 11 textures total:
  - **4 rail strips**, not one rotated texture. Each is tiny (`4 × Band` for horizontal, `Band × 4` for vertical) because the profile is constant along the edge, so IMGUI can stretch it the full window width at zero cost and perfect sharpness. There are four because *which side of the band is "outer" flips per edge* — that is what keeps the thick rail on the outside all the way round, and it is what lets the global bake light shade each edge the way a real frame catches light (top and left lit on their outer flank, bottom and right on their inner flank — physically correct for a top-left lamp).
  - **4 corner tiles** (`84×84`), drawn overhanging the window rect by `Overhang = 10`, so the flourishes spill outside the frame the way the reference does.
  - **2 crests** (`56×34`), centred on the top and bottom rails, overhanging outward by 8.
  - **1 diamond** (`14×14`) set into the middle of the title rule.
  - **1 panel** (`64×64` radial vignette, warm near-black, lifting slightly toward the centre — radial so it survives being stretched to any window size).
  - **9-slice patches** for buttons/rows/fields: `12×12`, one pixel of border around a flat fill, `FilterMode.Point`, `GUIStyle.border = RectOffset(3,3,3,3)`. The outline stays exactly one pixel however far the control stretches.

  **The rounded elbow.** All rails share one bend centre, so the bends stay concentric and the double rail keeps its spacing round the turn. For a rail at offset `mid` with the shared centre at `(c, c)`, its arc radius is simply `c - mid`. Per-pixel distance:

  ```csharp
  if (x <= c && y <= c) d = |dist((x,y),(c,c)) - r|;   // the arc quadrant
  else if (y <= c)      d = |y - mid|;                  // straight run along the top
  else if (x <= c)      d = |x - mid|;                  // straight run down the left
  else                  d = min(|y-mid|, |x-mid|);
  ```

  **Metrics** (in `TortalUITheme`): `Band = 18`, `Pad = 22`, `CornerTile = 84`, `Overhang = 10`, `CornerExtent = 74` (= tile − overhang; the straight rail run starts this far along each edge), rail cross-section `OuterMid 4.4 / OuterHalf 2.7` and `InnerMid 12.4 / InnerHalf 1.6` measured from the band's outer edge, `BendCentre = 32` (giving elbow radii 17.6 and 9.6).

  **The clearance constraint that drives the layout.** The corner flourishes are deliberately **long and shallow** — they run up to 74px along each edge but never reach more than ~36px deep. That is both what the reference does and what keeps them out of the content area, which starts at `Band + Pad = 40`. `Body(win)` and `FooterLine(win)` are the only two accessors any consumer needs; everything else is chrome.

  **Texture lifetime.** Every generated texture is flagged `HideFlags.HideAndDontSave`. Without it Unity collects them on the next scene load and `OnGUI` starts handing **destroyed** textures to the draw calls the moment the player reloads a world. `EnsureBuilt()` therefore guards on `_railTop != null`, not on a `_built` bool — for `UnityEngine.Object`s, "already built" and "still alive" are different questions.

  **Recolourable metal (added 1.1.1).** The three gold tones are no longer constants: `Gold` comes from the `UIGoldColour` config entry and the other two are **derived from it** — `GoldDeep = Gold * (0.325, 0.274, 0.192)`, `GoldBright = lerp(Gold, white, 0.72)` — so silver, bronze or verdigris is one dial with no geometry edits. The untouched default is special-cased back to the original hand-tuned trio verbatim, so a player who never opens the dial sees not one pixel change. This makes `EnsureBuilt()` a **two-tier invalidation**, which is the reusable shape here:

  - metal colour changed → `DestroyTextures()` and rebake everything (a few milliseconds, once per dial turn);
  - text scale or font delta changed → rebuild `GUIStyle`s only, no pixel regenerated;
  - otherwise → two colour compares and return.

  `DestroyTextures()` destroys explicitly rather than dropping references: `HideAndDontSave` textures are immortal until something kills them, so the same flag that stops Unity collecting them too early also makes leaking them the default. It also nulls `Title`, because the built styles hold references to textures that are now dead and `BuildStyles` must run again after the rebake.

  **The favourite heart** (`BuildHeart(28)`, added 1.1.1) is drawn from the classic implicit heart curve `(u² + v² − 1)³ − u²v³ ≤ 0` at 2×2 supersampling, baked **white**, and tinted at draw time by `DrawHeartMark(rect, lit)` — one texture serves both the gold "starred" and the ghost-grey "not starred" states, and any future colour. It is a generated texture rather than a text glyph precisely so it is never at the mercy of whichever font `FindFont()` happened to locate. It is also the one generator in the file that does **not** flip at bake: row 0 of a texture is the bottom and the curve is authored y-up, so the usual flip would be wrong.

- **How to implement/set up (step-by-step recipe):**
  1. Copy `UI/TortalUITheme.cs` into the target mod. Its only hard dependencies outside `UnityEngine` are three config entries — `Configuration.uiTextScale`, `Configuration.uiFontSizeDelta` and `Configuration.uiGoldColour` — each read defensively (`!= null ? .Value : default`). Swap them for local entries or constants and the file is fully portable. *Candidate for vendoring into `libs-Tools` as a shared source file alongside `ServerSync.cs` once those three are parameterised — Fatty already carries a port of this theme and the same `UIFontSizeDelta` knob at the same default, which is the whole reason that setting exists.*
  2. Reference `UnityEngine.IMGUIModule` and `UnityEngine.TextRenderingModule` in the `.csproj` (both already in `libs-Tools`).
  3. Call `TortalUITheme.EnsureBuilt()` as the first line of every `OnGUI()`.
  4. Draw a window: `TortalUITheme.DrawWindow(rect, "Title")`, then lay content inside `TortalUITheme.Body(rect)` and a hint inside `TortalUITheme.FooterLine(rect)`.
  5. Use the exposed styles (`Title`, `SubTitle`, `Header`, `Key`, `Value`, `Note`, `Footer`, `Button`, `Primary`, `Row`, `Field`, `ImageButton`) and helpers (`DrawInset`, `DrawOutline`, `DrawFill`, `DrawRule`, `DrawSelection`, `DrawHeartMark`, `DrawShadowed`).
  6. Rich-text colours (`HexMuted`, `HexLocked`, `HexOpen`) are `const string`s kept beside the `Color` values they mirror, so a label built with `<color=…>` cannot drift away from a label built with a style.

- **Reusable pattern/snippet — the painter core:**
  ```csharp
  private void Put(float fx, float fy, float a, float z)
  {
      if (a <= 0f) return;
      if (Transpose) { float t = fx; fx = fy; fy = t; }
      int x = Mathf.RoundToInt(fx), y = Mathf.RoundToInt(fy);
      if (MirrorX) x = _w - 1 - x;
      if (MirrorY) y = _h - 1 - y;
      if (x < 0 || y < 0 || x >= _w || y >= _h) return;
      int i = y * _w + x;
      if (a > _a[i]) _a[i] = a;     // max-blend coverage
      if (z > _z[i]) _z[i] = z;     // and height
  }

  public void Disc(float cx, float cy, float r)
  {
      for (int y = Mathf.FloorToInt(cy-r-1); y <= Mathf.CeilToInt(cy+r+1); y++)
      for (int x = Mathf.FloorToInt(cx-r-1); x <= Mathf.CeilToInt(cx+r+1); x++)
      {
          float d = Mathf.Sqrt((x-cx)*(x-cx) + (y-cy)*(y-cy));
          float a = Mathf.Clamp01(r + 0.5f - d);
          if (a > 0f) Put(x, y, a, Mathf.Sqrt(Mathf.Max(0f, 1f - (d/r)*(d/r))));
      }
  }
  ```

- **Reusable pattern/snippet — drop-shadowed IMGUI text** (IMGUI has no text shadow; mutating the shared style is safe because `OnGUI` is single-threaded and the colour is restored before anything reads it):
  ```csharp
  Color prev = style.normal.textColor;
  style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
  GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
  style.normal.textColor = prev;
  GUI.Label(r, text, style);
  ```

---

### Offline Procedural-Graphics Verification Harness ⭐

- **Purpose:** Iterate on procedurally generated art **without launching the game**. Procedural UI/VFX geometry is written blind — you cannot see a `Texture2D` you built in code until the game is running, and a Valheim round-trip (build → copy to profile → launch → load world → walk to the thing) is minutes per iteration. This harness cuts that to ~2 seconds and made the frame above shippable in three iterations.

- **Key files:** built ad-hoc in the session scratchpad (`framecheck/framecheck.csproj`, `framecheck/Program.cs`, `net8.0` console `Exe`). Not vendored — the point is that it is cheap to recreate per generator.

- **Architecture:**
  - Stub the handful of UnityEngine types the generator actually touches: `Mathf` (Clamp01/Abs/Sqrt/Min/Max/Sin/Cos/Exp/Lerp/Round-Floor-CeilToInt), a `Vector2` struct, a float `Color` struct. ~40 lines.
  - **Copy the generator verbatim** — same constants, same call order — returning a `Color[]` instead of a `Texture2D`. Verbatim matters: a "cleaned up" port verifies the wrong thing.
  - Composite a mock window exactly the way the real draw code does (panel, stretch the rail strips, blit corners and crests, hairlines), on a **non-black backdrop** so alpha and any overhang outside the window rect are visible.
  - Overlay the real content rect in red, to prove ornament never intrudes into layout.
  - Write a PNG **by hand** — no `System.Drawing`, no NuGet restore. PNG needs zlib, which is `DeflateStream` + a 2-byte header + Adler-32, wrapped in IHDR/IDAT/IEND chunks with CRC-32. ~50 lines, fully offline.
  - Emit a second image with 4× nearest-neighbour crops of the corner and the crest — full-frame views hide the flaws that matter at pixel scale.

- **What it caught** (none of which was visible in the full-frame view, and all of which would otherwise have shipped): the corner volute's spiral eye sitting on top of the rail elbow, so the two merged into a blob; the outer corner palmette floating detached from the frame, reading as debris rather than ornament; a first-pass crest so small it read as an accidental smudge; and a diagonal spine that read as a dart/pin.

- **How to implement/set up (step-by-step recipe):**
  1. `dotnet new console` in the scratchpad, `net8.0`, no packages.
  2. Paste the Mathf/Vector2/Color stubs, then the generator, unmodified.
  3. Composite onto a coloured backdrop; overlay layout guides.
  4. Hand-roll the PNG writer (snippet below).
  5. `dotnet run`, open the PNG, adjust constants, repeat. Only port constants back to the real file once it looks right.

- **Reusable pattern/snippet — dependency-free PNG writer:**
  ```csharp
  static byte[] Zlib(byte[] data)
  {
      using var ms = new MemoryStream();
      ms.WriteByte(0x78); ms.WriteByte(0x01);                       // zlib header
      using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
          ds.Write(data, 0, data.Length);
      uint a = 1, b = 0;                                            // Adler-32
      foreach (byte x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
      uint ad = (b << 16) | a;
      ms.WriteByte((byte)(ad>>24)); ms.WriteByte((byte)(ad>>16));
      ms.WriteByte((byte)(ad>>8));  ms.WriteByte((byte)ad);
      return ms.ToArray();
  }
  // Scanlines are [filter byte 0][RGB…] per row; chunks are
  // [len BE][type+data][CRC32 of type+data]; colour type 2 = truecolour RGB.
  ```

---

### PortalUIManager — IMGUI Window System

- **Purpose:** The destination picker (portal list + preview + teleport), the slim favourites strip, the quick PIN modal, the snapshot lightbox and the per-portal configuration window (name + PIN) — all hosted on one `DontDestroyOnLoad` MonoBehaviour with a single `OnGUI`.

- **Key files:** `TortalPortal/PortalUIManager.cs`.

- **Architecture:**
  - **Explicit `Rect` layout, not `GUILayout`.** The whole window is positioned with computed rectangles; `GUILayout` is used *only* inside the scrolling portal list, where variable item counts genuinely need it. Two reasons: (a) precise control over a themed layout, and (b) `GUILayout` scopes are a hazard when a click handler can tear the window down mid-frame — the pre-theme code called `HideUI()` from inside a `BeginHorizontal`, leaving the layout stack unbalanced. With explicit rects, a handler that closes the window and returns is always safe. Where a handler *does* fire inside a `GUILayout` scope (the favourites strip's one-click travel), the scopes are closed by hand before returning.
  - **One `OnGUI`, five mutually exclusive modes.** `OnGUI` dispatches to exactly one of `DrawPinModal` / `DrawConfigUI` / `DrawExpandedPreview` / `DrawSlimUI` / `DrawSelectUI`. This is not tidiness — see the lightbox section for why an IMGUI modal must suppress what is underneath rather than draw over it.
  - **Precomputed row model.** `BuildEntries()` resolves each destination once when the window opens into a `PortalEntry` (tag, PIN state, creator, position, rotation, biome, distance, prebuilt rich-text label, lowercased search key, favourite flag). Biome in particular *must* be precomputed: `WorldGenerator.GetBiome` samples world noise, and `OnGUI` runs at least twice per frame (Layout + Repaint) for every row on screen. Rows are built **entirely from the server's portal index**, never from a ZDO — see `PortalRegistry` for why a client does not have the ZDO of a portal on the far side of the map.
  - **Rich-text injection guard.** Portal tags and creator names are player-entered and every row label is rich text, so an unescaped `<` would eat the rest of the row. `Sanitise()` maps `<`/`>` to `(`/`)` at build time, once, rather than per frame.
  - **Two-line rows.** A single `GUI.Button` with `richText` and an embedded `\n` renders name-on-line-one and `biome · distance · owner` on line two via `<size>`/`<color>` tags — far less code than sub-rect layout, and it stays one hit target.
  - **Selection highlight painted under the control.** `GUILayoutUtility.GetRect(...)` reserves the row, `DrawSelection(row)` paints the wash, then `GUI.Button(row, ...)` draws on top with a transparent normal background. Doing it through the style's `onNormal` instead would lose the highlight on hover.
  - **Three window shapes, one rect function.** `SelectWindowRect()` returns the slim strip (`380×560` design units), the full window without its picture column (`640×660`, when `ShowDestinationPreviews` is off) or the full window (`860×660`), each `S()`-scaled and clamped to the screen, pinned left so the large map stays readable on the right. It is `static` and shared deliberately: the map-centring maths, the centre-message offset and the "is the pointer over our window" test all need to know exactly how much screen the window covers, and computing that in three places is how they drift apart. `FreeCentreFraction()` reduces it to one number — the middle of what the window is *not* covering, as a 0–1 fraction of screen width — which both the map centring and `MessageHudPatch` read.
  - **Dead uGUI path removed.** The original implementation built a `Canvas` + `Image` + `Text` hierarchy in `BuildAndShowUI()` and then immediately `Destroy`'d it before falling through to `OnGUI` — pure churn, and it called `Resources.GetBuiltinResource<Font>("Arial.ttf")`, which returns null on the Unity version Valheim now ships. Deleted.

---

### Favourites and the Slim Strip

- **Purpose:** Star portals you use, sort them to the top, and open the window as a slender one-click launcher containing nothing else.

- **Key files:** `TortalPortal/Favorites.cs`, the strip and star code in `PortalUIManager.cs`, `TortalUITheme.DrawHeartMark`.

- **Architecture / lessons:**
  - **Purely client-side, so multiplayer needs no design at all.** Which portals somebody likes is nobody else's business; nothing crosses the wire, and every player keeps a plain text file at `Paths.ConfigPath/wubarrk.TortalPortal.favorites.txt`. Not syncing it is what makes it free.
  - **The key is `worldName + "|" + ZDOID`.** World-scoped because one file serves every world and server the player joins, and a star on "Home" in one world must never light up a stranger's portal that happens to share a ZDOID in another. Keyed on the ZDOID rather than the tag because a ZDOID is stable for as long as the portal stands: renaming keeps the star, demolishing and rebuilding does not — which is the honest answer, since it *is* a different portal.
  - **The star must not overlap the row button.** IMGUI fires **every** control whose rect contains the cursor, so a star laid *over* the row would toggle the favourite and select the portal on the same click. The row is split into two non-overlapping rects (`row.width - starW` and a `starW` strip at the end), and the heart is drawn as a texture inside the star rect by a control with `GUIStyle.none`.
  - **Re-sorting mid-list is safe only because click handling has its own event pass.** Toggling a star changes the sort order, which changes the order of the `GUILayout` controls — normally a way to desync IMGUI's control count between Layout and Repaint. The toggle sets a `resort` flag and the sort runs after the loop; because a mouse-up event pass is a separate pass from the Layout pass that preceded it, the next Layout simply sees the new order.
  - **One sort order everywhere** (`favourites first, then case-insensitive alphabetical`), shared by the full list, the strip and the lightbox's paging, so a portal never appears to move when the window changes shape.
  - The strip only opens on `ShowUI` when the player actually has a favourite in *this* world; the `«` collapse control in the full window likewise only appears when there is something to collapse into, because folding into an empty strip is just a smaller window with nothing in it.

---

### Quick PIN Modal

- **Purpose:** A locked portal launched from the strip or a map marker gets a small "PIN, Enter, gone" window instead of hauling the full picker out.

- **Architecture / lessons:**
  - **`GoOrPrompt(entry)` is the single gate every "just go" path funnels through** — strip click, map second-click — so there is exactly one place that decides "locked and not an admin ⇒ prompt". The full window keeps its own inline PIN row, which is a different affordance for a different situation.
  - **Read `Return` before drawing the field.** A focused IMGUI text field consumes the `KeyDown` once it has seen it, so `Event.current` is inspected at the top of `DrawPinModal` and `ev.Use()`d there. After a successful submit the method returns immediately rather than continuing to draw a window that no longer exists.
  - **The PIN fetch starts when the modal opens, not when the player presses Enter.** The PIN lives on the portal's own ZDO, which for a distant gate has to be requested from the server; kicking `ResolveDetails` off at open time means the round trip usually completes while the player is still typing. Submitting early is handled explicitly ("Still reading that portal…") rather than by disabling the button.
  - **Close the map behind it.** `SetMapMode(None)` on open, `Large` on cancel, and `_pinReturnSlim` remembers which shape the player came from — a modal over a live map reads as part of the map; over nothing it reads as a question.
  - A wrong PIN keeps the window up, announces itself through the centre message, clears the field and never drops focus, so a second attempt is just typing again.

---

### Full-Size Preview Lightbox / Photo Album (IMGUI Modal) ⭐

- **Purpose:** Click a destination thumbnail to view the snapshot full-screen at the player's resolution, then page through every portal's photograph without leaving the overlay.

- **Architecture:**
  - **The IMGUI modality problem, and the only reliable fix.** IMGUI hit-tests *every* control whose rect contains the cursor during a given event — overlapping controls **both** fire on one click. So an overlay drawn on top of the picker does not block it; a single click would select a list row *and* dismiss the overlay. The fix is not z-order or event consumption: it is to **not draw the underlying window at all** while the modal is up.
  - **Invisible dismiss button.** A full-screen `GUI.Button(rect, GUIContent.none, GUIStyle.none)` drawn **last** (so it sits above the frame) gives click-anywhere-to-close. `GUIStyle.none` matters — reusing the row style would wash the entire screen with its gold hover tint.
  - **…and the same "every control fires" rule bites the album's own arrows.** The page-turn buttons sit in the gutters *inside* the full-screen dismiss rect, so a click on an arrow also lands on the dismiss button and would close the album on every page turn. Two guards: a `cycled` flag set by the arrow handlers, and an explicit `prev.Contains(ev.mousePosition) || next.Contains(...)` hover test. The flag alone is not enough, because the arrow and the overlay do not necessarily resolve in the same event.
  - **Window built around the image, not vice-versa.** Compute the fitted image size first, then size the window as `image + chrome`, where `chrome` is exactly what `Body()` subtracts. `Body(win)` then *is* the image rect, to the pixel, at any text scale:
    ```csharp
    float chromeW = 2f * (Band + Pad);
    float chromeH = 2f * Band + TitleHeight + FooterHeight;
    float imgW = availW, imgH = availW / aspect;
    if (imgH > availH) { imgH = availH; imgW = availH * aspect; }
    Rect win = new Rect(cx, cy, imgW + chromeW, imgH + chromeH);
    GUI.DrawTexture(Body(win), tex, ScaleMode.ScaleToFit);
    ```
  - Aspect comes from the loaded texture, not an assumed 16:9, so a snapshot captured at another resolution still fits correctly — and falls back to 16:9 while a snapshot is still in flight, so the frame has a shape to arrive into rather than snapping about mid-cycle.
  - **Paging reuses the row-click path exactly.** `CyclePreview(±1)` rebuilds the visible list under the current search filter, walks it modulo its length, and then does everything a row click does — clear the old texture, set `Details = Pending`, restart `ResolveDetails`, re-highlight the map pin — while leaving `_previewExpanded` true. Arrow keys and the on-screen arrows call the same method.
  - **Affordance:** the thumbnail's click target uses the `ImageButton` style — fully transparent normally, gold-edged on hover — plus a small "click the image to view it full size" hint underneath.

---

### Configurable UI Text Scale and Font Delta

- **Purpose:** Two local, deliberately un-synced config entries scale all portal UI text — and everything that holds it. `UITextScale` (float, 0.6–3.0, default **1.05**) is a proportional zoom of the whole window; `UIFontSizeDelta` (int, −6…+24, default **+6**) is a flat point offset applied *after* the scale.

- **Architecture:**
  - **Why both.** A scale keeps a window's proportions and is what you want on a 4K display; a flat delta is what you want when you simply need bigger letters at the same layout — and it is what lets two mods agree. Fatty carries a port of this theme with the same setting: set both mods to the same delta and their windows read at one size.
  - **Order matters and is fixed in one place:** `Pt(design) = max(8, round(design * TextScale) + fontDelta)`. Scaling first and adding second is what keeps the delta worth exactly what it says in points, whatever the scale is.
  - **Styles and textures have different lifetimes.** `EnsureBuilt()` builds textures once, but re-runs `BuildStyles(scale, delta)` whenever either live config value differs from the built one. Styles are cheap; ornament textures are not. Net effect: both dials are picked up **live** from BepInEx ConfigurationManager without regenerating a single pixel.
  - **Scaling the font alone is not enough** — fixed-height rows, fields and buttons would clip. `public static float S(float v) => round(v * TextScale)` handles layout metrics, and every metric in the UI that holds text goes through it.
  - **Chrome bands grow with both dials:** `TitleHeight = 26 + round(28*scale) + round(max(0,delta)*1.5)`, `FooterHeight = max(30, round(30*scale) + max(0,delta))`. Two constants worth noting. The **fixed 26** is crest clearance — the top palmette reaches 26px into the window, so the title must begin below it whatever the text is doing. And only a **positive** delta widens the bands: shrinking the text must not pull the title band up into ornament that is generated at a fixed pixel size. That is the general lesson — when ornament is fixed-size, the layout constants that clear it stay fixed while the text-bearing ones scale, and a negative text adjustment must not be allowed to reclaim the clearance.
  - **Windows scale and clamp:** `Mathf.Min(Screen.width - 120f, S(860f))` for the widest shape, with the left-pinned x-position clamped so a large window still fits on screen.

---

### Snapshot Preview Pipeline (Camera → ZDO → Thumbnail → Lightbox)

- **Purpose:** Each portal stores a JPEG of what its destination looks like, shown in the picker and the lightbox.

- **Key files:** `TortalPortal/PlacementPatch.cs` (capture and backfill), `TortalPortal/PortalUIManager.cs` (load/display).

- **Architecture:**
  - **Storage:** `nview.GetZDO().Set("PortalSnapshot", jpgBytes)` — a ZDO byte-array key as free replicated storage, so every client sees every portal's preview with no custom RPC. Same family as the `Player.m_customData` / ZDO-custom-key persistence pattern used across the codebase. Refreshed on placement (after a `WaitForSeconds(0.5f)` + `WaitForEndOfFrame` so the piece is instantiated) and by a background coroutine.
  - **The capture pass must run on players, not the server.** A snapshot is a camera render and a dedicated server draws nothing, so the original server-side-only pass could never produce an image at all — `InitializeBackgroundSnapshot` now returns immediately on `ZNet.IsDedicated()` and every player photographs the portals loaded around them. Between everyone playing, the network fills itself in. The coroutines live on the plugin object, which survives world loads, so `ZNet.Start` stops the previous one before starting a new one.
  - **Gaps first, refresh second.** The backfill wakes every `12.5s`, takes up to 2 portals that have **no** snapshot at all, and separately every `SnapshotRefreshIntervalMinutes` (default 5) re-photographs up to 3 that already have one. Worth recording what tuning this interval *cannot* fix: only portals whose instance is actually spawned around a player can be photographed, so a portal nobody has visited waits for somebody to walk near it no matter how fast the timer ticks. The default dropped 30 → 5 because a long interval mostly meant missing the window in which a player was standing near the portal at all.
  - **Claim ownership before writing.** A write to a ZDO this peer does not own is silently discarded at the next sync, which is *most* portals when backfilling an existing world. `if (!nview.IsOwner()) nview.ClaimOwnership();` before the `Set` is the whole fix.
  - **Camera framing rules learned the hard way:** the camera originally sat 1.5 m *behind* the portal shooting through the arch, so every preview was the gate itself with the mod's own rune VFX blazing across the middle and the destination hidden behind it. Correct framing is to stand the camera **out in front looking away** — that puts the portal and everything drawn in it behind the camera entirely. Final values: 2.0 m forward, 1.7 m up (eye level), 8° nose-down (frames ground, not sky), 78° FOV (wide angle).
  - **`Camera.CopyFrom(Camera.main)`** before overriding position/rotation/FOV/target, so the preview inherits clear flags, culling mask, fog and rendering path and looks like the world rather than a flat render of it. Two required follow-ups, and **the order of the first one is the bug**: `cam.enabled = false` must come **after** `CopyFrom`, because `CopyFrom` copies the enabled flag along with everything else and hands it straight back — clearing it first (as the code did until 1.1.1) left a second live camera rendering the player's view for one frame before the deferred `Destroy` caught up. And `cam.ResetAspect()` **after** assigning `targetTexture` — `CopyFrom` brings the game window's aspect with it, which would otherwise letterbox the RenderTexture.
  - **Hiding your own VFX for a capture — never `SetActive(false)` on the piece root.** The original code called `vfx.gameObject.SetActive(false)`, but the VFX component lives *on the portal root*, so it briefly deactivated the whole piece (`ZNetView` and all) mid-frame. The correct shape is for the VFX component to record the GameObjects it created and expose `SetVisible(bool)` that toggles only **their** `Renderer.enabled`. References stay valid, the systems keep simulating, and the game object graph is untouched. Restore in a `finally`, nulling the handle after a successful restore so it is not done twice.

- **Reusable pattern/snippet — the `ImageConversion.LoadImage` CS0518 shim ⭐** (a genuine `net48`-vs-Unity papercut worth remembering; `LoadImage` has a `ReadOnlySpan<byte>` overload typed against netstandard 2.1 that `net48` reference assemblies cannot resolve at *compile* time, even though only the `byte[]` overload is wanted — reflective binding sidesteps overload resolution entirely and Mono resolves it fine at runtime):
  ```csharp
  private static MethodInfo _loadImageMethod;
  internal static void LoadImageViaReflection(Texture2D tex, byte[] data)
  {
      if (_loadImageMethod == null)
          _loadImageMethod = typeof(ImageConversion)
              .GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
      _loadImageMethod.Invoke(null, new object[] { tex, data });
  }
  ```
  It is `internal` rather than `private` for one reason: `TortalPinIcon` loads the admin's custom map-pin PNG through the same shim, and a second copy of a workaround is a second thing to forget.

---

### PortalRegistry — Server-Published Portal Index ⭐

- **Purpose:** Where the destination list actually comes from. Everything in the picker — rows, map markers, click-n-go, the teleport itself — is built from this and nothing else.

- **Key files:** `TortalPortal/PortalRegistry.cs`, `TortalPortal/PlacementPatch.GetAllPortals`.

- **Architecture / lessons:**
  - **The failure this exists to fix is not obvious in single-player.** `ZDOMan` only holds the ZDOs a peer has actually been sent, and the server only sends what falls inside a player's active area. Scanning it locally therefore works perfectly for the host and returns "the one or two portals underfoot" for a client — worst of all on an established world, where every portal is somewhere the player has not yet stood. The server is the only peer holding the whole world, so it builds the index and publishes it; clients only ever read.
  - **`ServerSync.CustomSyncedValue<string>` is the transport**, chosen for compression and fragmentation but mostly for the **login handshake push**: a player joining a world full of portals has the entire index before they can walk up to one. Registering the value has to happen before `ZNet.Awake` wires up the ConfigSync RPCs, which is why `PortalRegistry.Init()` is called from `Plugin.Awake` ahead of `ApplyPatches()` — merely touching the class is enough.
  - **The payload is a versioned `ZPackage` in base64.** A leading format byte is checked on decode and a mismatch logs "the server is running a different version of the mod" rather than throwing. `GetEntries()` caches on the raw blob string and **records the decode attempt even when it fails**, so one bad payload cannot re-throw on every frame of `OnGUI`.
  - **Only "is it locked", never the PIN.** The index carries id, tag, creator name, position, rotation and a bool. The PIN stays on the portal's own ZDO and is pulled per-selection, which keeps the exposure exactly what it was before the index existed rather than landing every PIN on the server in every client's memory at login.
  - **Republish only on change.** The server rescans every `PortalIndexRefreshSeconds` (5–300, default 10) and assigns `SyncedIndex.Value` only if the encoded blob differs — assignment is what fires the broadcast. The startup pass forces a publish, but waits 3 s after `ZDOMan`/`Game` exist first: the world's ZDOs stream in after `ZNet.Start`, and publishing immediately would push an empty index and look exactly like the bug it replaced.
  - **`GetAllPortals()` prefers `ZDOMan.GetPortals()`** — the game's own portal list, maintained as ZDOs load, arrive and are destroyed. It covers every prefab in `Game.PortalPrefabHash`, so **modded portal pieces are included for free**, and it does not walk every sector in the world. The per-prefab `GetAllZDOsWithPrefabIterative` scan of `portal_wood`/`portal_stone`/`portal_blackmarble` remains only as a fallback for a world that somehow loaded without that list populated.
  - Because rows carry position *and rotation*, `ExecuteTeleport` can compute the exit point (`pos + rot * forward * m_exitDistance + up`) without the client ever holding the destination's ZDO.

---

### Map Integration: Markers, Input Arbitration and Click-n-Go ⭐

- **Purpose:** Mark every listed portal on the large map, highlight the chosen one, and let a click on a marker select and then travel — while the IMGUI window sitting over that map stops eating the map's input and vice-versa.

- **Key files:** `TortalPortal/MapInputPatch.cs`, the map section of `PortalUIManager.cs`, `TortalPortal/TortalPinIcon.cs`.

- **Architecture / lessons:**

  **1. IMGUI and uGUI have never been introduced.** The destination window is IMGUI; the large map is uGUI. Unpatched, every click on the window *also* landed on the map behind it — toggling pins, starting drags, opening vanilla's pin-name box, and a middle-click through the window pinged the whole server. The fix is a set of prefixes on `Minimap` that stand down while `PortalUIManager.IsPointerOverWindow()`:
  - `OnMapLeftDown` — refusing the **down** handler kills the click, the double-click *and* the drag in one place, because all three derive from `m_leftDownTime`, which never gets set.
  - `OnMapLeftUp` — a press that started on the map and was released over the window would leave that state set, turning the *next* press into a phantom double-click; the up-prefix clears `m_leftDownTime` and `m_dragView` before refusing.
  - `OnMapRightClick` (removes pins in vanilla — through the window that deleted whatever pin sat under the portal list) and `OnMapMiddleClick` (pings the whole server; misfiring that through a UI window is how you wake five people up at midnight).
  - `UpdateMap` — `ref bool takeInput` set false. This is the one place the scroll wheel can be taken from the map's zoom while the cursor is over the portal list, and it is a *prefix that returns void*: everything after the input block (map centring, drag continuation) still runs, which is exactly what is wanted.

  `IsPointerOverWindow()` returns true unconditionally for the configuration window, the PIN modal and the lightbox — all three are modal — and otherwise tests `SelectWindowRect().Contains(gui)`. Note the coordinate flip: `ZInput.mousePosition` is bottom-left origin, GUI rects are top-left.

  **2. Click-n-go lives in the same patch site,** because it is the same event. `OnMapLeftClick` converts the cursor to a world point, uses vanilla's own pin-proximity rule (`m_removeRadius * m_largeZoom * 2`), and hands it to `HandleMapClick`. First click on a marker selects; a click on the already-selected marker travels via `GoOrPrompt`. Returning "this click was ours" makes the prefix skip vanilla, so the marker does not get a checkmark slapped on it in passing.

  **3. Selecting from the map must NOT recentre the map.** This is the bug that broke click-n-go in 1.1.1 and is the most transferable lesson in the section. Recentring is right for a click in the **list** — the map swings round to show you where that portal is. It is wrong for a click **on** the map, because recentring drags the marker out from under the cursor: the second click of a click-n-go then lands on empty map somewhere else entirely and never reaches the portal. Hence `Select(entry, recentreMap: false)` from the map path only. Generally: any "reveal" that moves the world under the pointer is incompatible with a two-click gesture on that same pointer position.

  **4. The double-click that trails every successful click-n-go.** Vanilla turns two quick clicks into `OnMapLeftClick`, `OnMapLeftClick`, `OnMapDblClick`. The second click travels and closes the window — so by the time the double-click arrives, `IsSelectUIVisible()` is already false and the ordinary guard no longer applies, leaving vanilla free to open its pin-name box over the player's arrival. The fix is a timestamp (`_suppressDblClickUntil = Time.unscaledTime + 0.6f`) set when a marker click is acted on, and checked in `OnMapDblClick` **before** the visibility guard. Ordering is the whole trick: a state-based guard cannot cover an event that arrives after the state is gone.

  **5. Marker styling has to be a POSTFIX on `Minimap.UpdatePins`.** `UpdatePins` assigns every pin's icon colour on every run, so anything set beforehand is overwritten in the same frame. Running afterwards is the only way a tint sticks — and it also re-applies automatically after `Minimap` destroys and rebuilds a marker (which it does whenever one scrolls off the edge of the map), so the highlight survives panning with no bookkeeping. Because rebuilt elements arrive at scale 1, **every** pin is written every pass, not just the selected one.

  **6. Size the selected pin with `localScale`, not `m_doubleSize`.** `m_doubleSize` is a **boolean**: the only sizes on offer are normal and *twice* normal, which is why the selected marker used to look oddly huge. `Minimap` reads and writes `sizeDelta` but never touches `localScale`, so nothing fights for it and any factor is available — the selected marker is 1.3× and red (icon *and* name label). The general shape: when a vanilla system owns one property, drive the sibling property it never reads.

  **7. Leave the pin's alpha alone.** Vanilla's alpha carries the shared-map exploration fade; overwriting it would make the selected marker punch through a region the player has not actually explored.

  **8. Custom icons ride on a vanilla `PinType`.** `AddPin` stamps `m_icon` from the type and the map's UI element reads `pin.m_icon` when it is built, so assigning a different `Sprite` immediately after `AddPin` — before the next pin update — is all that is needed. `PortalPinIcon`'s enum values are spelled out rather than counted, because vanilla's `Icon4` is **6** (Death and Bed sit between), and the two non-vanilla entries (`Rune = 100`, `Custom = 101`) sit far above the `PinType` range so no future vanilla icon can collide.

  **9. The rune pin is generated, not shipped.** `TortalPinIcon.BuildRunePin` takes glyph 7 from the rune texture generator — a *fixed* index rather than the colour-hashed pick, so every client and every session shows the same symbol and two players comparing maps are looking at the same thing — composites it over a dark backing disc and puts a bright ring round it. A bare glyph over transparency reads as debris at map scale; the disc and ring are what make it read as a deliberate marker. Same `HideAndDontSave` rule as the UI theme, and the same consequence: when the colour dial moves, the **old sprite and its texture are explicitly destroyed** before the new one is built. `Custom` loads `TortalPortalPin.png` from beside the plugin DLL (ship it in a modpack to give a whole server one look) and falls back to `Rune` when absent — `Get()` never returns null.

  **10. Selection change alone does not redraw the map.** `Minimap` only runs `UpdatePins` when `m_pinUpdateRequired` is set, and a selection can change while the map sits perfectly still. `HighlightPin` therefore *applies nothing* — it just sets that flag and lets the postfix do the work on the next update.

  **11. The markers are transient.** Added on window open, removed on close, never saved, so they cannot silt up the player's own pin list — and torn down explicitly when the configuration window opens over the top, or they would be stranded on the small minimap for as long as it is up.

---

### The Rune Gateway VFX ⭐

- **Purpose:** Replace vanilla's portal particles with a five-layer procedural runic gateway that wakes as a player approaches and bleeds away behind them.

- **Key files:** `TortalPortal/VFX/TortalPortalRuneVFX.cs` (the gateway), `VFX/TortalRuneTexture.cs` and `VFX/TortalRuneMaterial.cs` (both **ported verbatim from Wings of the Valkyrie**), `TortalPortal/PortalBehaviorPatch.cs` (attachment and live config).

- **Why it is built from stock components.** Everything is `ParticleSystem` + `LineRenderer` driving procedurally generated textures through a **stock** particle shader discovered at runtime (`Particles/Standard Unlit` → `Legacy Shaders/Particles/Alpha Blended` → … with a scored scan of loaded materials as a last resort). Shipping a custom shader into Valheim is what turns custom materials pink; that shader-priority list is the single most valuable thing in the ported files, because it encodes which stock shaders actually exist and work in Valheim's pipeline. Nothing here needs an AssetBundle.

- **The five layers**, because one particle system on its own reads as a campfire rather than a gateway:
  1. **Veil** — small runes born on the **rim** of the doorway and swirled inward.
  2. **Rings** — two counter-rotating vertical rune circles plus four spokes, so the gate reads as machinery that is running rather than a static decal.
  3. **Sigil** — a horizontal magic circle scribed across the threshold, which is what sells "portal" from a distance and from above, with runes standing around its rim.
  4. **Motes** — slow embers rising out of the sigil, the one layer simulated in **world** space so they hang in the air as you walk past.
  5. **Swirl** — a rune ribbon orbiting the whole structure (see below).

- **Architecture / lessons:**

  **Measure the arch, do not tabulate it.** The orb height was a hand-tuned `GateHeight * 0.45`, which happens to land on the centre of the wood arch but parks the orb by the stone portal's feet — while the ground sigil, anchored to y=0, stayed correct. `MeasureGateCentre()` instead encapsulates the bounds of the piece's own `MeshRenderer`s and takes the local-space centre, with a plausibility clamp (`0.5 m … 6 m`, else fall back to the tuned constant). That reproduces wood, fixes stone, and costs nothing for black marble or any modded portal. **`GetComponentsInChildren<MeshRenderer>` is deliberate**: `ParticleSystemRenderer` and `LineRenderer` derive from `Renderer`, not `MeshRenderer`, so this can only ever see the arch and never anybody's effects. Measuring happens *before* anything is built, so the only renderers in the hierarchy are the portal's own.

  **Wake is proximity, not a boolean.** The gateway used to be a bool with a fixed ¾-second ramp, which is precisely why it read as a **switch**. `Proximity()` now returns how *close* the nearest player is — full strength inside the first quarter of `VFXWakeDistance`, then a smoothstep out to the edge so the taper does not read as a linear dimmer — and `_wake` chases it with `MoveTowards` at **asymmetric rates**: `1.6` rising (the gate noticing you) and `0.45` falling (a long exhale). `Player.GetClosestPlayer` rather than the local player, so on a shared server a friend walking up lights the gate for you too. Failure returns 1: if the player list is unreadable, a lit portal is the safer failure.

  **Never clear particles on the way down.** This is the other half of the on/off read, and the most transferable rule here. Emission rates fall to zero with `_wake`, and the runes **already in the air live out their own lifetimes** and fade on their own colour curve — exactly how the Wings of the Valkyrie wingtip trail bleeds off when a player stops flying. Renderers are only switched off once the fade has finished *and* every emitter has run dry (`AnyLiveParticles()`), and `SetShown(false)` deliberately does **not** `Stop()` the systems, because their rates are already zero by the time it runs. The earlier code called `Clear()`, which cut the fade off mid-air.

  **Dormant must cost nothing, not merely show nothing.** `SetShown` routes through the same `SetVisible` switch the snapshot camera uses, so the two cannot disagree; if a snapshot restore flips a dormant portal's renderers back on, the next `Update` flips them off again. A portal is **born dormant** (`_wake = 0`, `SetShown(false)` at the end of `Build`), because a portal loading in at the edge of view distance should not run its light show for nobody.

  **Two dials that used to be one.** `VFXIntensity` (`k`) is how *much* gateway there is — emission rates, rune spacing, ring width via `sqrt(k)`. `VFXBrightness` is only how hard it burns. They were one number, so anyone who wanted a dimmer gate got a thinner one too. The composite terms are worth quoting because they are where the two stay separate:

  ```csharp
  float c    = Mathf.Clamp01(Charge) * _wake;                       // activity, gated by presence
  float glow = bright * Mathf.Lerp(0.35f, 1f, _wake) * (1f + 1.2f * c);
  float w    = (1f + 1.8f * c) * _wake * Mathf.Sqrt(k);             // line widths
  ```

  Distance dims as well as thins, but the `0.35` floor means a far-off portal reads as a low ember rather than a black one. The **line rings dim on colour as well as thinning**, because unlike the particles they have no lifetime to fade along and would otherwise still be at full glare at the moment the last rune faded out.

  **`Charge` decays on its own** (`1.4/s`), driven up from the behaviour patch while a player is in the portal's activation range. Self-decay means the effect settles back to idle when the trigger simply stops reporting — leaving the volume, dying, being teleported away — rather than sticking on.

  **The colour band.** A single flat colour read as one solid sheet of green however many glyphs were on screen. `BuildColourBand()` converts `VFXColour` to HSV and derives two ends ±0.055 in hue, one paler and less saturated, one deeper — and every particle picks its own shade from between them via `MinMaxGradient`. A greyscale colour has no meaningful hue to swing around, so the swing collapses to 0 when saturation is under 0.05 and the band varies on saturation and value instead.

  **The swirl trail is a drawn path, not an emitter.** Both swirl systems have `emission.enabled = false` and `startSpeed = 0`; `Update` walks an invisible head around the structure and calls `Emit(1)` at the head's position whenever it has moved far enough. The particles stay where they were dropped, so the ribbon on screen is the **literal path of the head** — it cannot gap at low framerate or clump when the head slows. The head circles at `GateWidth * 1.05` with a slow sinusoidal vertical wander, so the ribbon weaves around the pillars rather than drawing a flat hoop. Its speed rides the charge, so it lazes around a quiet gate and whips around a charged one.

  Three details that are easy to get wrong:
  - **Distance-gated, not time-gated.** `runeStep = max(0.06, 0.55 / (k * dens))` is a spacing in **metres**, so the glyphs stay evenly spaced whatever pace the head is moving at. The floor drops with the dial too — a fixed floor would quietly cap the top of the range.
  - **The particle ceiling must scale with the density dial.** At triple density three times as many runes are alive on the same ribbon, and a fixed `maxParticles = 90` simply drops every one after that, so the tail stops partway along: `cap = clamp(ceil(90 * dens), 90, 400)`.
  - **`_swirlPrimed` is reset whenever the gateway sleeps,** so the head does not draw a chord straight across the gap it slept through when it wakes somewhere else on the circle.

  `Discharge()` — fired the instant the gate takes a player — bursts the veil, motes and sigil runes, and for the swirl walks a full 24-step ring emitting at each position, because the swirl systems are manual-emit and a position-less burst would just pile particles at the portal's root.

  **Textures are generated once for the whole game, not once per portal.** Both generators allocate a fresh `Texture2D` per call and `TortalRuneMaterial` keys its material cache partly on the **texture's instance ID** — so regenerating per instance would miss the cache every single time and leak a material and two textures per portal. The `static Texture2D _atlasTex, _softTex` fields are what make that cache actually work.

  **The 4×4 atlas is what makes one system show sixteen glyphs.** `textureSheetAnimation` with `numTilesX/Y = 4`, `startFrame` random over `0..16` and `frameOverTime` **pinned to 0** — a random glyph frozen per particle, not an animation cycling through the alphabet.

  **`LineRenderer` colours must be set explicitly.** Particle shaders multiply the texture by vertex colour, so a `LineRenderer` left at its default colours renders the runes muddy or invisible. `alignment = LineAlignment.View` keeps thin rings from vanishing edge-on.

  **Rebuild vs. read-live.** Colour and orb height are baked into materials and transforms when the emitters are built, so those dials call `Rebuild()` (teardown + build) on every loaded portal through a `SettingChanged` handler. Brightness, intensity, swirl density and wake distance are read **every frame** and need no handler at all. `OnDestroy` runs the same `Teardown`, which is what stops turning `EnableCustomVFX` off from stranding a dozen still-drawing child objects per portal.

- **Reusable pattern/snippet — emitting at a point in a Local-space system:**
  ```csharp
  ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
  {
      position = localPos,          // interpreted in the system's own simulation space
      applyShapeToPosition = false  // otherwise the shape module overrides it
  };
  ps.Emit(ep, 1);
  ```

- **Reusable gotcha — all three axes of a velocity triple, in the same curve mode:**
  ```csharp
  vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);   // must be written
  vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);   // must be written
  vel.orbitalZ = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
  ```
  Writing only the axis you care about leaves its siblings as *single-constant* curves against a *two-constant* one, and Unity answers with "Particle Velocity curves must all be in the same mode" in the BepInEx log, discards the module's curves entirely and moves nothing.

---

### Stripping and Restoring Vanilla VFX ⭐

- **Purpose:** Switch vanilla's portal particles and connection effects off while the custom gateway is on — **reversibly**, live, per portal.

- **Key files:** `TortalPortal/VFX/TortalVanillaVFXState.cs` (one component per portal), `PortalBehaviorPatch.ApplyVFXMode` / `RefreshLoadedPortals`.

- **Architecture / lessons:**

  **Deactivate, never destroy — and the reason is a cached array.** `EffectFade`, which is what `TeleportWorld.m_target_found` is, caches `GetComponentsInChildren<ParticleSystem>()` into `m_particles` during **its own** `Awake`, then writes `ps.emission.enabled` on every entry from `SetActive`. Unity does not guarantee component `Awake` order, so destroying those systems left `EffectFade` holding destroyed references, and the module setter threw *"Do not create your own module instances, get them from a ParticleSystem instance"* out of `SetActive` **on every tick** — which aborted the `UpdatePortal` prefix before it ever reached the menu code, so the portal menu never opened on approach. Deactivating the GameObject instead keeps every reference valid, renders nothing whatever `EffectFade` does to the emission module afterwards, and is the only reason any of this is reversible.

  The general rule: **never destroy an object another component may have cached a reference to at `Awake`.** Deactivation is almost always the right verb.

  **Record only what you actually changed.** `Strip` keeps a `List<GameObject> _deactivated` and a `List<ParticleSystem> _silenced`, and adds to them only when the thing was *on* when found. A particle system vanilla wanted dark stays dark on restore. The original code kept no record at all, which is why turning `EnableCustomVFX` off did nothing until a world reload — it was not merely unwired, it was *unrecoverable*.

  **Two things must never be deactivated:** the portal root (that hides the portal) and the `EffectFade` host (that stops `EffectFade` updating). Silencing their emission modules is enough.

  **`IsOurs(transform)`** walks up to the portal root looking for the gateway's own children by name prefix (`Rune`/`Sigil`/`Swirl`), stopping when it reaches a transform carrying `TortalPortalRuneVFX`. On a **live** toggle the rune gateway may still be standing when the strip runs, and stripping your own effects is a confusing way to fail.

  `m_connected.m_effectPrefabs` is swapped for an empty array and the original kept for restore — the connection effect is an `EffectList`, not a particle system, so it needs its own handling.

- **The portal frame tint** (`SyncTint` / `ApplyTint` / `RestoreTint`, added 1.1.2) recolours the piece's own emissive panel and point light to `VFXColour`, so a green gateway is not sitting in an orange gate. `SyncTint(portal, wanted)` is the single entry point: it reads the dial itself and settles the portal into paint / repaint / strip-back-to-vanilla, so callers never have to work out which of the three it is, and it early-returns when the portal is already wearing exactly that colour.

  - **Gate on the `_EMISSION` shader keyword, not on a non-black `_EmissionColor`.** This is BlightedWorldHeart's lesson (`BlightedItemsManager.TintMaterial`), and it is the difference between a tint and a mess: plenty of Valheim materials carry a leftover emission colour with the keyword **off**, where the value is inert and the surface does not glow at all. Selecting on the colour repaints timber and stonework that was never lit; selecting on `IsKeywordEnabled("_EMISSION")` lands on exactly the panel that lights up. The keyword is also **never enabled** here, for the same reason — switching emission on for a material that did not have it is what produces that flat pale glow over a whole piece.
  - **Copy the material, never write the shared asset.** `r.sharedMaterials` hands back the prefab's materials; writing to one recolours every portal in the world, including ones this component knows nothing about, with no way back short of a game restart. (BWH tints prefabs at registration, where permanence is the point; here the dial moves at runtime, so ownership matters.)
  - **Replace the hue, keep vanilla's strength.** `power = max(r,g,b)` of the existing emission — taken from the largest channel rather than normalised, because emission is often HDR (>1) — then `emission = colour * power`. A faint glow stays faint and a fierce one stays fierce. A straight **replace** rather than BWH's multiply, because multiplying shifts a colour (right for a blight tint) and orange × green is a murky dark yellow; the ask here is to *match*, so vanilla's hue is discarded and only its brightness kept. The albedo underneath is `Lerp(base, colour, 0.8)` rather than overwritten, or the panel's own shading flattens into a slab; alpha is untouched because it is doing transparency, not brightness.
  - **`ParticleSystemRenderer` and `LineRenderer` both derive from `Renderer`,** so the renderer sweep is exactly where the mod's own gateway and vanilla's effect quads have to be skipped.
  - **Lights get hue only** — intensity, range and shadows untouched, so the portal lights its surroundings exactly as far as it always did.
  - **Unity does not collect materials created from script.** `RestoreTint` destroys every copy it made, and so does `OnDestroy` (the portal being torn down takes the renderers with it, so there is nothing to put back, but the material copies are plain assets that outlive the GameObject). Skipping this leaks one material per glowing panel per portal, *every time the colour dial moves*.

- **Live re-application.** `RefreshLoadedPortals(rebuildGateway)` runs off `SettingChanged` handlers only, so a full `FindObjectsOfType<TeleportWorld>()` scan is paid once per dial turn rather than per frame. The `rebuildGateway` branch has to settle the tint itself, because it skips `ApplyVFXMode` — where the tint is normally settled — and the tint derives from the same colour that forced the rebuild.

---

### TeleportWorld Behaviour Patches: Auto-Open / Auto-Close State Machine

- **Purpose:** The picker opens when the player walks up to a portal and closes when they walk away. It lives in `TeleportWorld.UpdatePortal`, which the mod prefixes and **replaces outright** (`return false`, skipping vanilla target pairing entirely), so this one method also owns the portal's glow, its connection effect and the rune gateway's charge.

- **Key files:** `TortalPortal/PortalBehaviorPatch.cs`.

- **Architecture / lessons:**
  1. **Never key "already handled" state on a bare `bool` when the tick is per-instance.** *Every loaded portal* ticks `UpdatePortal`. A single shared `IsPortalUIPending` bool was cleared each tick by whichever *other* portal the player happened to be out of range of, so the menu re-opened the instant it was dismissed. The fix is to track the **instance**: `private static TeleportWorld _autoOpenedFor`, cleared only when *that same* instance's range check goes false.
  2. **A destination portal will greet the arriving player with its own menu.** The player lands well inside the destination's activation range, so its auto-open fires on the first tick after the trip — indistinguishable from the menu never having closed. Fix: the UI calls `PortalBehaviorPatch.SuppressAutoOpenFor(targetZdo.m_uid)` immediately before `TeleportTo`; when that ZDOID's range check first goes true it claims itself into `_autoOpenedFor` **without showing anything**. Leaving and returning then opens normally.
  3. **Do not clear that suppression from the out-of-range branch.** A player mid-teleport reads as out of range (`IsTeleporting()`), so clearing there races the teleport and hands the menu straight back — the exact thing being suppressed. Scoping it to one ZDOID bounds the downside: the worst case if a trip never completes is one missed auto-open at that one portal.
  4. **Keep cosmetics out of the gameplay path.** `EffectFade.SetActive` reaches into `ParticleSystem` modules and can throw; when it ran inline, one exception aborted the prefix *before* it reached the menu code, so the menu never opened at all. Cosmetic calls now live in their own `try/catch` helper with a `finally` that always advances the state it owns (`hadTarget`).
  5. **Measure the open range HORIZONTALLY.** `m_proximityRoot` sits above the ground while a player's transform is at their feet, so a full 3-D distance always carried about a metre of vertical component — which is why no `MenuOpenDistance` below 1 m could ever be satisfied, however close the player stood, and why the setting's range could not be opened up until this was fixed. Flattening it also makes the setting mean what a player reads it to mean: how far away across the ground.
  6. **Hysteresis, not a single threshold.** While this portal's window is open the close range is `openRange + MenuCloseMargin` (default 1 m), so hovering on the boundary does not strobe the window open and shut. The margin was hard-coded until 1.1.1 and is local-only — how sticky a window is is personal taste, not a rule of the world.
  7. **Two different ranges for two different audiences.** The glow and the connection effect stay on the portal's own `m_activationRange` and fire for **any** player, because they should read the same for everyone regardless of anyone's menu preference; the window is measured against `Player.m_localPlayer` **directly** rather than via `GetClosestPlayer`, because standing at a portal with a friend one step nearer made that call return *them*, and the window never opened for you at all.

---

### Security, Filters and Placement Limits

- **Key files:** `TortalPortal/SecurityPatch.cs`, `FilterPatch.cs`, `PlacementPatch.cs`.

- **PIN storage rides on a ZDO string key, and `SetText` is hijacked to write it.** `TeleportWorld.SetText` (vanilla's tag setter) is prefixed to store the value under `"PortalPIN"` instead and return false — the mod owns naming through its own configuration window, so vanilla's tag path is free to be repurposed. `Interact` is prefixed to open that window instead of vanilla's tag dialog.
- **One shared authorisation check.** `SecurityPatch.CanConfigure(portal)` — creator ID equals this player's profile ID, or `Configuration.IsAdmin()` — is called both from `Interact` and from the destination window's "Name & PIN" button, so the two routes into the configuration window can never disagree about who may use them. It **fails closed**: a portal that cannot be read is not one to offer for reconfiguration.
- **`IsTeleportable` is patched in two places,** `Humanoid` and `Inventory`, with identical logic, because the game asks both. Modes are `Vanilla` (return true, let vanilla run), `BypassAll` and `WhitelistBlacklist`; admins short-circuit to allowed when `AdminBypass` is on. The whitelist/blacklist evaluation order is deliberate — blacklist blocks first, whitelist then *overrides vanilla's own* `m_teleportable` flag for that item, and anything matching neither list falls back to vanilla's flag.
- **Placement limiting is a prefix/postfix pair on `Player.PlacePiece`,** using `__state` to carry "this was a portal and it was allowed" from the prefix to the postfix (which fires the snapshot). `MaxPortalsPerPlayer` is **0 = unlimited** by default; the count comes from `GetAllPortals()` filtered on `ZDOVars.s_creator`, which on a client only counts the portals it can see — an acceptable inaccuracy for a limit that is off by default and mainly enforced on hosts.

---

### Mounted Teleport ⭐

- **Purpose:** Ride a tamed creature through a portal and stay in the saddle on the other side — including with modded saddles and modded mounts.

- **Key files:** `TortalPortal/TamesPatch.cs`, called from `PortalUIManager.ExecuteTeleport`.

- **Architecture / lessons:**
  - **Program against the game's mount *interface*, not the vanilla class.** `player.m_doodadController` is an `IDoodadController`; `GetControlledComponent()` gives the thing being steered. Vanilla's `Sadle` hands back the `Character` itself, a custom controller may hand back its own component sitting somewhere on the creature — so the code takes `as Character` and falls back to `GetComponentInParent<Character>()`. This is what makes OdinMounts-style creatures and custom saddle items work for free.
  - **The one hard requirement is that the controlled thing is a tamed `Character`.** Ships, carts and turrets are also doodad controllers; that check is the entire reason they are never dragged through a portal.
  - **Identify the mount *before* the teleport, act on it *after*.** The controller reference is gone once dismounted, but `TeleportTo` can refuse (cooldown, already teleporting) — and dismounting a player whose trip never happens would just be rude. So `FindMount` runs first, and its results are only used if `TeleportTo` returned true.
  - **You cannot ride through.** While attached, the saddle writes the player's position every physics tick, so the player has to leave the saddle before the trip and be put back after it. `StopDoodadControl()` runs the controller's own `OnUseStop` (release RPC plus `AttachStop` for saddle-shaped controllers); the explicit `AttachStop()` afterwards covers a custom controller that forgot to detach its rider.
  - **Claim ownership before moving any creature.** On a dedicated server a creature is owned by whichever peer simulates it, and moving a ZDO this peer does not own is silently undone at the next sync. Same rule as the snapshot write. This applies to following tames as well as to the mount.
  - **Remount through the saddle's own `Interact`, not by writing state.** `RemountWhenArrived` waits out `IsTeleporting()` (25 s deadline) plus 0.6 s for both bodies to land, checks the mount is within 12 m and alive, and then calls `((Interactable)controller).Interact(player, false, false)` — exactly as if the player had pressed `E` on the saddle. That routes through the owning mod's own request/attach RPCs, so on a busy server the server still arbitrates the seat and whatever rules the saddle enforces still apply. Writing the attach state directly would work in single-player and desync everywhere else.
  - The iterator is split from its risky body (`TryRemount`) because **C# forbids `yield return` inside a `try` block with a `catch` clause** — the same split appears in `ResolveDetails`/`ApplyDetails` and `ServerIndexRoutine`/`Publish`, and is worth recognising as the standard shape rather than rediscovering per coroutine.
  - **Following tames** are collected within `TameFollowRadius` (server-synced, 2–30 m, default 10 — previously hard-coded) and only if `MonsterAI.GetFollowTarget()` is actually this player, so somebody else's wolf standing nearby is not abducted.

---

### tortal_scatter — Test-World Scaffolding

- **Purpose:** Fill a world with portals so the destination list, map markers, filter box, favourites strip and PIN flow can all be exercised without spending an evening building gates. Registered from a postfix on `Terminal.InitTerminal`, behind `isCheat: true` like vanilla's own world-editing commands.

- **Architecture / lessons:**
  - **Instantiate the real prefab; do not hand-roll a ZDO.** The prefab's own `ZNetView.Awake` is what sets persistence, object type, prefab hash and rotation correctly, and a hand-built ZDO that gets any of those wrong is a portal that vanishes on world reload or never replicates. The instance is culled by `ZNetScene` moments later because it is nowhere near the player — and that is *fine*, because `RemoveObjects` keeps a **persistent ZDO** and destroys only the GameObject. The ZDO is the portal as far as the rest of the world is concerned. A scattered portal therefore survives a reload, replicates, photographs itself and can be walked through.
  - **Height must come from `WorldGenerator.GetHeight`, not `ZoneSystem.GetGroundHeight`.** The latter is a physics raycast against a loaded `Heightmap`, and nothing is loaded out where these are going — it would silently hand back the Y it was passed and bury every portal at sea level. `WorldGenerator` is pure maths and works anywhere in the world.
  - **`sqrt(random)` on the radius** keeps the spread even across the disc instead of crowding the middle. Candidates are rejected for water and for landing within 64 m of one already placed (far enough apart that no two share a zone).
  - **Spread the work across frames.** Instantiating a portal builds a whole rune gateway through the mod's own `Awake` patch; four hundred of those in one frame is a visible freeze, so the coroutine yields every 4 and lets the culling keep pace.
  - **Mark what you make.** Every scattered portal gets `zdo.Set("TortalScattered", true)`, so `tortal_scatterclear` finds its own litter and never has to guess which portals somebody actually built.
  - **Clearing has two paths.** If the portal happens to be loaded, `ZNetScene.Destroy` takes the instance and the ZDO together; otherwise the ZDO must be **claimed first**, because `DestroyZDO` is a silent no-op on something this peer does not own. Run on a client, it can only reach what that client knows about, so the command says so rather than reporting a misleadingly small number.
  - Every fifth portal is PIN-locked with `1234`, so the locked-portal paths (inline PIN row, quick PIN modal, wrong-PIN message) all have something to be tested against, and each is named for the biome it landed in so the list, filter box and biome column have real data to chew on.

---

### Config Migration and the Version Gate

- **Key files:** `TortalPortal/ConfigMigration.cs`, the `ConfigSync` block at the top of `Configuration.cs`.

- **The problem migration exists for.** BepInEx binds settings by `(section, key)`. The moment one is renamed or moved to another section, the old line is an orphan: it is not read, it is **stripped out on the next save**, and nothing anywhere says it existed. An admin updates the mod and their whitelist, blacklist and portal limits are simply gone.
- **The shape of the answer** (shared with Fatty's food data file): stamp a layout version in the file, snapshot the raw INI **before a single `Bind`**, back the file up beside itself, plan the moves, then push carried values in after binding and `Save()`. `Begin(config)` and `Finish(config, versionEntry)` bracket the entire `Init`; adding a migration is bumping `CurrentConfigVersion` and adding one entry to `Moves[newVersion]`.
- Three details worth keeping: a value already sitting in the destination **wins** (it was put there deliberately); out-of-range carried values are clamped by BepInEx on the way in via `SetSerializedValue`, so a stale value cannot land somewhere the setting would not otherwise allow; and anything in the old file that is bound nowhere and carried nowhere is **named in the log with its value**, alongside the backup path, rather than disappearing quietly.
- A failed migration must never stop the mod loading — every stage is wrapped, and the worst case is "the config binds exactly as it always would have".
- **`MinimumRequiredVersion` is a UX decision, not a wire-format one.** It sat at `1.0.1` — the version that introduced the portal index, whose format has not changed since — on the reasoning that an older client merely misses features it does not have. 1.1.2 raises it to the current version, because that reasoning holds for the wire format and not for the *experience*: 1.1.x changed what clicking a map marker does, when the destination window opens and whether it captures the keyboard. A mixed table produces two players describing different behaviour and neither of them wrong, which is far harder to diagnose than a refused connection. Raising it is a breaking change and every client must update alongside the server.

---

### Cross-cutting IMGUI gotchas (consolidated)

Hard-won; all cost real debugging time.

| Gotcha | Detail |
|---|---|
| **`FilterMode` name collision** | The mod defines its own `FilterMode` enum (item filters) in the same namespace, so `UnityEngine.FilterMode` **must** be spelled out in full. Symptom: `CS0117: 'FilterMode' does not contain a definition for 'Bilinear'`. `TortalRuneTexture.cs` and `TortalPinIcon.cs` qualify it for the same reason. |
| **`Arial.ttf` is not a builtin resource any more** | `Resources.GetBuiltinResource<Font>("Arial.ttf")` returns null on the Unity version Valheim now ships. A **null** `GUIStyle.font` is fine — IMGUI falls back to `GUI.skin.font` — so returning null is the correct failure mode, not an error. |
| **Borrowing the game's font** | `Resources.FindObjectsOfTypeAll<Font>()` then substring-match `"AveriaSerifLibre"` / `"Norsebold"` / `"Norse"`. Only returns *legacy* `Font` assets (not `TMP_FontAsset`), so it may find nothing — hence the null fallback above. Cache the result; do not call per frame. |
| **Texture row 0 is the bottom** | Design shapes in screen-space (y down), then flip at bake: `row = (h - 1 - y)`. Getting this wrong flips every asymmetric profile. The heart is the exception — its curve is authored y-up, so it must *not* flip. |
| **`GUI.DrawTexture` is Repaint-only** | It no-ops outside `EventType.Repaint`, so no manual event guard is needed around pure drawing. Controls placed with explicit `Rect`s, by contrast, work in all event types — which is why explicit-rect layout is safe to abandon mid-frame and `GUILayout` is not. |
| **Overlapping controls both fire** ⭐ | There is no z-order-based hit-test in IMGUI. This is not one bug, it is a class of them: it is why the modal suppresses the picker rather than covering it, why the favourite heart gets its own non-overlapping strip of the row, and why the album's page arrows need an explicit hover test against the full-screen dismiss button. |
| **Read `Return`/arrow keys before drawing the field** | A focused IMGUI text field swallows the `KeyDown` once it has seen it, so any "Enter submits from anywhere in this window" handling must run at the top of the draw method and call `ev.Use()`. |
| **An auto-focused text field silently kills the game's `Use` key** ⭐ | `Player.Update` reads `bool flag2 = TakeInput();` and puts `ZInput.GetButtonDown("Use")` **inside** that branch — and `TakeInput()` consults `Chat.HasFocus()`, which this mod postfixes to report its own field focus. So holding focus does not just block movement, it blocks **`E`**, and `Interact` is never called on anything. Symptom is deceptive: it looks *intermittent*, because clicking any non-field control drops focus and the next `E` works. **Rule: anything reachable by `E` while one of your windows is open needs an on-screen button instead** — hence the "Name & PIN" button. As of 1.1.2 the search box is also no longer focused on open (`FocusSearchOnOpen`, default **off**), because the destination window opens while the player is still walking and a focused field turned the rest of the run-up into `wwwww` in the filter. |
| **Valheim only frees the cursor for windows it knows by name** | `GameCamera.UpdateMouseCapture` names the map, inventory, store and barber. An IMGUI window is on nobody's list, so it opens with the cursor locked to the camera and invisible — the fields are right there and cannot be clicked. A postfix that forces `Cursor.lockState = None; Cursor.visible = true` while `WantsCursor()` is the last word. (The destination window got away with it only because it opens the large map, which *is* on that list.) |
| **IMGUI clicks fall straight through onto uGUI** ⭐ | The two input systems know nothing about each other. Every click on an IMGUI window over the large map also reaches the map. Blocking has to be done at the *game's* handlers, and blocking the **down** handler is worth more than blocking the click, because click, double-click and drag are all derived from it. |
| **`anchoredPosition` is canvas units, not screen pixels** | Moving a vanilla uGUI HUD element (e.g. nudging `MessageHud.m_messageCenterText` clear of an IMGUI window) by a pixel offset computed from `Screen.width` needs dividing by `canvas.scaleFactor` — Valheim's HUD canvas scales with resolution and UI scale, so the two only coincide at scale 1. Untreated it lands correctly at 1080p and drifts badly at 4K. Capture the element's rest position lazily on first move and key it to the transform you read it from: a world reload builds a fresh HUD, and restoring a stale position onto the new object parks it somewhere wrong. Re-apply every frame — the message is laid out again as it cross-fades. |
| **Vanilla re-writes what it owns, every frame** | `Minimap.UpdatePins` assigns each pin's icon colour unconditionally, so a tint set beforehand is gone in the same frame. Postfix it. The corollary is pleasant: unselected markers need no reset, because vanilla has just repainted them itself. |
| **Boolean vanilla knobs have exactly two values** | `PinData.m_doubleSize` offers "normal" and "twice normal" and nothing between. When a vanilla system owns one property, drive a sibling it never reads — here `m_uiElement.localScale`. |
| **Claim ownership before writing a ZDO you did not create** | Writes to an unowned ZDO are silently discarded at the next sync. Applies to snapshots, to moving tames and mounts, and to `DestroyZDO`. Silent is the operative word: nothing throws, the value simply is not there a moment later. |
| **Script-created materials are never collected** | `new Material(m)` leaks unless something destroys it. Destroy on restore *and* in `OnDestroy` — the GameObject going away does not take the material with it. |
| **Never destroy what another component cached at `Awake`** | `EffectFade` caches its `ParticleSystem[]` in `Awake` and writes to it every tick. Destroy those systems and you get "Do not create your own module instances…" thrown from *its* code, on every tick, aborting whatever called it. `SetActive(false)` achieves the same visual result with none of the risk — and can be undone. |
| **Animated bands, if you ever want one** | `GUI.DrawTextureWithTexCoords(rect, tex, new Rect(offset, 0, tilesX, 1))` with a `TextureWrapMode.Repeat` texture and `offset = Mathf.Repeat(Time.unscaledTime * pxPerSec / texWidth, 1f)` scrolls a tiling strip. Wrap the offset — an unbounded accumulator loses float precision over a long session. (Considered here, then dropped: the reference frame is static, and a travelling highlight on period metalwork reads as a web banner.) |
| **`HideFlags.HideAndDontSave`** | Mandatory on any runtime-generated texture an `OnGUI` or a map sprite will keep using across scene loads — and it makes the texture immortal, so anything that regenerates the set must destroy the old one explicitly. |
| **Guard on the object, not a bool** | `if (_tex != null) return;` — "already built" ≠ "still alive" for `UnityEngine.Object`s. |
| **`yield return` cannot sit in a `try`/`catch`** | Split the risky body into an ordinary method the iterator calls. `ResolveDetails`/`ApplyDetails`, `RemountWhenArrived`/`TryRemount` and `ServerIndexRoutine`/`Publish` are all this pattern. |

---

## Suggested follow-ups

- **Delete or finish `VFXPatch.cs`.** It is vestigial: it tries to load a `MistsOfAvalorBundle` from beside the plugin and, failing that, from a **hard-coded absolute path** into another project's `HexiumDist`. Neither exists in a shipped build, so the prefab is null and its `TeleportWorld.Awake` postfix returns immediately — but the class is still registered in `Plugin.PatchTypes`, and if a bundle ever *were* present it would `SetActive(false)` every particle system on the portal with no record and no way back, which is exactly the failure `TortalVanillaVFXState` was written to fix. Removing it also removes the last line in the codebase suggesting a `MistsofAvalor` dependency that does not exist.
- ✅ **Partially done 2026-08-04:** a parameterised canonical copy now exists at `libs-Tools/SharedUI/GiltFrameTheme.cs` (`EnsureBuilt(Color goldColour, float textScale, int fontSizeDelta)` instead of reading the three `Configuration` fields directly) — built for `BarrkUI`, the first consumer via `<Compile Include>`. **This file (`UI/TortalUITheme.cs`) has NOT itself been migrated** to delete its local copy and reference the shared one — that would touch this mod's shipped build for no functional gain and wasn't done as part of the promotion. Fatty's copy (`Patches/GiltFrameTheme.cs`) is likewise still local, and is already visibly behind this file (no live gold-recolour, no favourite heart, no `DrawDot`/`ImageButton`) — exactly the drift this note originally warned about. See `SharedInfrastructure.md`'s `SharedUI/` entry for the full promotion writeup. Njord's telemetry HUD still uses unstyled IMGUI and remains a candidate to adopt the shared theme.
- **Style the scroll bars.** The portal list still uses the default `GUI.skin` scrollbar, which is off-theme; it needs three more 9-slice patches and a `BeginScrollView(pos, hStyle, vStyle)` overload.
- **`ConfigMigration.Moves` has never carried a real move** (`{1, new Move[0]}` only stamps the file). The machinery is unexercised in anger; the first genuine rename is worth testing against a hand-written 1.0.0 config rather than trusting it cold.
- **The portal index does not shrink gracefully.** `Encode` writes every portal in the world into one base64 blob on every publish; on a long-lived server with hundreds of portals that is a full re-broadcast every time anything changes. A dirty-set or a hash-and-delta would be the next step if anyone reports it.
