# Headless & Empty-Server Facts — four traps that fail silently

**Applies to every project in `c:\WubarrkCODING`.** Written 2026-08-13 after all four shipped live in
AwayFromHome 1.1.0 at once. Every one of them compiles clean, logs nothing, and looks like a *different*
bug than it is — which is why each cost hours rather than minutes.

The pattern they share: **a call that works on your desktop returns a plausible wrong answer somewhere
else** — on a GPU-less build agent, or on a server with nobody logged in.

---

## 1. `-nographics` silently destroys `Graphics.Blit` + `ReadPixels`

**The trap.** The standard "resize/convert a texture in an editor script" idiom is

```csharp
RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, ...);
Graphics.Blit(src, rt);
RenderTexture.active = rt;
tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);   // <-- returns garbage under -nographics
```

Under `Unity.exe -batchmode -quit -nographics` there is no graphics device, so the blit never happens and
`ReadPixels` reads an empty target. **No exception. No warning. Exit code 0.** You get a texture of the
right dimensions, right format and right name, containing nothing.

**What it cost.** AwayFromHome's Keeper Stone shipped with blank albedo and normal maps for its entire
1.1.0 life. In game the model rendered as a pale, untextured blob. It was reported three separate times
as "the model looks like crap" and chased through the *material* every time — tint, emission, rune glow,
metallic/gloss, shader property dumps — because the material was where a colour problem obviously lives.
The client log said `_MainTex=keeperstone_albedo` bound successfully, which was true and irrelevant: the
texture bound fine, it just had nothing in it.

**The tell is file size.** The bundle was 381 KB for a 2048² albedo *plus* a 2048² normal *plus* an
11k-vert mesh. Rebuilt with a graphics device: **4,779 KB — 12.5×**. Detailed textures do not crunch to
nothing; blank ones do.

**Same call, same day, different disguise.** The same `Blit`/`ReadPixels` path was also used to *measure*
textures, where it returned byte-identical statistics across three different implementations and reported
a visibly black texture as "100% lit". That was correctly diagnosed and abandoned — and nobody checked
whether the identical call was also in the path that produced the shipped asset. **If you catch this in
one place, grep the whole file for the other callers.**

**The fix.** Either bake on the CPU (`TextureImporter.isReadable = true` then `GetPixels()`, with a CPU
downscale), or refuse to run at all:

```csharp
if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
    throw new Exception("Bundle bake needs a graphics device. Drop -nographics from the Unity command line.");
```

The guard is worth having even if you fix the bake, because the failure is invisible without it.

**And check the copy step.** A bundle rebuilt in `Unity\AssetBundles\` does not reach the DLL until it is
copied to wherever the csproj's `<EmbeddedResource>` points. A good bake plus a stale copy ships the old
broken asset and the DLL size does not move.

---

## 2. A dedicated Valheim server stops its world clock while empty

`ZNet.UpdateNetTime`, in effect:

```csharp
if (IsServer()) { if (GetNrOfPlayers() <= 0) return; m_netTime += dt; return; }
m_netTime += dt;
```

**With zero players connected, world time does not advance at all.** Anything measured against it is
frozen: `Smelter.GetDeltaTime()` is `ZNet.GetTime() - ZDOVars.s_startTime`, so smelters, kilns, blast
furnaces, spinning wheels, windmills and eitr refineries make **zero** progress on an empty server, no
matter who owns them or how long they stay loaded.

**Why it hides.** `Tameable` counts down on **real frame time**, not world time. So taming keeps climbing
perfectly while production sits dead — which reads as a scheduling or ownership bug and sends you looking
in entirely the wrong place. If animals advance but machines do not, suspect this immediately.

**Fixing it without touching the global clock:** rewind each piece's own `s_startTime` by the time the
clock failed to pass, and let vanilla's own code do the arithmetic under its own rules (including its
3600s per-gap ceiling). Patching `m_netTime` globally would run day/night on an empty server, which is
precisely what the freeze exists to prevent. Compute the correction as
`realElapsed - vanillaDelta` — it self-cancels to zero whenever players are online, so it needs no "is
the server empty" test and cannot double-count.

---

## 3. Valheim never releases a persistent ZDO when its owner disconnects

`ZDOMan` only reassigns **non-persistent** ZDOs (`RemoveOrphanNonPersistentZDOS`). A smelter a player
loaded with ore keeps that player's dead session id **forever**. Every vanilla loop is gated on
`m_nview.IsOwner()`, which is then false for everyone alive, so the object stops completely.

Vanilla does not care, because with no player nearby the zone is unloaded anyway. **Any mod that keeps
areas simulated with nobody present inherits this as a showstopper**, and it bites hardest on exactly the
sites a player just finished setting up.

**The staleness test** — `zdo.GetOwner()` against `ZDOMan.GetSessionID()` and every
`ZNet.GetPeers()[i].m_uid`. Vanilla's own private `ZDOMan.IsPeerConnected` does the same comparison,
which is also the proof that ZDO owner ids and `ZNetPeer.m_uid` share one id space (they are *not*
obviously the same thing — `ZDOMan.m_sessionID` is generated separately, so verify before trusting it).

Reclaiming from a **disconnected** session does not violate "never fight a live player" — it states the
rule precisely. A connected player's claim stays untouchable.

---

## 4. `transform.position` does not move a non-kinematic Rigidbody

Valheim creatures are non-kinematic rigidbodies with interpolation enabled. Assigning
`view.transform.position` is **silently reverted from the body on the next physics step**. Use
`rb.position` (and zero `velocity`/`angularVelocity` if you don't want it to resume its previous motion).

**How it presented.** Livestock-pinning code logged `put 3 of 3 animals back (furthest had drifted 97m)`
for weeks and never moved anything. The logs asserted success the whole time. It was only caught because
the same animal was reported at an **identical distance to 0.1 m** on four consecutive checks — organic
movement is never that repeatable.

**Corollary for measurement:** if you add a speed/jump detector afterwards, remember the teleport itself
is the fastest thing on the map. Record the corrected position, not the pre-move one, or every successful
correction reports itself as an anomaly.

---

## The meta-lesson

All four produce **confident, well-formed, wrong output**. A log line saying `_MainTex=keeperstone_albedo`
is true and tells you nothing about whether the texture has pixels. `put 3 of 3 animals back` is emitted
whether or not anything moved. `accTime` is a real field that is not the field you want (it is
`UpdateSmelter`'s sub-second remainder, always 0–1s; the progress field is `s_bakeTimer`).

When a symptom survives several confident fixes, **stop fixing and go validate the instrument.** In
AwayFromHome the whole day turned around within minutes of the telemetry reporting `bakeTimer` and
ownership instead of `accTime` — both showstoppers fell straight out.

See also: `IMPLEMENTATIONS/AwayFromHome.md`, `TEXT-ENCODING-FACTS.md`.
