# Njord — Technical System Report

## Overview

Njord is a Valheim BepInEx/Harmony mod that overhauls ship sailing feel: it intercepts vanilla `Ship.CustomFixedUpdate` and `Ship.ApplyControlls` to replace Valheim's stock sail-force and steering math with a fully custom, config-driven propulsion model (acceleration curves, per-sail-state force multipliers, per-ship-type speed caps, and a "wind assist" system that smooths/cheats the effective wind angle toward favorable headings). It layers a server-authoritative helm-ownership/ZDO-authority system on top (so only the controlling player's client applies physics forces), a draggable IMGUI telemetry HUD that displays live ship physics/steering/water data, and a procedurally-generated "rune" VFX trail (pooled sprites drawn as Nordic runes) that streams from a moving ship's stern and bursts on state changes.

Source folders inspected: `Core/`, `Debug/` (real source, not build output — contains `NjordDebug.cs` and `Njord_Telemetry.cs`), `HUD/`, `Patches/`, `VFX/`, plus `Plugin.cs` and `Njord.csproj`.

---

### 1. Server-Authoritative Ship Control/Ownership System

- **Purpose:** Determines which single player "controls" a ship at any moment (i.e. is allowed to apply propulsion forces to its `Rigidbody`), and transfers Valheim `ZDO` network ownership to that player so physics stay authoritative and don't desync in multiplayer. Also drives HUD visibility.
- **Key files:** `Core/NjordAuthorityEngine.cs`, `Core/Njord_ShipControlTracker.cs`, `Patches/NjordDoodadControlRune.cs`, `Patches/Njord_ZNet_Awake_Patch.cs` (contains `Njord_PlayerSpawned_Init`, despite the filename).
- **Architecture:**
  - `NjordAuthorityEngine` is a static server-only authority map (`Dictionary<ZDOID,long> ServerAuthorityMap`) keyed by ship `ZDOID` → controlling player's `long` player ID (0 = uncontrolled).
  - It registers 5 custom RPCs on `ZRoutedRpc.instance`: `Njord_RequestControl`, `Njord_ReleaseControl`, `Njord_RequestAuthorityState`, `Njord_AuthorityUpdate`, `Njord_AuthoritySyncAll` — all with explicit `Action<...>` delegate signatures matching RPC args (`long sender, ZDOID, long` etc.).
  - Client → calls `ZRoutedRpc.instance.InvokeRoutedRPC("Njord_RequestControl", shipZdoid, playerId)` (routes to server, server ID unknown to client — uses default routed-to-server semantics for non-`Everybody` target when called from a client).
  - Server → on `RPC_RequestControl`, updates the authority map, calls `zdo.SetOwner(sender)` (transfers ZDO network ownership to the requesting peer for physics authority), then broadcasts `Njord_AuthorityUpdate` to `ZRoutedRpc.Everybody`.
  - Every peer's `RPC_AuthorityUpdate` handler resolves the `Ship` from the `ZDOID` via `ZDOMan.instance.GetZDO()` → `ZNetScene.instance.FindInstance()` → `GetComponent<Ship>()`, then calls `Njord_ShipControlTracker.SetControl`/`ClearControl`.
  - `Njord_ShipControlTracker` is the **client-local read model**: `ControlMap` (ZDOID→playerID), plus per-ship dictionaries for wind-angle state, grace timers, and "was in dead zone" flags (all keyed by `ZDOID`, all lazily created). It exposes `CurrentController`, `ShouldShowHud` (recomputed whenever control changes: `hasPlayer && hasControl && hudEnabled`), and a `SteeringGateReady`/`ReadyTimer` readiness gate (0.35s delay, `READY_DELAY` const) that prevents physics/HUD from reacting instantly on ownership handshake to avoid a frame of bad state.
  - `Patches/NjordDoodadControlRune.cs` has two Harmony patches: postfix on `Player.StartDoodadControl` (fires when a player grabs the helm) and postfix on `ShipControlls.OnUseStop` (fires on release). Each branches on `ZNet.instance.IsServer()`: server calls `Server_OnHelmClaim/Release` directly; client calls `Client_RequestControl/ReleaseControl` (RPC to server). Both **also** apply `Njord_ShipControlTracker.SetControl/ClearControl` locally immediately (optimistic local update for instant responsiveness) and immediately call `shipNview.ClaimOwnership()` on the local `ZNetView` for fast-path ownership.
  - `UseLegacyFallback` flag: if `ZRoutedRpc.instance` is null at `Initialize()` time, the whole authority RPC system is disabled and the mod presumably falls back to purely local/optimistic control tracking (no true server arbitration) — a defensive degradation path.
  - Initialization is deliberately delayed: `Njord_PlayerSpawned_Init` (Harmony postfix on `Player.OnSpawned`) starts a coroutine that yields 4 frames (`yield return null` × 4) before calling `NjordAuthorityEngine.Initialize()`, to let ZDOs/ZNetScene/world state settle first.
- **How to implement (step-by-step recipe):**
  1. Define a static class holding `Dictionary<ZDOID, long>` for server-side authority.
  2. In an `Initialize()` guarded by a `_initialized` bool, register RPCs via `ZRoutedRpc.instance.Register("Name", new Action<...>(Handler))` — signature must exactly match invocation args (first arg is always `long sender`).
  3. Guard registration: if `ZRoutedRpc.instance == null`, set a fallback flag and return early — RPC subsystem isn't ready at all load times.
  4. Call `Initialize()` from a delayed hook (e.g., a coroutine off `Player.OnSpawned` postfix, waiting a few frames) rather than directly in `Plugin.Awake()`, since `ZRoutedRpc`/`ZNet`/`ZDOMan` may not exist yet.
  5. On the "claim" Harmony patch target (for ships: `Player.StartDoodadControl` postfix, checking `shipControl is ShipControlls`), branch: if `ZNet.instance.IsServer()`, mutate the map directly and broadcast; else invoke a routed RPC to request control.
  6. Server RPC handler: validate, update map, call `ZDO.SetOwner(senderPeerId)` to hand off network ownership (critical: this is what makes Valheim replicate the ship's transform authoritatively from that peer), then re-broadcast state via `ZRoutedRpc.Everybody`.
  7. Every client's broadcast handler resolves the target object from `ZDOID` (`ZDOMan.instance.GetZDO(id)` → `ZNetScene.instance.FindInstance(zdo)` → `GetComponent<T>()`) and updates a local read-model class.
  8. Also apply the state change **optimistically and locally** at the same call site that triggered the RPC (don't wait for the round trip) so the initiating client feels no input lag; the RPC path exists to keep *other* clients in sync.
  9. Add a release patch (`ShipControlls.OnUseStop` postfix) mirroring claim, but validate that the releasing player is actually the current controller before releasing (prevents a stale/duplicate release from another peer stealing control).
  10. Gate any per-frame physics/logic that must be single-writer (only one client should `AddForce` to a shared `Rigidbody`) behind `controller == localPlayerId` checks read from the local tracker, and as a safety net re-claim `ZNetView` ownership if the local controller doesn't already own the ZDO (`!nview.IsOwner()` → `nview.ClaimOwnership()`).
- **Reusable pattern/snippet:**
```csharp
ZRoutedRpc.instance.Register("Mod_RequestControl", new Action<long, ZDOID, long>(RPC_RequestControl));
// ...
private static void RPC_RequestControl(long sender, ZDOID id, long playerID) {
    if (!IsServer()) return;
    ServerAuthorityMap[id] = playerID;
    var zdo = ZDOMan.instance?.GetZDO(id);
    zdo?.SetOwner(sender);                       // hand off ZDO/network authority
    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "Mod_AuthorityUpdate", id, playerID);
}
```

---

### 2. Ship Propulsion & Wind-Assist Physics System ("SeaDog Engine")

- **Purpose:** Njord's core gameplay system — fully replaces vanilla Valheim ship propulsion with custom acceleration/force curves, sail-state multipliers, per-ship-type hard speed caps, and an optional "wind assist" that smooths the boat's effective wind angle toward favorable headings (up to always-perfect tailwind) instead of vanilla's raw wind-angle-based sail efficiency. This is the "rigidbody physics" system — note it does **not** implement custom buoyancy (that stays 100% vanilla `Floating`/`WaterVolume`); it only reads water level for telemetry/VFX and applies forward/reverse thrust as `Rigidbody.AddForce` impulses plus a hard velocity clamp.
- **Key files:** `Patches/NjordSeaDogEngine.cs`, `Core/NjordBoatController.cs`, `Core/NjordShipSpdMAX.cs` (`NjordShipSpeed`), `Core/NjordSailState.cs`, `Core/NjordRuntimeConfig.cs`, `Core/NjordConfig.cs`, `Patches/Patch_Ship_ApplyControlls.cs`.
- **Architecture:**
  - `NjordSeaDogEngine` is a manual/reflection-targeted Harmony patch: `[HarmonyPatch]` with a `static MethodBase TargetMethod()` that resolves `Ship.CustomFixedUpdate` via `AccessTools.TypeByName("Ship")` + `AccessTools.Method(shipType, "CustomFixedUpdate")` (rather than a compile-time `[HarmonyPatch(typeof(Ship), "CustomFixedUpdate")]` — likely done because the method may be private/internal or version-sensitive). A `Prefix` runs every physics tick.
  - Inside the prefix, it reflects private `Ship` fields via `AccessTools.Field` (`m_nview`, `m_body` as `Rigidbody`, `m_lastDepth`, `m_sailForceFactor`, `m_sailForceOffset`, `m_backwardForce`, `m_sailForce`, `m_rudder`/`m_rudderValue`) — this is the standard technique for touching private vanilla fields from a Harmony patch without a public API.
  - Guard chain: bails if `nview` missing/invalid, `Rigidbody` missing/NaN/zero mass, or `m_lastDepth <= -9000` (Valheim's sentinel for "not in water / uninitialized").
  - Reads current controller via `Njord_ShipControlTracker.GetControllerForShip`, computes `playerIsControlling` (controller == local player ID), and calls `UpdateController` to advance the readiness gate.
  - **Wind assist logic:** computes `naturalAngle` from vanilla `ship.GetWindAngle()` (via `Mathf.DeltaAngle(0, angle)`), then depending on config (`Wind_AlwaysFull`, `Wind_NoDeadZone`, `Wind_BlendToFull`) computes a `targetAngle`. A per-ship "dead zone" (>130° from tailwind) and grace-period timer (tracked per-`ZDOID` in the tracker) prevent instant snapping. The angle is smoothly interpolated every physics tick with `Mathf.MoveTowardsAngle(current, target, Wind_BlendRate * dt)` and stored back per-ship. From the resulting angle a `windThrottle` multiplier (0..1) is derived via `Mathf.Lerp` bands (full below 90°, tapering 90–130°, zero beyond 130°).
  - **Force nullification:** vanilla sail-related private fields are zeroed every tick (`m_sailForceFactor=0`, `m_sailForceOffset=0`, `m_backwardForce=0`, `m_sailForce=Vector3.zero`) so vanilla's own force application (which runs later in the same vanilla method, since this is a `Prefix` not a full replace) contributes nothing — Njord fully substitutes vanilla thrust while letting the rest of `CustomFixedUpdate` (buoyancy, drag, wave forces, etc.) run untouched.
  - **Custom force calc:** reads `GetSpeedSetting()` (Stop/Slow/Half/Full/Back) via reflection, maps to `NjordSailState` enum, looks up baseline forward force from config (`NjordBoatController.GetBaseline`), applies acceleration multiplier, sail-state multiplier (`GetSailMult`), and — only for Half/Full — either the wind throttle (system on) or vanilla's own `ship.GetWindAngleFactor()` read *before* being nulled (system off, so at least vanilla wind efficiency is preserved). Final `forwardForce`/`reverseForce` are NaN/Infinity-scrubbed (`Safe()` helper) and clamped to `[0, 2000]`.
  - **Force application:** only if `playerIsControlling`, applies `body.AddForce(direction * force * body.mass * fixedDeltaTime, ForceMode.Impulse)` where direction is `tf.forward` (or `-tf.forward` for Backward state) — this is the single-writer rule enforced by the authority system above.
  - **Hard speed cap:** after force application, clamps `body.linearVelocity` to a per-ship-type max speed (from `NjordShipSpeed.GetMaxSpeedForShip`, matched by substring on the cleaned prefab name, e.g. `"raft"`, `"karve"`, `"longship"`, `"drakkar"`, plus optional OdinShip/OdinShipPlus ship names gated by mod-detection flags).
  - **VFX + telemetry hooks** fire from the same tick (see VFX and HUD sections) — VFX trigger processing (`NjordSeaDogEngineVFXTriggers.Process`) runs for **every client** (not gated by `playerIsControlling`) so trails render for everyone watching the ship, while telemetry (rate-limited to every 0.2s via `_telemetryTimer`) only updates for the controlling client.
  - `Patch_Ship_ApplyControlls` is a separate simple Harmony postfix on `Ship.ApplyControlls` that reads the vanilla rudder value (again via reflected `m_rudder`/`m_rudderValue`), multiplies by `SteeringMultiplier` config, and reports both to `Njord_Telemetry` — but notably **does not appear to write the multiplied value back to the ship** (it's postfix, read-only observation feeding the HUD's "Vanilla vs Njord" steering comparison, not an actual steering multiplier application point — verify against your own goals before assuming this patch changes rudder physics).
- **How to implement (step-by-step recipe):**
  1. Use `[HarmonyPatch]` with a `static MethodBase TargetMethod()` override instead of attribute-based targeting when the target type/method might be private, renamed across game versions, or you want defensive null-checks with logging if resolution fails.
  2. In the prefix, use `AccessTools.Field(type, "privateFieldName")` to reflect into private vanilla fields — cache `Type` via `__instance.GetType()`, not a static `typeof()`, to stay resilient to subclassing.
  3. Zero out the vanilla force-contributing fields *before* falling through to the original method body (since this is a Prefix, the original still runs after and would otherwise add its own force on top of yours).
  4. Build your own force model as a pure function of config values + a discrete state enum (sail position) — keep this logic in a separate static helper class (`NjordBoatController`-equivalent) so it's unit-testable and decoupled from Harmony/reflection plumbing.
  5. Scrub every computed float for NaN/Infinity before using it in `Rigidbody.AddForce` — a single bad ship (e.g. mid-destruction, zero mass) will otherwise propagate NaN into the physics world and corrupt other objects.
  6. Only the client that "owns" the ship (per your authority system) should call `AddForce`; everyone else should skip force application but may still run cosmetic logic (VFX, telemetry display).
  7. After applying force, directly clamp `Rigidbody.linearVelocity` (not just force) to enforce a hard speed ceiling — force-based caps alone allow overshoot on a single frame.
  8. Wire a wind-angle smoothing system as a per-object (per-`ZDOID`) piece of mutable state stored in a dictionary outside the Rigidbody, since you can't easily add fields to the vanilla `Ship` class — `Mathf.MoveTowardsAngle` per-tick with a configurable degrees/sec rate avoids instant/jarring snaps and works cleanly with `Mathf.DeltaAngle` for angle wraparound.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch]
public static class MyShipForcePatch {
    static MethodBase TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("Ship"), "CustomFixedUpdate");

    static void Prefix(object __instance, float fixedDeltaTime) {
        var shipType = __instance.GetType();
        var body = AccessTools.Field(shipType, "m_body").GetValue(__instance) as Rigidbody;
        AccessTools.Field(shipType, "m_sailForceFactor").SetValue(__instance, 0f); // nullify vanilla
        float force = Safe(ComputeMyForce(...));
        body.AddForce(transformForward * force * body.mass * fixedDeltaTime, ForceMode.Impulse);
        if (body.linearVelocity.magnitude > maxSpeed)
            body.linearVelocity = body.linearVelocity.normalized * maxSpeed;
    }
}
```

---

### 3. Per-Ship-Type Speed Cap & Soft Mod-Compatibility Detection

- **Purpose:** Applies a distinct configurable max speed per ship prefab (vanilla Raft/Karve/Longship/Drakkar, plus optional entries for two other community mods' custom ship prefabs), and demonstrates a pattern for detecting/soft-integrating with other BepInEx mods without a hard reference/dependency.
- **Key files:** `Core/NjordShipSpdMAX.cs` (`NjordShipSpeed` class), `Plugin.cs` (detection flags), `Core/NjordConfig.cs` (conditional config sections).
- **Architecture:**
  - `Plugin.Awake()` checks `BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("marlthon.OdinShip")` / `"marlthon.OdinShipPlus"` and stores results in public static bools `OdinShipDetected`/`OdinShipPlusDetected` — this requires **no compile-time reference** to the other mod's assembly at all.
  - `NjordConfig.Bind()` wraps ~15 extra `MaxSpeed_*` config entries in `if (Plugin.OdinShipDetected || ...)` / `if (Plugin.OdinShipPlusDetected)` blocks so config files stay clean when those mods aren't present.
  - `NjordShipSpeed.GetMaxSpeedForShip(Ship ship)` cleans the ship's GameObject name (`.Replace("(Clone)", "").Trim().ToLowerInvariant()`) and does cascading `string.Contains(...)` matches against known prefab name fragments (`"raft"`, `"karve"`, `"mercantship"`, `"bigcargoship"` before `"cargoship"` to avoid the shorter substring matching first, etc.) to select which config entry's `.Value` to return. Order matters for substring collisions.
- **How to implement (step-by-step recipe):**
  1. To detect another mod without referencing its DLL: `BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("<author>.<pluginname>")` (the BepInPlugin GUID string) inside your own plugin's `Awake()`.
  2. Store the result in a public static bool other classes/config binders can read.
  3. Gate any config entries or logic paths specific to that integration behind the flag, checked at `Bind()`/`Awake()` time (config schema is static per session — this doesn't support hot-detection of a mod loaded later).
  4. For per-prefab-type behavior without hard type references to a foreign mod, match on `GameObject.name` (strip the `"(Clone)"` Unity instantiation suffix) case-insensitively with ordered substring checks, most-specific-first.
- **Reusable pattern/snippet:**
```csharp
bool otherModPresent = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("author.pluginid");
// ...
string cleanName = obj.name.Replace("(Clone)", "").Trim().ToLowerInvariant();
if (cleanName.Contains("bigcargoship")) return cfgBig.Value;   // check longer/more specific first
else if (cleanName.Contains("cargoship")) return cfgSmall.Value;
```

---

### 4. HUD Telemetry Overlay (IMGUI)

- **Purpose:** A draggable, toggleable on-screen debug/status panel showing live ship physics state (water level, depth, hull HP bar, steering comparison, speed comparison, wind blend) — visible only to the player currently controlling a ship.
- **Key files:** `HUD/NjordHudManager.cs`, `HUD/NjordReleaseIndicator.cs` (present but **unreferenced/dead code**), `Debug/Njord_Telemetry.cs` (data source), `Core/Njord_ShipControlTracker.cs` (visibility gate), `Plugin.cs` (`OnGUI` entry point, position/enabled config).
- **Architecture:**
  - Rendering technique is **legacy Unity IMGUI** (`OnGUI`), not UI Toolkit or uGUI/Canvas. `Plugin.OnGUI()` (a Unity message on the `BaseUnityPlugin` MonoBehaviour) calls `NjordHudManager.OnGUI()` every IMGUI event/frame.
  - `NjordHudManager` holds a `Rect _hudRect` (position from `Plugin.HudPositionX/Y` config, fixed `WIDTH=360, HEIGHT=570`), builds `GUIStyle`s lazily once (`CreateStylesIfNeeded`, includes 1×1-pixel `Texture2D`-backed styles for an HP bar background/fill), and early-outs entirely if `!Njord_ShipControlTracker.ShouldShowHud`.
  - Layout uses `GUILayout.BeginArea/BeginVertical` + `GUILayout.Label` rows pulling fields straight from the static `Njord_Telemetry` class (no MVVM/event system — polling read of public static floats/bools each `OnGUI` call). Sections: Vessel/Status, Waterline (water level, boat Y, depth, disable-level, readiness timer), Hull Integrity (HP bar drawn as two overlapping `GUI.Box` rects with solid-color textures sized by `GUILayoutUtility.GetLastRect()` + computed `hpPercent`), Steering (vanilla vs. Njord-multiplied rudder), Speed (Njord vs. "Mortal"/vanilla baseline, delta labeled "Gift of the Deep"), Wind (blend angle + color).
  - **Dragging:** `HandleDragging()` reads `Event.current` directly (`EventType.MouseDown/MouseDrag/MouseUp`), checks `_hudRect.Contains(e.mousePosition)` on mouse-down to start a drag, tracks delta from `_dragStartMouse`/`_dragStartHud`, and on mouse-up **writes the new position back into the BepInEx config entries** (`Plugin.HudPositionX.Value = _hudRect.x`) so position persists across sessions. Calls `e.Use()` to consume the event and prevent click-through to the game.
  - **Visibility gating** is entirely delegated to `Njord_ShipControlTracker.ShouldShowHud`, which is recomputed (`RecalculateHudState`) any time control changes: `Player.m_localPlayer != null && LocalPlayerHasControl() && Plugin.HudEnabledByUser.Value`. `LocalPlayerHasControl()` scans the `ControlMap` for any entry whose value equals the local player's ID.
  - **Toggle:** `Plugin.Update()` checks `Input.GetKeyDown(KeyCode.F7)` and flips `HudEnabledByUser.Value` (a `ConfigEntry<bool>`, not synced — local-only).
  - `Njord_Telemetry` is a plain static data bag (all public static fields) with a few `Update*` methods that also compute derived per-second rates (`SpeedGainPerSecond`, `SteeringGainPerSecond` via `(current - last) / dt` using `Time.time` deltas) and NaN/Infinity error-flagging (`WaterError`, `DepthError`, `SpeedError`, `RudderError`) so the HUD can render `"—"` instead of garbage numbers. It is written to from the physics tick (`NjordSeaDogEngine`) and the steering patch, and read from by the HUD — a simple decoupled producer/consumer via static state, no events/observers.
- **How to implement (step-by-step recipe):**
  1. In your `BaseUnityPlugin`, implement `private void OnGUI() { MyHud.OnGUI(); }` — Unity calls this automatically every IMGUI repaint/layout event; no registration needed.
  2. Store a `Rect` for panel position/size; initialize position from `ConfigEntry<float>` values so it's user-configurable and persists.
  3. Build `GUIStyle` objects once and cache them (constructing new `GUIStyle`/`Texture2D` every `OnGUI` call is expensive and leaks textures) — lazy-init with a `bool _stylesCreated` guard.
  4. For colored bars/backgrounds, create a `Texture2D(1,1)`, `SetPixel(0,0,color)`, `Apply()`, then assign to `GUIStyle.normal.background` — this is the standard IMGUI solid-color-rect trick.
  5. Use `GUILayout.BeginArea(rect)` + `GUILayout.BeginVertical()`/`Label`/`Space` for auto-flowing layout inside a fixed-position panel; use `GUILayoutUtility.GetLastRect()` when you need pixel-precise placement of a custom-drawn element (like a progress bar) relative to the previous auto-laid-out label.
  6. For a maintainable "system status" HUD, push all displayed values into one plain static data class updated from wherever the real computation happens (physics tick, patches, etc.) rather than having the HUD reach into game objects directly — keeps the render code simple/decoupled and lets multiple producers feed one display safely.
  7. To make a panel draggable: read `Event.current` inside your draw method (only valid during `OnGUI`), track `MouseDown`/`MouseDrag`/`MouseUp` against your `Rect.Contains(mousePosition)`, mutate the rect's `x`/`y` during drag, call `e.Use()` on each handled event to stop click-through, and persist the final position into a config entry on `MouseUp`.
  8. Gate the entire draw call behind a single visibility predicate (ownership/context-sensitive state + a user toggle) evaluated once at the top so you don't waste per-frame layout cost when hidden.
- **Reusable pattern/snippet:**
```csharp
// Plugin.cs
private void OnGUI() => MyHudManager.OnGUI();

// MyHudManager.cs
public static void OnGUI() {
    if (!ShouldShow) return;
    GUI.color = new Color(0,0,0,0.7f); GUI.Box(_rect, GUIContent.none); GUI.color = Color.white;
    GUILayout.BeginArea(_rect); GUILayout.BeginVertical();
    GUILayout.Label($"Speed: {Telemetry.Speed:F1} m/s");
    GUILayout.EndVertical(); GUILayout.EndArea();
    HandleDrag();
}
```

---

### 5. Vanilla Wind-Dial UI Override

- **Purpose:** Overrides Valheim's built-in ship-wind-direction HUD widget (the small compass/dial near the ship control UI) to reflect Njord's smoothed/assisted wind angle instead of the raw natural angle, and recolors it (green/blue/white bands, or a pulsing red/blue "adjusting" state) to communicate assist status to the player.
- **Key files:** `Patches/NjordTheWindsCall.cs`.
- **Architecture:**
  - `[HarmonyPatch(typeof(Hud), "UpdateShipHud")]` postfix, receiving `Hud __instance, Player player, float dt`. Resolves the player's controlled ship via `player.GetControlledShip()`.
  - Reflects the private `Hud.m_shipWindIconRoot` (`RectTransform`) via `AccessTools.Field(typeof(Hud), "m_shipWindIconRoot")` and rotates it: `iconRoot.localRotation = Quaternion.Euler(0, 0, currentNjordAngle)` — this is standard Unity **uGUI** (`RectTransform`), unlike the IMGUI HUD panel; it's manipulating an existing vanilla `Canvas`-based UI element, not creating new UI.
  - Also grabs the public `Hud.m_shipWindIcon` (`Image` component) and sets its `.color` based on re-derived dead-zone/adjusting state (duplicates the angle-target logic from the physics patch, since the HUD patch and physics patch run independently and both need to agree on "is the ship in the wind dead zone / actively being assisted").
  - Color logic: pulsing red (in dead zone, being pushed out) or pulsing blue (actively blending) using `(Mathf.Sin(Time.time * 6f) + 1f) / 2f` for the pulse; otherwise solid red (naturally in dead zone, not assisted), or a green→blue→white gradient banded by `Mathf.Abs(currentAngle)` when in a normal wind zone.
- **How to implement (step-by-step recipe):**
  1. Identify the vanilla UI update method that redraws the widget you want to override each frame (here `Hud.UpdateShipHud`) and Harmony-postfix it so your changes apply *after* vanilla sets its own values that frame.
  2. Use `AccessTools.Field`/`AccessTools.Property` to reach private `RectTransform`/`Image`/`Text` fields on the vanilla `Hud` singleton instance passed as `__instance`.
  3. Mutate `RectTransform.localRotation`/`Image.color` directly — no need to instantiate new UI; you're just re-driving values vanilla already wired to the Canvas.
  4. Keep any "decision" logic (what angle/color to show) in sync with whatever your gameplay system computed that same tick — either read it from a shared static/tracker class (preferred, avoids drift) or, if reflection into gameplay internals is unavoidable, duplicate the minimal calculation carefully.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(Hud), "UpdateShipHud")]
static class OverrideWindDial {
    static void Postfix(Hud __instance, Player player, float dt) {
        var iconRoot = AccessTools.Field(typeof(Hud), "m_shipWindIconRoot").GetValue(__instance) as RectTransform;
        iconRoot.localRotation = Quaternion.Euler(0, 0, myComputedAngle);
        __instance.m_shipWindIcon.color = myComputedColor;
    }
}
```

---

### 6. Procedural Rune VFX Trail System

- **Purpose:** The mod's signature visual — glowing procedurally-drawn Nordic rune glyphs that trail behind a moving ship's wake at the waterline, burst when the helm is claimed/released, and burst when sail state changes. Entirely CPU-procedural (no imported textures/art assets, no `AssetBundle`).
- **Key files:** `VFX/NjordRuneTextureGenerator.cs` (procedural texture drawing), `VFX/NjordRuneShader.shader` (custom unlit additive shader, largely superseded), `VFX/NjordRuneVFX_Helper.cs` (runtime shader discovery + material caching), `VFX/NjordRuneVFX.cs` (facade/orchestrator + legacy `ParticleSystem` prefab builder), `VFX/NjordRuneSpriteTrail.cs` (the **actual live implementation**), `VFX/NjordRunePiggyback.cs` (an alternate/earlier implementation), `VFX/NjordSeaDogEngineVFXTriggers.cs` (event-driven trigger logic called from the physics tick), `Patches/Njord_Ship_Destroy_Patch.cs` (cleanup).
- **Architecture:**
  - **Texture generation** (`NjordRuneTextureGenerator`): builds a `128×128` `Texture2D` (mipmapped, `RGBA32`) per rune by drawing 16 distinct Bresenham-line-based rune glyph patterns (`DrawLine`/`DrawCircle` pixel plotting) with a two-pass "glow" (thicker, dim, semi-transparent) + "stroke" (thinner, full-color, opaque) layering per line for a soft-glow look. `GenerateRune(color, index)` picks from a `switch` of 16 hardcoded coordinate patterns. Also provides `GenerateSoftTrail` (radial falloff blob) and `GenerateStreak` (directional smear) and `GenerateBlend` variants, though the live sprite trail only uses `GenerateRune`.
  - **Shader/material**: `NjordRuneShader.shader` is a minimal hand-written Cg/HLSL unlit additive shader (`Blend One One`, `ZWrite Off`, `Cull Off`) — but the actual live code path (`NjordRuneVFX_Helper.GetOrCreateRuneMaterial`) instead searches at runtime for an existing built-in shader via `Shader.Find("Particles/Standard Unlit")` with fallbacks (`"Legacy Shaders/Particles/Alpha Blended"`, `"Particles/Alpha Blended"`, `"Particles/Additive"`), and if none of those resolve, **scores every loaded `Material`** in `Resources.FindObjectsOfTypeAll<Material>()` by shader-name heuristics — a defensive pattern for render-pipeline portability without shipping a custom shader asset. Materials are cached in a `Dictionary<long, Material>` keyed by a packed `(shaderID, texID, colorHash)` composite key.
  - **Object pooling**: both `NjordRuneSpriteTrail` and `NjordRunePiggyback` maintain a `Queue<GameObject>` pool (initial 64, max 1024) of pre-built `GameObject`s each with a `SpriteRenderer` + a custom "fader" component + a disabled debug quad child. Pool root is a `DontDestroyOnLoad` container GameObject. `GetFromPool()`/`ReturnToPool()` toggle `SetActive` and reparent rather than instantiate/destroy on every spawn.
  - **Live trail emitter** (`NjordRuneSpriteTrail.Emitter`, a `MonoBehaviour` attached under the ship's own transform, *not* the stern sub-node): every `Update()`, computes XZ-plane distance moved since last spawn (`_spacing` threshold, ~0.5m), and when exceeded spawns a **5-point "wake fan"** of runes at the water surface Y. Water Y is sourced two ways: `_useTelemetry` mode reads `Njord_Telemetry.WaterLevel`/`BoatY`; non-telemetry mode (other players' ships) does a `Physics.Raycast` down/up against a resolved "Water" layer mask.
  - **Fader** (`RuneSprite_Fader` `MonoBehaviour`): drives a spawned rune's short lifetime (`_life` ≈2.5s) — random per-axis tumble, a light "gravity" (`GRAVITY=-0.35`) + exponential drag (`Mathf.Exp(-DRAG*dt)`) velocity model so runes rise from the wake, decelerate, hover, then dissolve. On completing its lifetime, invokes an `Action<GameObject> _onFinish` callback (bound to the pool's `ReturnToPool`).
  - **Burst spawns**: `SpawnBurst(pos, color, count)` — used for helm-claim/release and sail-state-change events — spawns `count` runes at a random offset with random velocity outward.
  - **Trigger orchestration** (`NjordSeaDogEngineVFXTriggers.Process`, called every physics tick from `NjordSeaDogEngine` for *every* client): tracks per-ship "last controller"/"last speed string" in dictionaries; on controller change → burst (claim) or `StopTrailForShip` (release); on sail-state string change to Half/Full → 4-rune scatter burst; every tick, if speed conditions are met, calls `StartTrailForShip` (idempotent) with a rate multiplier derived from `currentSpeed / maxSpeedForShipType`, else `StopTrailForShip`. Stern transform is resolved once via `ship.GetComponentInChildren<ShipControlls>(true).transform` and cached per-`Ship`.
  - **Cleanup**: `Njord_Ship_Destroy_Patch` is a Harmony postfix on `Ship.OnDestroy` that calls `NjordRuneVFX.RemoveEmitterForShip` so trail `GameObject`s don't leak when a ship despawns. A separate `NjordRuneVFX_Manager` `MonoBehaviour` (singleton, `DontDestroyOnLoad`) runs every 5s and destroys any legacy `ParticleSystem` emitter that hasn't been touched in 15s (timeout-based GC).
  - **Multiplayer sync note**: VFX is *not* network-synced via RPC/ZDO at all — it's **derived independently on every client** from the same deterministic-ish inputs (ship speed, sail state read straight off the `Ship` component, which itself *is* ZDO-synced by vanilla Valheim). An efficient pattern that avoids VFX network traffic entirely at the cost of minor spawn-timing differences between clients.
- **How to implement (step-by-step recipe):**
  1. **Procedural texture**: create `new Texture2D(w, h, TextureFormat.RGBA32, mipmaps: true)`, fill with a transparent `Color[]` via `SetPixels`, implement a simple Bresenham `DrawLine` that steps pixel-by-pixel and stamps a filled circle (`DrawCircle`) of the given radius at each step for thickness, call `tex.Apply(true)` when done. Draw a "glow" pass (thicker, low alpha) before a "stroke" pass (thinner, full alpha) per line segment for a soft-glow aesthetic without needing a blur shader.
  2. Wrap the texture in a `Sprite.Create(tex, new Rect(0,0,w,h), pivot, pixelsPerUnit)` for use with `SpriteRenderer`.
  3. **Shader/material portability**: don't ship a custom shader if you can avoid it — at runtime call `Shader.Find("Particles/Standard Unlit")` (and 2–3 named fallbacks) to get a built-in shader that works across the render pipeline the host game uses; only fall back to scanning `Resources.FindObjectsOfTypeAll<Material>()` by name heuristics as a last resort. Cache the resulting `Material` per (shader, texture, color) combination.
  4. Configure the material for additive/alpha glow: `SetInt("_SrcBlend", (int)BlendMode.One)`, `SetInt("_DstBlend", (int)BlendMode.One)`, `SetInt("_ZWrite", 0)`, appropriate keyword toggling, and `renderQueue = 3000`.
  5. **Object pool**: pre-instantiate N `GameObject`s each with a `SpriteRenderer` + a custom lifetime/fader `MonoBehaviour`, store in a `Queue<GameObject>`, parent to a single `DontDestroyOnLoad` root; `Dequeue`/`SetActive(true)` to "spawn", and on lifetime-end have the fader component call back to `SetActive(false)` + `Enqueue` to "despawn".
  6. **Trail emitter**: a `MonoBehaviour` parented to a *stable* transform, tracking distance-since-last-spawn in `Update()`; spawn a small fan/cluster of pooled sprites once a spacing threshold is crossed, and gate spawning on a minimum-speed threshold.
  7. **Fader component**: on `Play(velocity, life, scale, color)`, reset an age timer; each `Update()`, integrate velocity (optionally with drag/gravity) into position, interpolate alpha/scale by `age/life`, and invoke a completion callback when `age >= life`.
  8. **Trigger orchestration**: keep a small per-object "last state" dictionary so you can detect *edges* (state transitions) for one-shot burst effects, separately from *continuous* per-frame trail activation logic.
  9. **Multiplayer**: if the driving state (speed, discrete mode enum) is already replicated by the underlying networked object, you can skip building any VFX-specific RPC/sync layer — just run the same trigger/spawn logic unconditionally on every client inside a patch that already fires for all observers.
  10. **Cleanup**: patch the networked object's destroy method to explicitly destroy/pool-return any VFX `GameObject`s keyed to that object, and additionally run a periodic timeout-based sweep.
- **Reusable pattern/snippet:**
```csharp
// Runtime-portable glow material without shipping a custom shader
Shader best = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Particles/Additive");
var mat = new Material(best);
mat.SetTexture("_MainTex", proceduralTex);
mat.SetInt("_SrcBlend", (int)BlendMode.One);
mat.SetInt("_DstBlend", (int)BlendMode.One);
mat.SetInt("_ZWrite", 0);
mat.renderQueue = 3000;
```

---

### 7. Config System with Server-Locked, Network-Synced Settings

- **Purpose:** Lets server admins lock down and centrally control tuning values (all the force/speed/wind constants) so joining clients automatically receive the server's values instead of using their own local config, while still allowing single-player/local editing when unlocked.
- **Key files:** `Core/NjordConfig.cs`, `Core/NjordRuntimeConfig.cs`, `libs-Tools/ServerSync.cs` (shared/vendored across the author's mods, compiled directly into each project via `<Compile Include="..\libs-Tools\ServerSync.cs" />` in the `.csproj` rather than referenced as a DLL).
- **Architecture:**
  - `ServerSync.ConfigSync` wraps standard BepInEx `ConfigEntry<T>` in a `SyncedConfigEntry<T>` and handles broadcasting/receiving config values between server and clients.
  - `NjordConfig` instantiates one `ConfigSync configSync = new ConfigSync("wubarrk.njord") { DisplayName, CurrentVersion, MinimumRequiredVersion }` and a private generic helper `config<T>(group, name, default, description, synchronized=true)` that does `Plugin.Instance.Config.Bind(...)` then `configSync.AddConfigEntry(configEntry)`.
  - One special entry, `ServerConfigLocked` (bool, default true), is registered additionally via `configSync.AddLockingConfigEntry(ServerConfigLocked.SourceConfig)` — this is the "admin lock" switch.
  - All ~35 config entries are organized into numbered groups (`"1 - General"` .. `"10 - Debug"`) purely for BepInEx config-manager UI ordering.
  - Debug-related entries pass `synchronizedSetting: false` explicitly.
  - `NjordRuntimeConfig` is a thin singleton facade exposing plain `float`/`bool`/`Color` properties that forward to `NjordConfig.X.Value`.
- **How to implement (step-by-step recipe):**
  1. Vendor a copy of the community `ServerSync.cs` helper into a shared `libs-Tools` folder, compiled directly into each mod project.
  2. Construct one `private static readonly ServerSync.ConfigSync configSync = new(yourModGuid) { ... };`.
  3. Wrap every `ConfigFile.Bind(...)` call in `configSync.AddConfigEntry(...)`; if you maintain a plain POCO snapshot for hot-path reads, subscribe a `SettingChanged` handler.
  4. Add one bool "Lock Configuration" entry and register it via `configSync.AddLockingConfigEntry(...)`.
  5. Keep purely-local/per-client settings on plain unsynced `Bind` calls.
  6. Guard any float clamp helper against `NaN`/`Infinity`.

---

### 8. Debug Logging Utility

- **Purpose:** Centralized, level-gated logging wrapper so verbose diagnostic output can be toggled on/off at runtime via a single config flag without littering call sites with conditionals.
- **Key files:** `Debug/NjordDebug.cs`.
- **Architecture:** Static class with `Info`/`Debug`/`Warn` methods that check `IsEnabled()` before forwarding to `Plugin.Log.LogInfo/LogDebug/LogWarning`. `Log`/`Error` always print unconditionally.
- **How to implement:** Create a static wrapper class around your `ManualLogSource`; add a private `IsEnabled()` helper reading a boolean `ConfigEntry`, wrapped in try/catch; provide gated methods for routine/verbose output and always-on methods for critical messages; null-check the underlying log source everywhere.

---

### 9. Robust Console Command Registration

- **Purpose:** Registers custom in-game debug console commands reliably even if Valheim's `Terminal` singleton isn't initialized yet at plugin `Awake()` time.
- **Key files:** `Plugin.cs` (`RegisterConsoleCommands`, `ConsoleCommand`, `WaitAndRegister`).
- **Architecture:** `ConsoleCommand(name, action)` first tries immediate registration. If that throws, it reflects for a static `instance` field/property on `Terminal` to check readiness, and if still unavailable, starts a `StartCoroutine(WaitAndRegister(...))` that polls every frame for up to 10 seconds.
- **How to implement:** Attempt direct API registration first; on failure, reflect for the dependency's readiness signal; if still not ready, fall back to a coroutine that polls once per frame with a bounded timeout.

---

## Notable Gotchas / Non-Obvious Findings Worth Flagging

- **`Patches/Njord_ZNet_Awake_Patch.cs`** does not patch `ZNet.Awake` — it contains `Njord_PlayerSpawned_Init`, a Harmony postfix on `Player.OnSpawned`. Filename and content are mismatched.
- **`HUD/NjordReleaseIndicator.cs`** is fully implemented but is **never invoked** anywhere in the project.
- **`Patches/Patch_Ship_ApplyControlls.cs`** reads and reports a steering-multiplier value to telemetry but is a bare postfix with no evidence it writes the multiplied value back into any field the game reads.
- **`NjordRunePiggyback.cs`** is a largely-superseded parallel VFX implementation still wired to console debug commands.
- All Harmony patch application is centralized through `Patches/NjordPatchCaller.cs`'s `BindRune` helper, which wraps each `harmony.PatchAll(type)` call in try/catch so a single failing patch logs a warning and leaves that one rune "dormant" rather than crashing the whole mod — a resilience pattern worth replicating: patch registration should fail soft, per-patch-group, not all-or-nothing.
