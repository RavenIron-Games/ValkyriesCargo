# DeathBeforeDishonor & TheEye — Technical Reference Report

---

# PROJECT 1: DeathBeforeDishonor

**Overview:** DeathBeforeDishonor is a Valheim BepInEx/Harmony mod that replaces the sting of death with a "wisp" mini-game: on death the player's vanilla tombstone/respawn flow is left completely untouched, but the moment they respawn they are re-skinned as an invulnerable, fast, translucent "wisp" tied to a persistent per-character flag stored in `Player.m_customData`. The player must physically walk from their bed/spawn point back to their own tombstone (guided by a camera-relative HUD compass arrow) and interact with it to resurrect with a `SE_RebornSpirit` buff; if someone else loots the grave first, or the player clicks a dedicated "Give Up" HUD button, they instead get resolved in place with a `SE_DishonoredSoul` debuff. A vendored copy of a separate "Resurrection" mod (teammate-revives-your-ragdoll) is bundled in `temp_resurrection/` purely as a compatibility reference/soft-dependency target, not as shared code.

---

### Wisp State Machine & Persistent Cross-Respawn Flag

- **Purpose:** The architectural core of the mod — lets a mod inject custom "you are in a special mode" state that survives the game's native, un-modified death → 10s timer → GameObject-destroy → GameObject-recreate → respawn pipeline, including a full process relog. The single most reusable pattern in the project: any mod that needs a persistent per-character flag that isn't a Harmony override of vanilla flow can use it.
- **Key files:** `Patches/PlayerDeathPatch.cs`, `Patches/TombstonePatch.cs` (tracking half only), `System/WispController.cs`.
- **Architecture:**
  - Vanilla flow (unmodified): `Player.OnDeath()` creates the tombstone synchronously, then calls `Game.instance.RequestRespawn(10f, afterDeath:true)`. ~10s later `Game._RequestRespawn()` saves player data (`PlayerProfile.SavePlayerData`, which serializes `m_customData`) and destroys the player `GameObject`. `Game.UpdateRespawn` → `SpawnPlayer()` calls `LoadPlayerData` (restores `m_customData`) **then** `Player.OnSpawned(bool)`.
  - `Player.m_customData` is a real, public `Dictionary<string,string>` that is part of the actual save payload (round-trips through `Save()`/`Load()`), making it the correct, native extension point for "a flag that must survive object destruction and even a process restart" — not a custom save file, not a static field.
  - `TombstonePatch` keeps a static `LastCreatedTombstone` reference, stashed via a postfix on `TombStone.Awake()`. Because `Awake()` runs synchronously inside `CreateTombStone()` which itself runs synchronously inside `Player.OnDeath()`, by the time `PlayerDeathPatch`'s postfix on `OnDeath` runs, `TombstonePatch.LastCreatedTombstone` is guaranteed correct.
  - **Enter wisp (write side):** `[HarmonyPostfix]` on `Player.OnDeath()`. Guard `__instance == Player.m_localPlayer`. Skip if inventory is empty (vanilla spawns nothing, no tombstone to bind to). Otherwise read the just-created tombstone's `ZNetView.GetZDO().m_uid` and write two string entries into `m_customData`: `"dbd_wisp" = "1"` and `"dbd_tombstone" = "{ZDOID.UserID}:{ZDOID.ID}"`.
  - **Resume wisp (read side):** `[HarmonyPostfix]` on `Player.OnSpawned(bool)`. If `m_customData` contains `"dbd_wisp"`, parse the stored ZDOID string back into a real `ZDOID`, resolve the live `GameObject` via `ZNetScene.instance.FindInstance(zdoid)`, grab its `ZNetView`, then add/fetch a `WispController` (and `CompassBeacon`) component and call `wispCtrl.EnableWisp(tombstoneView)`.
  - **Exit wisp:** two call sites converge on `WispController.DisableWisp()` plus clearing both `m_customData` keys — `Resurrect()` and `GiveUp()`. No teleport in either exit path: the player has been standing at their bed/spawn point the whole time since vanilla respawn already put them there.
- **How to implement (step-by-step recipe):**
  1. Pick two or three short string keys, namespaced to your mod, to store in `Player.m_customData`.
  2. Add a Harmony postfix on `Player.OnDeath()` (protected instance method — needs `Publicize="true"` on your `assembly_valheim` reference) guarded to the local player, that writes your flag(s) after capturing whatever state you need.
  3. Add a Harmony postfix on `Player.OnSpawned(bool)` guarded to the local player, that reads the flag back out, reconstructs any referenced live object via `ZNetScene.instance.FindInstance(ZDOID)`, and re-applies your custom state.
  4. On your "exit" trigger, remove the `m_customData` keys and undo whatever `OnSpawned` set up.
  5. Never fight `IsDead()`/vanilla's own respawn — by the time your `OnSpawned` postfix runs, the character is already alive and healed; layer your mod-specific restrictions as an independent boolean flag checked by other patches.
  6. To reconstruct a `ZDOID` from a stored string: split on `:`, parse `long UserID` and `uint ID`, call `new ZDOID(userId, id)`.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(Player), "OnDeath")]
[HarmonyPostfix]
static void OnDeathPostfix(Player __instance) {
    if (__instance != Player.m_localPlayer) return;
    __instance.m_customData["yourmod_flag"] = "1";
}

[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
[HarmonyPostfix]
static void OnSpawnedPostfix(Player __instance, bool spawnValkyrie) {
    if (__instance != Player.m_localPlayer) return;
    if (__instance.m_customData.TryGetValue("yourmod_flag", out var v) && v == "1") {
        // re-apply custom mode; the character is already alive & positioned
    }
}
```

---

### Ghost Visual & Movement Modifier System

- **Purpose:** Turns a normal player character into a translucent, faster "ghost" without any custom shader assets required at runtime, while remaining swappable for a real custom shader later with zero code changes. Broadly reusable for any "temporary transformation" mechanic.
- **Key files:** `System/WispController.cs`.
- **Architecture:**
  - **Invulnerability is free/native:** `Player.SetGhostMode(true)` — vanilla's `Character.ApplyDamage` floors health at 1 whenever `InGhostMode()` is true. No custom damage-interception patch needed.
  - **Speed:** caches the player's real `m_walkSpeed`, `m_runSpeed`, `m_swimSpeed`, `m_speed` before multiplying each by a configurable multiplier, and restores the cached originals on exit.
  - **Stamina:** every `Update()` while active, force-sets stamina to max.
  - **Visual — swappable, no hard shader dependency:** looks up `Shader.Find(Config.WispShaderName)`. If found, builds one shared `Material` and swaps every child renderer's `sharedMaterials` array (caching the original array per-renderer). If no shader resolves, falls back to tinting via `MaterialPropertyBlock` (`SetColor("_Color", ...)` via `renderer.SetPropertyBlock`), caching each renderer's original block first.
  - **Critical gotcha:** never mutate `renderer.material` directly — Valheim shares material *instances* across all players wearing the same gear piece, so mutating `.material` will bleed the tint onto other players' identical gear. Always go through `sharedMaterials` (for full swap) or `MaterialPropertyBlock` (for tint).
  - Restoration iterates the two caches and restores `sharedMaterials`/`PropertyBlock` exactly, then clears them.
- **How to implement (step-by-step recipe):**
  1. Cache `Character`'s speed fields before mutation; restore verbatim on exit — never recompute a "restored" value.
  2. For invulnerability, call `player.SetGhostMode(true)`/`false` — verify the floor-at-1 behavior still exists in your game version first.
  3. For a placeholder ghost look with no art assets: iterate `GetComponentsInChildren<Renderer>(true)`, filter to mesh renderers, apply a `MaterialPropertyBlock` tint per renderer.
  4. Design the "real" visual path as a config-driven `Shader.Find(name)` lookup from day one, so dropping in the finished shader later is a config value change.
  5. Always cache-and-restore per-`Renderer` (materials array or property block) keyed in a `Dictionary<Renderer, T>`.
- **Reusable pattern/snippet:**
```csharp
MaterialPropertyBlock block = new MaterialPropertyBlock();
block.SetColor("_Color", new Color(0.5f, 0.9f, 1f, 0.5f));
foreach (var renderer in GetComponentsInChildren<Renderer>(true)) {
    if (renderer is SkinnedMeshRenderer || renderer is MeshRenderer) {
        var orig = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(orig);
        _originalBlocks[renderer] = orig;         // cache for restore
        renderer.SetPropertyBlock(block);          // never touch renderer.material directly
    }
}
```

---

### Tombstone Ownership Tracking & Loot-Reservation Gate

- **Purpose:** Tracks "the most recently created tombstone" for cross-patch handoff, enforces optional owner-only looting while the owner is an active wisp, and broadcasts "your grave was looted by X" cross-client via a custom ZDO field — all without any custom RPC.
- **Key files:** `Patches/TombstonePatch.cs`.
- **Architecture:**
  - **Capture:** `[HarmonyPostfix]` on `TombStone.Awake()` unconditionally overwrites a static `LastCreatedTombstone` property.
  - **Reservation prefix:** `[HarmonyPrefix]` on `TombStone.Interact`, returns `false` when reservation is enabled, the caller isn't the tombstone owner, and the owner currently has an active wisp. Shows a center-screen message.
  - **Resolution postfix:** `[HarmonyPostfix]` on `TombStone.Interact`, gated on `bool __result` (loot succeeded). Branches: owner interacting with own tombstone → `wispCtrl.Resurrect()`; someone else interacting with a tombstone that isn't theirs → writes `nview.GetZDO().Set("dbd_lootedBy", character.GetHoverName())`. This is the cross-client signal: any client (including an absent owner) can poll this ZDO field because ZDOs replicate.
  - **Consumer:** `WispController.Update()` polls `TombstoneView.GetZDO().GetString("dbd_lootedBy", "")` every frame while wisp is active; once non-empty, shows a big center banner and calls `GiveUp()`.
- **How to implement (step-by-step recipe):**
  1. Use a postfix on the target entity's `Awake()` to stash a static "most recently created" reference.
  2. For ownership checks, use `IsOwner()`/`GetOwner()` (persistent player ID) rather than string name matching.
  3. For a cross-client "flag on a specific networked object" that any peer can read without a custom RPC, just call `zdo.Set(key, value)`/`zdo.GetString/GetBool/...(key, default)` on any `ZNetView`-backed `ZDO`.
  4. Poll the custom ZDO field from a `MonoBehaviour.Update()` tied to the interested party rather than trying to push a notification.
- **Reusable pattern/snippet:**
```csharp
// Writer (any client that interacts with the object):
nview.GetZDO().Set("modid_customFlag", someValue);

// Reader (polled locally by the interested party):
string flag = nview.GetZDO().GetString("modid_customFlag", "");
if (!string.IsNullOrEmpty(flag)) { /* react */ }
```

---

### Wisp Action-Gating: Combat & World Interaction Blocking

- **Purpose:** Prevents a ghost/transformed player from taking actions inappropriate to that state while still allowing the one interaction that matters (their own tombstone).
- **Key files:** `Patches/CombatGatePatch.cs`, `Patches/PlayerInteractGatePatch.cs`.
- **Architecture:**
  - `CombatGatePatch`: `[HarmonyPrefix]` on `Humanoid.StartAttack`, guarded to the local player, checks wisp state and returns `false` (cancels the attack) plus a message; else falls through.
  - `PlayerInteractGatePatch`: `[HarmonyPrefix]` on `Player.Interact(GameObject go, bool hold, bool alt)`. While wisp is active, inspects the hovered object via `go.GetComponentInParent<TombStone>()`; if it's the owner's own tombstone, allows through, else blocks. A strict allow-list layered on top of a deny-everything-else default.
- **How to implement (step-by-step recipe):**
  1. Identify the single vanilla "gateway" method that all instances of the restricted action funnel through — patch that one chokepoint rather than every individual ability/item.
  2. Use a `[HarmonyPrefix]` returning `bool` — `false` cancels the original method entirely.
  3. Structure state-gated blocking as "default allow, with an explicit allow-list carve-out for exactly what you want to still work."
  4. Always guard to `Player.m_localPlayer` for player-only mods.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(Player), "Interact")]
[HarmonyPrefix]
static bool InteractPrefix(Player __instance, GameObject go, bool hold, bool alt) {
    if (__instance != Player.m_localPlayer) return true;
    if (!IsInRestrictedState(__instance)) return true;
    if (go != null && go.GetComponentInParent<AllowedType>() is { } allowed && AllowCheck(allowed))
        return true;               // allow-list carve-out
    return false;                   // deny everything else
}
```

---

### Enemy Aggro Suppression for Ghost State

- **Purpose:** Ghost mode alone doesn't stop monsters from targeting/attacking the (unkillable) player — this patch makes enemies unable to perceive the wisp at all.
- **Key files:** `Patches/EnemyAIPatch.cs`.
- **Architecture:** Two `[HarmonyPostfix]` patches on `BaseAI.CanSenseTarget(Character)` and its `(Character, bool)` overload. Each takes `ref bool __result` and, if the vanilla result was `true` and the target is the local player with active wisp, forces `__result = false`. Postfix (not prefix) because it needs vanilla's own perception logic to run first, then simply vetoes a positive result.
- **How to implement (step-by-step recipe):**
  1. Find the actual perception entry point on the AI base class (verify against decompiled source).
  2. Patch as a postfix with `ref bool __result`, only ever flip `true → false`.
  3. Patch every overload the base class exposes.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), typeof(Character))]
[HarmonyPostfix]
static void Postfix(ref bool __result, Character target) {
    if (__result && target == Player.m_localPlayer && IsInStealthyCustomState(target))
        __result = false;
}
```

---

### HUD Directional Compass Beacon

- **Purpose:** Gives the player real-time, camera-relative visual guidance back to a distant world position — a general "point an arrow at a distant target" HUD widget usable for any "go here" objective marker.
- **Key files:** `System/CompassBeacon.cs`.
- **Architecture:**
  - Parents a plain `GameObject` with a `Text` component directly under `Hud.instance.m_rootObject.transform` (piggybacking on vanilla's existing HUD canvas). Uses `Resources.GetBuiltinResource<Font>("Arial.ttf")` (no font asset needed), a Unicode triangle `"▲"` glyph, anchored to screen center, rotated around Z each frame.
  - Every `LateUpdate()`: bearing computed by flattening both the camera's forward vector and the direction-to-target vector to the XZ plane, `Vector3.SignedAngle(camForward, dirToTarget, Vector3.up)`, applied as `Quaternion.Euler(0, 0, -angle)`.
  - **Deliberately uses the camera's forward vector, not the player body's** — the camera visibly leads the body during turns, so a body-relative bearing lags noticeably.
- **How to implement (step-by-step recipe):**
  1. Create your indicator as a child of an existing HUD root so it inherits the game's UI scaling/canvas setup.
  2. Anchor to screen center; rotate the object, don't reposition it.
  3. In `LateUpdate`, compute `dir = (targetPos - camera.position).normalized`, flatten both `camera.forward` and `dir` to XZ, use `Vector3.SignedAngle(flatCamForward, flatDir, Vector3.up)`.
  4. Apply as a Z-axis rotation: `Quaternion.Euler(0, 0, -angle)`.
  5. Gate the whole thing's `SetActive` on state flag + target validity every frame.
- **Reusable pattern/snippet:**
```csharp
Vector3 dir = (targetPos - cam.transform.position).normalized;
Vector3 camFwd = cam.transform.forward; camFwd.y = 0; camFwd.Normalize();
dir.y = 0; dir.Normalize();
float angle = Vector3.SignedAngle(camFwd, dir, Vector3.up);
arrowRect.localRotation = Quaternion.Euler(0, 0, -angle);
```

---

### Clickable World-Space HUD Button — "Give Up" UI

- **Purpose:** A real, mouse-clickable HUD button (not a keybind) that's conditionally visible based on player state.
- **Key files:** `System/GiveUpButton.cs` (`GiveUpButtonRoot`).
- **Architecture:**
  - Single `DontDestroyOnLoad` singleton created once from plugin `Awake()`.
  - Builds a **full independent UI stack from scratch**: `Canvas` (`ScreenSpaceOverlay`, `sortingOrder=9999`), `CanvasScaler`, `GraphicRaycaster`, and — conditionally, via `FindAnyObjectByType<EventSystem>() == null` — a new `EventSystem` + `StandaloneInputModule`, also `DontDestroyOnLoad`. This "only create an EventSystem if one doesn't already exist" check is essential.
  - Button: `RectTransform` + `Image` (as `Button.targetGraphic`) + `Button` with `onClick.AddListener(handler)`, plus a child `Text`.
  - Visibility toggled every `Update()` only when `activeSelf` differs from current state.
  - **Known limitation:** Valheim normally captures/hides the mouse cursor, so this button is only actually clickable while some other UI has already freed the cursor.
- **How to implement (step-by-step recipe):**
  1. Build UI in a `DontDestroyOnLoad` root created once at plugin `Awake()`.
  2. Stack order: `Canvas` → `CanvasScaler` → `GraphicRaycaster` on the root; ensure exactly one `EventSystem` exists (guard-check before creating).
  3. A `Button` needs `targetGraphic` set or click detection silently fails visually (though `onClick` still fires).
  4. Gate visibility with a per-frame state check comparing against current `activeSelf`.
  5. Be aware of and test against your game's cursor-lock behavior.
- **Reusable pattern/snippet:**
```csharp
canvas = gameObject.AddComponent<Canvas>();
canvas.renderMode = RenderMode.ScreenSpaceOverlay;
gameObject.AddComponent<CanvasScaler>();
gameObject.AddComponent<GraphicRaycaster>();
if (Object.FindAnyObjectByType<EventSystem>() == null) {
    var es = new GameObject("EventSystem");
    es.AddComponent<EventSystem>();
    es.AddComponent<StandaloneInputModule>();
    DontDestroyOnLoad(es);
}
```

---

### Custom StatusEffect Registration System

- **Purpose:** The correct, robust pattern for registering brand-new `StatusEffect`/`SE_Stats` buffs/debuffs into Valheim's `ObjectDB` so they survive both first load and world-swap without being silently dropped or hash-colliding.
- **Key files:** `System/StatusEffects.cs`.
- **Architecture:**
  - Two Harmony postfixes: `ObjectDB.Awake` and `ObjectDB.CopyOtherDB`, both calling a shared `Register(ObjectDB)` method. **Both are required** — `Awake` alone misses that `CopyOtherDB` rebuilds/replaces the effect list on world load.
  - `Register()` is idempotent: checked against a **statically cached `int` hash**.
  - Effects built via `ScriptableObject.CreateInstance<SE_Stats>()`. **Critical gotcha:** `StatusEffect.NameHash()` hashes the Unity `Object.name`, *not* the `m_name` display field. Both must be set identically or effects hash-collide.
  - Registration itself is `objectDB.m_StatusEffects.Add(se)` — a `List<StatusEffect>`, not a dictionary; no unregister/replace API, hence the existence check.
  - Field-name correctness matters: `m_healthRegenMultiplier` (multiplier, not delta), `m_speedModifier` (additive fraction), `m_addMaxCarryWeight` (flat units), `m_ttl` (not `m_duration`).
- **How to implement (step-by-step recipe):**
  1. Never call your effect-registration function directly from plugin `Awake()`.
  2. Patch both `ObjectDB.Awake` (postfix) and `ObjectDB.CopyOtherDB` (postfix), both calling the same idempotent registration function.
  3. Build effects via `ScriptableObject.CreateInstance<YourEffectType>()`.
  4. Set **both** `.name` and `.m_name` to the same unique string.
  5. Cache `GetStableHashCode()` of that name as a `static readonly int` once.
  6. Double check every stat field's actual semantics against decompiled source before shipping.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
static class ObjectDB_Awake_Patch { static void Postfix(ObjectDB __instance) => Register(__instance); }
[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
static class ObjectDB_CopyOtherDB_Patch { static void Postfix(ObjectDB __instance) => Register(__instance); }

static void Register(ObjectDB db) {
    if (db?.m_StatusEffects == null) return;
    if (db.GetStatusEffect(MyEffectHash) != null) return;
    var se = ScriptableObject.CreateInstance<SE_Stats>();
    se.name = se.m_name = "SE_MyEffect";     // both required — NameHash() reads .name
    db.m_StatusEffects.Add(se);
}
```

---

### Synced Mod Configuration System — "ServerSync"

- **Purpose:** A vendored, widely-reused BepInEx config helper library that makes `ConfigEntry<T>` values automatically sync from server to connected clients over the network, with an optional server-side lock. This is boilerplate infrastructure meant to be copy-pasted verbatim into new mods.
- **Key files:** `System/ServerSync.cs` (contains `ConfigSync`, `SyncedConfigEntry<T>`, `CustomSyncedValue<T>`, `VersionCheck`), `System/PluginConfig.cs`.
- **Architecture:**
  - `ConfigSync` constructed once per mod with a unique string name, holds `DisplayName`/`CurrentVersion`/`MinimumRequiredVersion`.
  - `configSync.AddConfigEntry(ConfigEntry<T>)` wraps a normal BepInEx entry in a `SyncedConfigEntry<T>` and hooks `SettingChanged` to broadcast via a routed RPC.
  - `configSync.AddLockingConfigEntry(ConfigEntry<bool>)` designates one entry as the "Lock Configuration" toggle.
  - On new connection, the full config set is serialized into a `ZPackage` (with chunked fragmentation for large payloads and Deflate compression) and sent.
  - `VersionCheck` independently enforces connecting clients/servers have compatible mod versions.
- **How to implement (step-by-step recipe):**
  1. Copy `ServerSync.cs` verbatim into a new mod.
  2. Instantiate one `static readonly ConfigSync configSync = new(yourModGuid) { ... };`.
  3. For every config value, bind normally then wrap with `configSync.AddConfigEntry(entry)`.
  4. Add one bool "Lock Configuration" entry and register it via `AddLockingConfigEntry`.
  5. No further manual wiring needed — the file self-patches `ZNet` via a static constructor.
  6. Mark genuinely client-only settings with `SynchronizedConfig = false`.

---

### Soft-Dependency Harmony Compat Patch

- **Purpose:** A general pattern for a mod to hook into another, *optionally installed*, third-party mod's internals via reflection, without a hard compile-time dependency.
- **Key files:** `System/ResurrectionCompat.cs`.
- **Architecture:**
  - A static constructor checks `Chainloader.PluginInfos.ContainsKey(otherModGuid)` before doing anything.
  - If present, creates a *second*, separately-named `Harmony` instance scoped to the compat patch.
  - Uses `[HarmonyPatch]` with no target specified plus a `static MethodBase TargetMethod()` — the canonical way to resolve a patch target dynamically. Uses `AccessTools.TypeByName(...)` (returns `null`, not a throw, if the type doesn't exist) then `AccessTools.Method(...)`. If `TargetMethod()` returns `null`, Harmony silently skips that patch class.
- **How to implement (step-by-step recipe):**
  1. Gate the entire compat layer behind a runtime check of `Chainloader.PluginInfos.ContainsKey(otherModGuid)`.
  2. Use a dedicated, separately-named `Harmony` instance for compat patches.
  3. Resolve cross-mod types/methods via `AccessTools.TypeByName`/`AccessTools.Method` inside a `[HarmonyPatch]`-annotated class's static `TargetMethod()`.
  4. Treat this pattern as fragile to the other mod's internal refactors — verify the target actually exists in the specific version you're compat-testing against.

---

### Vendored "Resurrection" Mod — Ragdoll Teammate-Revival System

- **Purpose:** A complete, separate, real published Valheim mod (blaxxun-boop's "Resurrection") vendored in purely so `ResurrectionCompat.cs` has real source to check against. It replaces vanilla's instant death→tombstone flow with a ragdoll-interactable "teammate resurrects you in place" mechanic.
- **Key files:** `temp_resurrection/Resurrection/Resurrection.cs`, `ResInteract.cs`, `DeathPortal.cs` / `FlickeringLight.cs`, `Requirement.cs`.
- **Architecture:**
  - **Death interception, not prevention:** postfix on `Player.RPC_OnDeath` freezes the corpse (`isKinematic = true`). A prefix on `Game.RequestRespawn` returns `false` as long as the local player's ZDO still has `"dead" == true` — resurrection works by **indefinitely delaying** vanilla's own respawn call until manually revived.
  - **Death popup:** on `Humanoid.OnRagdollCreated` (local player only), shows a popup offering "wait to be resurrected" vs. forced "Respawn". Cancels the ragdoll's scheduled self-destroy so it persists indefinitely. Stores dead player's name/ZDOID onto the ragdoll's own `ZDO`.
  - **Making the corpse interactable:** on `Ragdoll.Awake()`, if the ragdoll carries player-info ZDO data, sets the body child's layer to `Default`, adds a `MeshCollider` + kinematic `Rigidbody`, and attaches a custom `ResInteract : MonoBehaviour, Interactable, Hoverable` component. Implementing Valheim's `Interactable`/`Hoverable` interfaces directly on a plain component is the reusable technique — any `GameObject` with collider + these two interfaces becomes something the player can hover/interact with.
  - **`ResInteract.Interact()`**: checks optional Group/Guild mod soft-dependencies to gate who's allowed to resurrect whom; checks item cost; starts a channel timer, during which `Player.GetActionProgress` is postfix-patched to inject a fake progress bar reusing vanilla's existing action-progress UI.
  - **Interrupting the channel:** postfix on `Character.RPC_Damage` resets the channel if the reviver takes damage; postfix on `Player.ClearActionQueue` does the same for movement.
  - **Actual revival RPC flow:** routed RPC to the *dead* player's own client (owner-routed RPC, not broadcast) triggers a local reveal sequence with VFX, then after a delay the dead player's client broadcasts a follow-up "resurrected" RPC to everyone so all clients re-enable visuals/physics in sync.
  - **RPC registration pattern:** postfix on `Player.Awake()` registers custom per-instance RPCs on every `Player`'s own `ZNetView` — the standard place to register instance-scoped custom RPC handlers in Valheim modding.
  - **VFX:** a Unity `AssetBundle` embedded as a manifest resource supplies a "DeathPortal" prefab, sequenced via nested `yield return new WaitForSeconds(...)` coroutine timing.
  - **Config-driven item cost with custom IMGUI editor:** serializes a list of `(itemName, amount)` pairs into a single delimited config string, with a `CustomDrawer` wired via the `ConfigDescription`'s tag object for a proper editable table UI.
- **How to implement (step-by-step recipe, if reimplementing a similar "revive from corpse" system):**
  1. Freeze the corpse instead of letting it ragdoll/despawn; cancel the ragdoll's self-destroy `Invoke`.
  2. Block vanilla auto-respawn with a prefix returning `false` while a custom "still dead" ZDO flag is set.
  3. Stamp identifying info onto the ragdoll's own `ZDO` so any client can look it up.
  4. Make the ragdoll body interactable by adding a collider + a component implementing `Interactable`/`Hoverable` directly.
  5. For a channeled action with a progress bar, postfix `Player.GetActionProgress` rather than building bespoke UI.
  6. Route the "you've been revived" trigger as an owner-targeted RPC to the dead player's own client, then broadcast a follow-up RPC once the local reveal completes.
  7. For list-shaped config values, serialize to a delimited string and supply a `CustomDrawer` for a proper editable table.

---

# PROJECT 2: TheEye

**Overview:** TheEye (published as "Wubarrk's Eye" / `WubarrksEye`) is a Valheim diagnostic/reverse-engineering tool, not a gameplay mod: it performs deterministic, streaming, async reflection dumps of the game's entire loaded type system, active scene graph, prefab registry, live ZDO state, and `ObjectDB` contents to JSON (or a zipped JSON-Lines "ML" format optimized for feeding to LLM coding agents), plus a "Huginn's Report" diff engine that hashes and compares dumps across time to catch injected assemblies or state drift. Everything is controllable via an in-game IMGUI HUD (F8/F9) or a `dump_eye` console command, and a companion standalone file (`DeepReflectionWalker.cs`) provides a generic, cycle-safe deep object-graph walker shared across the author's other tooling.

---

### DeepReflectionWalker — Generic Cycle-Safe Object Graph Walker

- **Purpose:** A standalone, dependency-light, drop-in-anywhere utility for recursively walking an arbitrary .NET/Unity object graph via reflection and invoking a callback on every reachable field, while guarding against reference cycles, runaway depth/volume, and reflecting into dangerous or noisy runtime-internal types. It exists identically (byte-for-byte) at both `TheEye/DeepReflectionWalker.cs` and the shared `libs-Tools/DeepReflectionWalker.cs`, and is **not referenced anywhere inside TheEye's own build** — it's a standalone shared tool file meant to be copy-pasted into whichever mod needs deep object inspection, independent of TheEye's own dump pipeline. (See also `libs-Tools_Shared.md` for the canonical cross-project documentation of this utility.)
- **Key files:** `TheEye/DeepReflectionWalker.cs` (identical copy also at `libs-Tools/DeepReflectionWalker.cs`).
- **Architecture:**
  - Single static method: `DeepReflectionWalker.Walk(object root, Action<string, object> onField)`.
  - **Cycle safety:** a `HashSet<object>` keyed by a custom `ReferenceEqualityComparer` (uses `object.ReferenceEquals`/`RuntimeHelpers.GetHashCode`, not `.Equals`/`GetHashCode()`) ensures every distinct object instance is visited at most once.
  - **Runaway guards:** hard caps `MAX_DEPTH = 32` and `MAX_ITEMS = 500_000`, both checked at the top of every recursive call, simply truncating silently once exceeded.
  - **Forbidden-type filtering:** a static prefix list blocks recursion into namespaces/types known to be dangerous, pure noise (compiler-generated closures `<>`/`c__DisplayClass`/`d__`), or third-party SDK internals.
  - **Unity object liveness check:** `if (val != null && val == null) return;` — exploits Unity's overridden `==` operator on `UnityEngine.Object`, which returns `true` for equality against `null` when the native C++ side has been destroyed even though the C# wrapper reference is still non-null.
  - **Field enumeration:** `SafeFields(Type)` wraps `GetFields` in try/catch, filters out static and pointer-typed fields, and filters by declared type against the forbidden-prefix list too.
  - **Collection traversal:** after fields, if the object implements `IEnumerable` (and isn't `string`), iterates and recurses into each non-null element with an indexed path segment.
  - Each `GetValue`/enumeration step is individually wrapped in try/catch.
- **How to implement (step-by-step recipe):**
  1. Define a `ReferenceEqualityComparer : IEqualityComparer<object>` using `ReferenceEquals`/`RuntimeHelpers.GetHashCode`.
  2. Maintain module-level `HashSet<object> visited`, an item counter, and max-depth/max-item constants; check both at the top of every recursive step.
  3. Build a `string[]` prefix blocklist: compiler-generated state machine/closure types, reflection-emit/threading/interop/security internals, and any third-party native SDK your host process links.
  4. For Unity, add the `(UnityEngine.Object)obj == null` double-check to skip destroyed-but-not-null wrapper references.
  5. Wrap every individual reflective operation in its own try/catch.
  6. Recurse into both declared instance fields and `IEnumerable` contents (excluding `string`).
  7. Expose a single `Walk(root, Action<string,object> onField)` entry point that resets all shared state first.
- **Reusable pattern/snippet:**
```csharp
private sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
    public new bool Equals(object x, object y) => x == y;
    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
if (obj is UnityEngine.Object uo && uo == null) return;   // destroyed-but-not-null guard
if (!_visited.Add(obj)) return;                            // cycle guard
```

---

### Async/Coroutine Streaming JSON Dump Engine

- **Purpose:** The core "Deep Dump" pipeline — extracts large volumes of reflection/game-state data to disk as JSON without freezing the game, by spreading work across many frames via Unity coroutines, and supports two output shapes (pretty-printed per-category JSON files, vs. a single zipped JSON-Lines file for LLM ingestion) from the same extractor implementations.
- **Key files:** `Core/Commands/DumpManager.cs`, `Core/Dumpers/IDataExtractor.cs`, `Core/Commands/CoroutineRunner.cs`, `Core/EyeCommands.cs`, `Core/UI/EyeUIManager.cs`.
- **Architecture:**
  - `CoroutineRunner` is a tiny `DontDestroyOnLoad` singleton created via `[RuntimeInitializeOnLoadMethod]` (fires automatically at game startup) whose only job is `public static void Run(IEnumerator routine) => _instance.StartCoroutine(routine)` — the standard trick for starting coroutines from static contexts that have no `MonoBehaviour` of their own.
  - `IDataExtractor` interface: `ExtractorName`, `OutputFileName`, `ExtractData(StreamWriter, int maxBytes)` (standard mode), `ExtractDataToZip(StreamWriter)` (JSONL mode).
  - `DumpManager.Start(dumpRoot, DumpOptions)` is fire-and-forget: hands a top-level `DumpRoutine` `IEnumerator` to `CoroutineRunner.Run`. Progress exposed via static fields polled by the UI — no event/callback system.
  - **The coroutine-yielding contract:** each extractor's iterator does real batches of work then `yield return null` — this is what avoids freezing the game. `DumpManager` composes progress from nested iterators via manual `while (extRoutine.MoveNext()) { updateProgress(); yield return extRoutine.Current; }` — a coroutine-of-coroutines composition pattern.
  - **Config-driven batch sizing:** each extractor multiplies its per-frame batch size by a hardware-tier multiplier (1x/3x/10x for 16/32/64+GB profiles).
  - **Off-thread CPU work inside a coroutine:** the API extractor does `Task.Run(() => Parallel.ForEach(...)).Wait()` **inside** a coroutine step — trading "no full-game freeze across frames" for "one longer per-batch stall each yield," a real but consciously chosen tradeoff.
  - **Extractors implemented:** `ApiExtractor` (all loaded types via reflection), `PrefabExtractor`, `SceneExtractor`, `ZdoExtractor`, `ObjectDbExtractor` — several funnel through a shared `DumpGameObjectData(GameObject)` helper.
  - **Entry points:** `EyeCommands.Initialize` registers a console command `dump_eye`. `EyeUIManager`'s config window builds a `DumpOptions` from live checkbox state.
- **How to implement (step-by-step recipe):**
  1. Create a tiny `DontDestroyOnLoad` `MonoBehaviour` singleton via `[RuntimeInitializeOnLoadMethod]` exposing a static `Run(IEnumerator)`.
  2. Define a small extractor interface and implement one per logical data category.
  3. In each extractor's iterator, do a bounded amount of work, update a shared progress field, then `yield return null`.
  4. Drive a top-level orchestrating coroutine that manually pumps each extractor's `IEnumerator` and computes overall progress.
  5. Support two output modes by having each extractor accept either a plain `StreamWriter` (pretty JSON array) or a `StreamWriter` into a `ZipArchiveEntry` (JSON Lines) — don't build two separate extraction code paths, just two serialization calls.
  6. Expose `IsRunning`/`OverallProgress`/`CurrentPhase` as static fields for a polling UI.
  7. It's acceptable to block a single coroutine step on a `Task.Run(...).Wait()` for CPU-bound reflection work that doesn't parallelize naturally across frames.
- **Reusable pattern/snippet:**
```csharp
public class CoroutineRunner : MonoBehaviour {
    private static CoroutineRunner _instance;
    [RuntimeInitializeOnLoadMethod]
    private static void Init() {
        var go = new GameObject("Runner");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoroutineRunner>();
    }
    public static void Run(IEnumerator routine) => _instance.StartCoroutine(routine);
}

IEnumerator ExtractBatched(IList items, StreamWriter w) {
    for (int i = 0; i < items.Count; i++) {
        WriteOne(w, items[i]);
        if (i % batchSize == 0) yield return null;   // give the frame back
    }
}
```

---

### Huginn's Report — Cross-Dump State Diffing Engine

- **Purpose:** Compares the just-completed dump against the most recent prior dump of the same output format to surface exactly what changed — new/modified prefabs, ZDOs, injected types — without needing structured schema-aware diffing.
- **Key files:** `Core/Commands/HuginnsReport.cs`.
- **Architecture:**
  - **Baseline discovery:** lists sibling dump directories sorted by creation time, picks the first one matching the *current run's format*.
  - **Diffing algorithm is intentionally format-agnostic and line-based, not JSON-structure-aware:** reads the *old* dump's every line, computes SHA256 over each trimmed line, stores digests in a `HashSet<string>`. Streams the *new* dump's lines the same way; any new line whose hash isn't in the old set is emitted as a change. A real, documented tradeoff for streaming-without-loading-everything-into-memory (a single-field change surfaces as multiple "changed" lines rather than one coherent object-level diff).
  - **Streaming, chunked, and parallelized to bound memory:** reads new-dump lines into chunks (config-driven size), hashes/compares each chunk via `Parallel.ForEach`, draining results into a `ConcurrentQueue<string>` that a single writer thread flushes — old-dump hashes loaded entirely into memory up front, but the *new* dump and diff output are both streamed.
  - Runs via `Task.Run(...)`, with the calling coroutine polling `IsCompleted` — the same "yield while a background Task finishes" pattern as the ML API extractor.
- **How to implement (step-by-step recipe):**
  1. For a cheap, schema-agnostic "did anything change" diff over large structured-text dumps, hash each line independently rather than attempting a structural diff — trades diff precision for near-zero implementation complexity and full streaming capability.
  2. Load only the *baseline* side's hashes fully into memory; stream the *new* side line-by-line.
  3. Batch new-side lines into config-sized chunks, hash/compare with `Parallel.ForEach` at a configurable thread count, drain through a `ConcurrentQueue` to a single output writer.
  4. Guard against invalid cross-format comparisons up front.
  5. Offload the diff computation to `Task.Run` and have the orchestrating coroutine just poll `IsCompleted`.

---

### Ghost ZDO Protection — Forced Global Active-Area Patch

- **Purpose:** Valheim only keeps ZDOs "active"/loaded within each client's local active area; a naive ZDO dump run near spawn would only capture nearby objects. This patch forces the game to treat every ZDO as always "in range," so a full-world dump captures everything regardless of player position.
- **Key files:** `Core/Patches/ZDOExistencePatch.cs`, wired up manually in `Plugin.cs`.
- **Architecture:**
  - Four tiny static prefix methods, each unconditionally short-circuiting a specific `ZNetScene` culling method (`OutsideActiveArea`, three overloads of `InActiveArea`) and skipping the original body.
  - **Wired up without `[HarmonyPatch]` attributes** — `Plugin.Awake()` resolves each target method explicitly via `AccessTools.Method(typeof(ZNetScene), "OutsideActiveArea", new[]{typeof(Vector3)})` with each overload's exact parameter type array, builds a `HarmonyMethod`, calls `_harmony.Patch(...)` manually. Wrapped in its own try/catch, logging a warning and continuing normal load if resolution fails ("Ghost ZDOs" may be missed) — deliberately fails soft.
- **How to implement (step-by-step recipe):**
  1. Identify every method gate that could reject/cull the data you want to force-include.
  2. Prefer explicit `AccessTools.Method(type, name, new[]{ paramTypes })` + manual `harmony.Patch(...)` over attribute-based patching when patching several overloads with a single auditable code block.
  3. Wrap this kind of "aggressive vanilla behavior override" patch group in its own try/catch, distinct from your general patch-application code.
  4. A prefix returning `false` with a hardcoded `__result` completely replaces the original method's logic for that call.
- **Reusable pattern/snippet:**
```csharp
var mOut = AccessTools.Method(typeof(ZNetScene), "OutsideActiveArea", new[] { typeof(Vector3) });
if (mOut == null) throw new Exception("target not found");
harmony.Patch(mOut, prefix: new HarmonyMethod(AccessTools.Method(typeof(MyPatch), nameof(MyPatch.ForceFalse))));

static bool ForceFalse(ref bool __result) { __result = false; return false; }
```

---

### In-Game IMGUI Debug HUD & Config Window

- **Purpose:** A no-asset, code-only in-game control panel using Unity's legacy immediate-mode `OnGUI` system — appropriate for developer/debug tooling where quick iteration matters more than polish.
- **Key files:** `Core/UI/EyeUIManager.cs`.
- **Architecture:**
  - Single `DontDestroyOnLoad` singleton, created from `Plugin.Awake()`.
  - `Update()` polls `Input.GetKeyDown(KeyCode.F8)`/`F9` to toggle windows — simple boolean flags.
  - `OnGUI()` shows a small HUD when the big window is closed, and the config window (`GUI.Window`) when toggled — `GUI.DragWindow(new Rect(0,0,10000,20))` inside the window callback gives free click-and-drag-by-title-bar.
  - **Custom styling without any texture assets:** builds 1×1 `Texture2D`s in code for solid-color backgrounds, derives `GUIStyle`s from `GUI.skin.window`/`button`/`label` with overrides.
  - Checkboxes bind directly to public static fields — UI state doubles as the actual settings storage.
  - Rich text color tags (`<color=#FF5555>...</color>`) work directly inside `GUILayout.Label` strings.
- **How to implement (step-by-step recipe):**
  1. Create one persistent `DontDestroyOnLoad` `MonoBehaviour` to host `OnGUI` calls.
  2. Use `Input.GetKeyDown(KeyCode.X)` in `Update()` for simple show/hide toggles.
  3. For a draggable window with zero art assets: `_rect = GUI.Window(id, _rect, DrawFn, title, style)` ending with `GUI.DragWindow(new Rect(0,0,width,titleBarHeight))`.
  4. For solid-color custom styling without textures: build `Texture2D(1,1)` procedurally, assign to a cloned `GUIStyle`'s `.normal.background`.
  5. Bind checkbox/toggle state directly to the fields you'll actually consume — skip a separate view-model layer for a small dev tool.
  6. Branch window content on the state of the system it's controlling rather than tracking a parallel "window mode" enum.
- **Reusable pattern/snippet:**
```csharp
private void OnGUI() {
    _rect = GUI.Window(9999, _rect, DrawWindow, "My Tool", _windowStyle);
}
private void DrawWindow(int id) {
    GUILayout.Label("Controls");
    MyToggle = GUILayout.Toggle(MyToggle, "Enable Thing");
    if (GUILayout.Button("Go")) DoWork();
    GUI.DragWindow(new Rect(0, 0, 10000, 20));   // drag-by-title-bar
}
```

---

### Console Command Registration

- **Purpose:** Registers a custom Valheim developer-console command (`dump_eye`) that triggers a dump with a fixed default option set — an alternate, keyboard/console-driven trigger path alongside the HUD button.
- **Key files:** `Core/EyeCommands.cs`.
- **Architecture:** Wraps `new Terminal.ConsoleCommand(name, description, (Terminal.ConsoleEventArgs args) => handler(args.Args), isCheat, isNetwork, onlyServer, ..., optionsFetcher:null, ...)` — Valheim's real console command registration API. The handler calls the same `DumpManager.Start` path the HUD uses.
- **How to implement (step-by-step recipe):**
  1. Construct `new Terminal.ConsoleCommand(name, helpText, (a) => YourHandler(a.Args), ...)` — confirm the exact constructor overload against the target game version's decompiled source.
  2. Wrap registration in try/catch and log both success and failure.
  3. Keep the command handler as a thin call into the same underlying trigger function the UI uses.

---

### Defensive/Safe Harmony Mass-Patcher

- **Purpose:** A hardened alternative to Harmony's own `PatchAll()` that patches every `[HarmonyPatch]`-annotated class in an assembly one at a time with individual error isolation, plus utilities for patching *all* overloads of a same-named method, and a fuzzy/best-effort fallback method-resolution strategy for small game-version API drifts. Notably not currently wired into `Plugin.cs` (which calls plain `PatchAll()`) — kept as an available reusable utility.
- **Key files:** `HarmonyPatches/HarmonyPatcher.cs`, `HarmonyPatches/HarmonyBootstrap.cs`.
- **Architecture:**
  - Enumerates every type in the assembly, tolerating `ReflectionTypeLoadException` by falling back to `ex.Types.Where(t => t != null)`.
  - Explicitly **skips** any class using the broad `[HarmonyPatchAll]` attribute ("too broad").
  - `ProcessTargetSpec`: resolves the exact overload via `Type.GetMethod`, falling back to `FindMethodBySignatureShape` (matches by parameter type list, tie-broken by Levenshtein distance on the method name) if the exact pair isn't found — i.e., can survive a method being *renamed* between game versions.
  - `PatchAllOverloads` patches *every* method on the type sharing a name, individually try/catching each.
  - `IsPatchable(MethodBase)` pre-filters out abstract methods, P/Invoke methods, methods with no IL body.
- **How to implement (step-by-step recipe):**
  1. Never call raw `Assembly.GetTypes()` in a context that must survive partial load failures — always catch `ReflectionTypeLoadException`.
  2. When mass-patching, iterate patch-candidate classes individually with each `Patch()` call wrapped in its own try/catch.
  3. Provide an "all overloads" patch helper for cases where you want every overload covered without hand-listing parameter type arrays.
  4. Consider a fuzzy-fallback resolution strategy only for tooling/debug contexts where "probably the right method" is acceptable — not appropriate for gameplay-critical patches.
  5. Pre-validate patchability before calling `harmony.Patch(...)` to convert a hard crash into a clean, logged skip.
- **Reusable pattern/snippet:**
```csharp
Type[] types;
try { types = assembly.GetTypes(); }
catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

foreach (var type in types) {
    try { /* resolve + harmony.Patch(...) for this type only */ }
    catch (Exception ex) { Log.Warn($"Skipping {type.FullName}: {ex.Message}"); }
}
```

---

### Reflection-Based Full Type API Text Dumper

- **Purpose:** Produces a complete, human-readable, deterministic text description of a single .NET `Type` — inheritance chain, interfaces, attributes, every field/property/event/method with full visibility/modifier/signature formatting, enum values, and Unity-specific metadata. A separate, more verbose, per-type text format from the JSON-based `ApiExtractor` — currently unreferenced/orphaned but a complete, correct, reusable utility.
- **Key files:** `Core/ApiDumpWriter.cs`.
- **Architecture:**
  - Single entry point `WriteTypeApi(TextWriter w, Type type)`.
  - **Consistent, deterministic ordering everywhere** — every member list is sorted by name (then param count for methods), meaning two dumps of an unchanged type produce byte-identical output — exactly what makes line-hash diffing (Huginn's Report) meaningful.
  - Modifier formatting helpers build a bracketed `[public static readonly]`-style tag from raw reflection booleans, correctly distinguishing `abstract`/`override`/`virtual`.
  - Every "get custom attributes" call wrapped defensively (returns empty on any exception).
  - Unity-specific section: `MonoBehaviour`/`ScriptableObject` classification, and `[RequireComponent]`'s actual internal field layout (`m_Type0`/`m_Type1`/`m_Type2` — three separate fields, not an array).
- **How to implement (step-by-step recipe):**
  1. For any "dump a type's full API surface" tool, always sort every member collection by a stable key — this is what makes the output diffable and reproducible.
  2. Wrap every individual reflection call that can throw in its own try/catch returning an empty/default result.
  3. Build inheritance chains via `while (t != null) { yield return t; t = t.BaseType; }`.
  4. When targeting Unity types, remember `RequireComponent` stores its (up to 3) required types in three separate fields, not an array.
  5. Derive `abstract`/`override`/`virtual` correctly via the combination of `IsAbstract`/`IsVirtual`/`GetBaseDefinition() != this`/`IsFinal` — a naive `IsVirtual` check alone conflates all three.

---

### Basic (Unsynced) Config Binding & Console-Logging Utilities

- **Purpose:** A simpler contrast to DeathBeforeDishonor's networked `ServerSync` system — since TheEye is a single-player-facing diagnostic tool, its config binding is plain, direct BepInEx `ConfigFile.Bind` with manual clamping, no sync layer.
- **Key files:** `Core/WubarrkConfig.cs`, `Core/WubarrkLogger.cs`, `Adapters/BlackBoxAdapter.cs` (also unreferenced elsewhere).
- **Architecture:**
  - `WubarrkConfig.Bind(ConfigFile Config)` binds each setting directly to a `public static ConfigEntry<T>` field. Numeric settings manually range-clamped immediately after binding rather than using `AcceptableValueRange<T>`.
  - `WubarrkLogger` is a static wrapper around a `ManualLogSource` providing `Info`/`Warn`/`Error`, each independently try/catching so a logging call itself can never throw. A private `Colorize` helper wraps messages containing certain markers in BepInEx rich-text color.
  - `BlackBoxAdapter` is a small set of static logging helpers designed to be called from Harmony patch bodies to emit structured "which patch just fired" telemetry lines — a reusable idea even though not currently wired into any patches.
- **How to implement (step-by-step recipe):**
  1. For simple, non-networked tools, skip a sync layer entirely — direct `public static ConfigEntry<T>` fields populated by one `Bind()` call is sufficient.
  2. Clamp numeric config values immediately after binding for a hard guarantee.
  3. Centralize all logging through one static wrapper that both prefixes a consistent tag and try/catches internally.
  4. For lightweight "which Harmony patch just fired" telemetry without a full instrumentation framework, add a one-line static call at the top of prefix/postfix methods pointing at a shared logging helper.

---

## Cross-Project Notes

- Both `.csproj` files follow the same modern build convention: `net48` target, `BepInEx.AssemblyPublicizer.MSBuild` with `Publicize="true"`, and `HintPath` references into a shared `libs-Tools/` pool at the workspace root rather than per-project vendored DLLs — this is the author's standard project template across mods.
- DeathBeforeDishonor's `ServerSync.cs` and TheEye's simpler `WubarrkConfig.cs` represent two ends of a spectrum (multiplayer-synced vs. single-player-only config) — reach for `ServerSync` when server-authoritative balance values matter, `WubarrkConfig`-style direct binding for pure debug/diagnostic tooling.
- TheEye's `CoroutineRunner` + chunked `yield return null` pattern is the general answer to "run expensive work without freezing the game" and should be the canonical reference for future mods needing background-safe long-running work.
- The `DeepReflectionWalker.cs` utility is confirmed to be the author's intended pattern for genuinely cross-project shared code (shared-by-reference-location at `libs-Tools`), as opposed to `ServerSync.cs` (copy-pasted per-project rather than referenced from a shared location).
