# Client audit — every engine call on the client paths, 2026-09-06

Adversarial pass over the client half of the mod: `Client/Terminal/CargoTerminal.cs`, `Client/TrayModel.cs`,
`Client/DealApplier.cs`, `Client/ComfortReporter.cs`, `Client/InboxStore.cs`, `Net/CargoTransport.cs`,
`Net/AdminRpc.cs`, `Core/CargoTick.cs`, `ValkyriesCargo.cs`, against the vendored `Libs/SharedUI/*` and
`Patches/Patch_Terminal.cs`. **None of this code has ever run with a renderer.** The stance was: every engine
call is wrong until the decompiled assembly says otherwise.

Decompiles read from the installed 0.221.x under
`C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\`. Line numbers below are lines in
the ilspycmd output of the named type, not in the game's source.

**Result: 57 engine members walked. 4 were used against their real contract; 3 further defects were control
flow or protocol rather than a member. All 7 are fixed on this branch. 0 warnings, 926/926 harness.**

---

## (a) Every engine member used on these paths

`ok` = our use matches the decompiled body. `fixed` = it did not, and this branch changed our side.
`proposed` = a defect in a file this round may not edit, or a fix deliberately not taken; the diff is in (b).
`screen` = the contract is right on paper and only a renderer can settle it.

### assembly_valheim.dll

| Member | Verified at | Verdict |
|---|---|---|
| `Player.m_localPlayer` (static field) | `Player.cs` | ok |
| `Player.GetComfortLevel()` | `Player.cs:1834` — public; returns 0 when `m_nview` is null | ok |
| `Player.GetPlayerName()` | `Player.cs:681` — public | ok |
| `Player.IsDead()` | `Player.cs:5409`, override of `Character.cs:2552` — public | ok |
| `Humanoid.GetInventory()` | `Humanoid.cs:833` — public | ok |
| `Character.GetSEMan()` | `Character.cs:3832` — public | ok |
| `SEMan.HaveStatusEffect(int nameHash)` | `SEMan.cs:281` — public, hash set lookup | ok |
| `SEMan.s_statusEffectRested` | `SEMan.cs:20` — `public static readonly int = "Rested".GetStableHashCode()` | ok — the hash is right, and it is vanilla's own constant rather than one we spell out |
| `Inventory.CountItems(string, int = -1, bool matchWorldLevel = true)` | `Inventory.cs:395` — sums `m_stack` where `m_shared.m_name == name` **and `item.m_worldLevel >= Game.m_worldLevel`** | ok — the world-level filter is the same one `RemoveItem` applies, so Count and Remove agree |
| `Inventory.RemoveItem(string, int, int = -1, bool worldLevelBased = true)` | `Inventory.cs:348` — **returns `void`**; walks stacks taking `Min(stack, amount)`; drops emptied entries; calls `Changed()` | ok — `DealApplier` correctly does not read a return value, and correctly pre-checks with `CountItems` |
| `Inventory.AddItem(GameObject, int amount)` | `Inventory.cs:88` — clones the prefab's `m_itemData`, **`m_stack = Min(amount, m_maxStackSize)`**, stamps `m_worldLevel = Game.m_worldLevel`, calls `Changed()` | ok — the one-stack cap is real, and `DealApplier.AddStacks` loops for it |
| `Inventory.AddItem(ItemDrop.ItemData)` | `Inventory.cs:98` — merges into free stack space one unit at a time, then places the remainder; returns false if no slot, having already mutated | screen — a partial add is not undone by vanilla; our rollback puts the *removals* back but cannot un-add. Reachable only past `CanAddItem` |
| `Inventory.CanAddItem(GameObject, int)` | `Inventory.cs:69` → `:79` — `FindFreeStackSpace(name, item.m_worldLevel) + freeSlots * maxStackSize >= stack` | proposed — it uses the **prefab template's** `m_worldLevel` while `AddItem` stamps **`Game.m_worldLevel`**. On a world level above 0 the two disagree about which stacks can be merged into. Vanilla's own inconsistency; harmless while `Game.m_worldLevel == 0` |
| `Inventory.Changed()` | `Inventory.cs:950` — **private**, and called by `AddItem`/`RemoveItem` themselves | ok — house rule 5 forbids naming it and it is not needed; the UI refreshes without us |
| `ObjectDB.instance` | `ObjectDB.cs:18` | ok |
| `ObjectDB.GetItemPrefab(string)` | `ObjectDB.cs:61` → `name.GetStableHashCode()` — **NREs on a null name** | ok — `DealApplier.Prefab` guards with `IsNullOrEmpty` first |
| `ObjectDB.UpdateRegisters` / `m_itemByHash` | `ObjectDB.cs:14`, `:20`, `:34` — field-initialised, rebuilt in `Awake` | ok — `GetItemPrefab` cannot NRE on the map |
| `ItemDrop.ItemData.GetIcon()` | `ItemDrop.cs:627` — `return m_shared.m_icons[m_variant];`, **no guard** | ok — `CargoTerminal.DrawIcon` wraps it; an item with an empty `m_icons` throws and is cached as "no icon" |
| `InventoryGui.IsVisible()` | `InventoryGui.cs:802` — static, `if ((bool)m_instance)` first | ok — safe with no world |
| `InventoryGui.Update` open gate | `InventoryGui.cs:394` — `(Chat.instance == null \|\| !Chat.instance.HasFocus()) && … && !Menu.IsVisible()`, then reads `ZInput.GetButtonDown("Inventory")` | **fixed** — see finding 2 |
| `Minimap.IsOpen()` | `Minimap.cs:392` — static, `if ((bool)m_instance)` first | ok |
| `Minimap.Update` map toggle | `Minimap.cs:616` — `ZInput.GetButtonDown("Map")` under the same chat gate | **fixed** — see finding 2 |
| `Menu.IsVisible()` | `Menu.cs:271` — static, `if (m_instance == null) return false;` | **fixed** — was not consulted at all; now a close reason |
| `Menu.Update` escape-to-open | `Menu.cs:363-367` — `flag` excludes InventoryGui / Minimap / Console / TextInput / StoreGui / radial / barber, then `ZInput.GetKeyDown(Escape) & flag && !Chat.instance.m_wasFocused` → `Show()` | screen — see finding 7 |
| `Chat.HasFocus()` | `Chat.cs:170` — the method `UIFocus` postfixes | ok |
| `Chat.m_wasFocused` | `Chat.cs:110` declared, `Chat.cs:224` **`m_wasFocused = m_input.isFocused;`** | screen — assigned from the input field directly, **not** from `HasFocus()`, so `UIFocus`'s postfix does **not** reach `Menu.Update`'s gate |
| `Console.instance` | `Console.cs:14` — public static property | ok |
| `Console.IsVisible()` | `Console.cs:87` | ok (read only via vanilla's gates) |
| `Terminal.AddString(string)` | `Terminal.cs:2517` — public | ok |
| `Terminal.ConsoleCommand` ctor / `ConsoleEventArgs` | headless-verified 2026-09-06 (CLAUDE.md status) | ok |
| `ZNet.instance`, `ZNet.IsServer()` | `ZNet.cs:1956` | ok |
| `ZNet.GetServerRPC()` | `ZNet.cs:2406` — **`return GetServerPeer()?.m_rpc;`** — null until a server peer exists, and null forever on a listen host | ok — `CargoTransport.EnsureRegistered` returns early on `IsServer()` and handles null |
| `ZNet.GetServerPeer()` | `ZNet.cs:2383` | ok |
| `ZNet.GetPeer(long)` | `ZNet.cs:1483` | ok |
| `ZNet.GetUID()` | `ZNet.cs:1787` — static | ok |
| `ZNet.GetTimeSeconds()` | `ZNet.cs:2337` | ok |
| `ZNet.IsAdmin(string)` | `ZNet.cs:2592` — public (server side, `AdminGate`) | ok |
| `ZRpc.Register<T>(string, Action<ZRpc,T>)` | `ZRpc.cs:240` — replaces by name, safe to repeat | ok |
| `ZRpc.Invoke(string, params object[])` | `ZRpc.cs:274` | ok |
| `ZRpc.IsConnected()` | `ZRpc.cs:206` | ok — `CargoTransport.Ready` re-asks every send |
| `ZRoutedRpc.instance` | `ZRoutedRpc.cs:57` — null through plugin `Awake`, new per world join | ok — `AdminRpc.EnsureRegistered` keys off instance identity |
| `ZRoutedRpc.Register<T>` / `<T,U>` | `ZRoutedRpc.cs:215`, `:220` | ok |
| `ZRoutedRpc.InvokeRoutedRPC(long, string, …)` | `ZRoutedRpc.cs` | ok |
| `ZNetView.IsValid()` / `IsOwner()` / `GetZDO()` | `p4/ZNetView.cs:263`, `:232`, `:258` | ok |
| `ZDO.Set(int, bool)` | `ZDO.cs:381` → `Set(hash, value ? 1 : 0)` | ok — both `vc_rested` and `vc_comfort` land in the ZDO **int** map |
| `ZDO.Set(int, int, bool okForNotOwner = false)` | `ZDO.cs:352` | ok — we write only on our own owned ZDO |
| `MessageHud.instance` | `MessageHud.cs:90` | ok |
| `MessageHud.ShowMessage(MessageType, string, …)` | `MessageHud.cs:156` | ok |
| `MessageHud.MessageType.TopLeft` / `.Center` | `MessageHud.cs:8-12` | ok |
| `Game.instance.GetPlayerProfile()` | `Game.cs:699` | ok |
| `PlayerProfile.GetPlayerID()` | `PlayerProfile.cs:615` | ok |
| `FejdStartup.Awake` → `ZInput.Initialize()` | `FejdStartup.cs:314` | ok — **ZInput is live at the main menu** |
| `FejdStartup.SetupObjectDB()` | `FejdStartup.cs:716-720` — `AddComponent<ObjectDB>()` then `CopyOtherDB(m_objectDBPrefab)` | ok — **`ObjectDB.instance` is live and populated at the main menu**, so the demo's names and icons can resolve there |

### assembly_utils.dll

| Member | Verified at | Verdict |
|---|---|---|
| `ZInput.instance` / `m_instance` | `ZInput:1285`, `:249` | ok |
| `ZInput.GetButtonDown(string)` | `ZInput:1820` — `m_instance?.TryGetButtonState(...) ?? false` — **null-safe** | ok |
| `ZInput.GetKeyDown(KeyCode, bool = true)` | `ZInput:1897` — `m_instance?.…  ?? false` — null-safe; reads `Keyboard.current` (new Input System) | **fixed** — Escape now goes through this |
| `ZInput.ResetButtonStatus(string)` | `ZInput:2123` — **`m_instance.m_buttons` with no null check**, unlike every getter beside it | ok, narrowly — every call site sits behind a `GetButtonDown` that already proved `m_instance` non-null. Do not lift it out of that guard |
| Registered button names `"Use"`, `"Inventory"`, `"Map"` | `ZInput:2642`, `:2658`, `:2659` (`AddButton`) | ok — all three exist and are rebindable |
| `string.GetStableHashCode()` | `assembly_utils` (`Utils`) | ok |

### Unity 6000.0 documented behaviour (no decompile; reasoned from the contract and stated as such)

| Member / behaviour | Verdict |
|---|---|
| `SystemInfo.graphicsDeviceType` vs `GraphicsDeviceType.Null` | ok — but see finding 8: it is re-read every frame, not "decided at Awake" as its comment claims |
| **Update runs before OnGUI**, every frame | ok — the whole reason `UIFocus` tokens are raised in `CargoTerminal.Tick`, and they are |
| **MonoBehaviour `Update` order between components is undefined** without a script execution order | **fixed** — this is exactly what finding 2 turns on |
| `GUI.BeginScrollView` / `EndScrollView` balance | ok — both panes wrap the body in `try/finally { GUI.EndScrollView(); }`; the `Begin` assignment is outside the `try`, which is correct (nothing to end if it threw) |
| `GUI.enabled` restore | ok — `DrawTray` sets it inside `try/finally { GUI.enabled = true; }`, and `Draw`'s own catch re-asserts it |
| `GUI.depth`, `GUI.color`, `GUI.matrix` | ok — the terminal never changes any of them; the theme's helpers save and restore `GUI.color` around each primitive |
| `GUILayout` mixed into a rect layout | ok — **not one `GUILayout` call anywhere in the window**; every rect is explicit, so nothing can desync between events |
| `Event.current` read only inside OnGUI | ok — `StageCount()` and `RightClicked()` are reachable only from `Draw()` |
| `Event.current.button == 1` on `EventType.MouseDown`, then `Use()` | ok — the right phase, and the consumption is what stops the row `GUI.Button` underneath from also firing |
| `Event.current.shift` / `.control` for the 5/20 multipliers | ok — read from the event, never from `Input` |
| `Sprite.texture` is the **atlas page** | ok — `DrawIcon` treats it as one |
| `Sprite.textureRect` normalised by the atlas size for `GUI.DrawTextureWithTexCoords` | ok — `tr.x/t.width, tr.y/t.height, tr.width/t.width, tr.height/t.height`, and Unity's texture origin is bottom-left for both, so no flip is needed |
| `Sprite.packed` with `packingRotation != None` | screen — a rotated atlas entry would draw sideways and say nothing about it. Valheim item icons are not expected to be runtime-packed; nobody has looked |
| Unity's overloaded `==` on `Object` reads a **destroyed** object as null | **fixed** — finding 4 |
| `Screen.width` / `Screen.height` inside OnGUI | ok |
| `Cursor.lockState` / `Cursor.visible` | ok — owned entirely by `UIFocus`'s `GameCamera.UpdateMouseCapture` prefix; the mod never touches them directly |

### BepInEx / Harmony

| Member | Verdict |
|---|---|
| `BepInEx.Paths.ConfigPath` | ok — `InboxStore.Path` wraps even the `Path.Combine` in a try |
| `Harmony.PatchAll()` (no args) patches the **calling assembly** | ok — this is why `Libs/ServerSync.cs` and `Libs/SharedUI/UIFocus.cs`, compiled in as shared source, are patched under **our** Harmony id |
| `Harmony.GetPatchedMethods()` — instance, this id only | ok — see (d) |
| `ServerSync.VersionCheck` static ctor → `PatchServerSync()` under id `org.bepinex.helpers.ServerSync` | ok — it early-outs because our `PatchAll()` in `Awake` beats its `ThreadingHelper.StartSyncInvoke` deferral. This is why the count is 13 and not 4 |

---

## (b) Findings

### 1 — BLOCKER. `cargo terminal demo` is inert at the main menu. *(fixed)*

`Core/CargoTick.cs`, `Tick`. `CargoTerminal.Instance.Tick(dt)` sat **below** the `if (ZNet.instance == null) …
return;` guard. `OnGUI` has no such guard, so with no world the window drew and never ticked.

**Failure.** Boot a client, do not load a world, `cargo terminal demo`. The gilt window appears. Then:

* Escape does nothing — the panel rules live in `Tick`. The only way out is the X button.
* `RefreshCounts` never runs, so `_coins` stays at its default `0`. `Confirm` → `TrayModel.Validate(m, 0, Has)`
  → `if (Net > 0 && coins < Net) return DealReason.CoinsShort` (`TrayModel.cs:165`). **Every buy in the demo is
  refused**, with the window showing "Coins 0" beside a purse of 800.
* `_tray.Refresh(CargoRpc.Market)` never runs, so the amber reconfirm cannot happen.
* `UIFocus.SetWantsCursor` is never raised. Benign at the main menu (no `GameCamera`), but it means the demo
  never exercises the token path it exists to prove.

CLAUDE.md verify item 17 is written against exactly this ("from the main menu or in a world"), and every
sentence of it after "the gilt window opens" would have failed.

**Fix.** Tick the terminal on the no-world path too, before returning. `CargoTerminal.Tick`'s own body is
already safe there: `_demo` skips the player and visit rules, `RefreshCounts` short-circuits to the demo purse,
and `ZInput.GetButtonDown` / `InventoryGui.IsVisible` / `Minimap.IsOpen` / `Menu.IsVisible` are all null-safe.

### 2 — HIGH. Tab closes the terminal *and* opens the inventory; M does the same to the map. *(fixed)*

`Client/Terminal/CargoTerminal.cs`, `Tick`. Of the three relayed buttons only `"Use"` was stood down with
`ZInput.ResetButtonStatus`.

**Failure.** Terminal open (holding `UIFocus.SetBlocksGameInput`, so `Chat.HasFocus()` answers true and vanilla's
panels are stood down). Press Tab. Our `Tick` reads `ZInput.GetButtonDown("Inventory")`, calls `Close("inventory")`,
and `Close` releases the token **in the same frame**. `InventoryGui.Update` (`InventoryGui.cs:394`) opens on
`(Chat.instance == null || !Chat.instance.HasFocus()) && …` and then reads `ZInput.GetButtonDown("Inventory")`
(`:396` onwards) — the button is still pressed and the gate is now open. MonoBehaviour `Update` order is undefined,
so on any frame where `InventoryGui.Update` runs after `CargoTick.Update` the player gets the terminal closed *and*
the inventory opened. `Minimap.Update` (`Minimap.cs:616`) is the same shape for `"Map"`.

`UIFocus`'s own header prescribes the remedy for a token holder that relays these two keys: "calling
`ZInput.ResetButtonStatus` on the button so the press is not still pending when you let go."

**Fix.** `ResetButtonStatus("Inventory")` and `ResetButtonStatus("Map")` beside the existing `"Use"` one. Safe:
both are behind a `GetButtonDown` that has already proven `ZInput.m_instance` non-null, which matters because
`ResetButtonStatus` (`ZInput:2123`) is the one member in that family with **no** null guard.

### 3 — HIGH. An accepted deal the pack refused is acked anyway, and the goods are gone. *(fixed)*

`Net/CargoTransport.cs`, `CargoTransport.OnDealt` and `LocalTransport.Send`. Both acked on `r.Ok` alone.

**Failure.** Terminal open on a live visit. The player buys 20 Iron; `Confirm` runs `DealApplier.CanApply` on the
provisional result and it passes. The packet goes out. Over the next second or two the player's pack fills (loot,
a passing autopickup). The answer arrives:

1. `CargoRpc.Send`'s wrapper marks the delivery id into the in-memory inbox **before** handing the result on
   (`CargoRpc.cs:76`) — so the inbox already claims it landed.
2. The terminal calls `DealApplier.Apply(r)`. `CanApply` now returns `inventory_full`; `Apply` returns false and
   the terminal says *"the server keeps it for you (cargo claim)"*.
3. `OnDealt` then ran `InboxStore.Save(...)` — persisting the false claim — and `AckNow(r.DeliveryId)`, which
   **clears the server's owed row**.

`cargo claim` now returns nothing; a redelivery would be swallowed by the inbox as a duplicate. The server has
decremented its stock and credited its purse for goods the player never received. The redelivery path
(`Deliveries.Handle`) already had this right — it acks only after `Apply` succeeds — so the solicited path was
inconsistent with its own sibling twenty lines away.

**Fix.** `DealApplier` — the mod's one inventory writer — now publishes `LastApplied`, the delivery id it wrote
**in full** (cleared at the top of every `Apply`). Both transports hang the inbox write and the ack off it. The
inbox is the wrong witness here by construction, because `CargoRpc` marks it before the caller applies.

**Residual, deliberately not fixed:** the *in-memory* inbox still carries the id for the rest of the session, so a
redelivery inside the same session would be treated as a duplicate. Redeliveries only arrive on `vc_claim`, which
fires once per connection, so in practice the recovery is the next login — where the persisted inbox is clean
because we no longer wrote it. Closing the in-memory hole means touching `CargoRpc.cs`, which is the frozen
contract; the proposed diff is below.

### 4 — MEDIUM. A destroyed merchant silently switches the 5 m rule off. *(fixed)*

`CargoTerminal.Tick` read `if (_merchant != null && Vector3.Distance(...) > CloseDistance)`. `!=` on a
`GameObject` is Unity's overload, which reports a **destroyed** object as null. So the moment the merchant is
destroyed — zone unload, the Odin vanish at the end of a visit, P5's despawn — the guard stops being "he is
near" and becomes "there is no merchant, skip the rule", and the terminal stays open on a merchant who is not
there. This is trap 1 from DESIGN §12 / BarrkUI §8 ("`activeInHierarchy` as well as null when caching anything on
screen") in its plainest form.

`cargo terminal open` passes null on purpose ("with no merchant to stand by"), so the two cases had to be told
apart. **Fix:** a `_hasMerchant` flag set at open; a destroyed merchant now closes with `"he is gone"`.

### 5 — MEDIUM. The cursor and input tokens outlive the tick. *(fixed)*

`CargoTick.OnDestroy` flushed the sidecar and dropped `Instance`, but never reset the terminal. With the window
open at that moment, `UIFocus._cursorWindows` and `_inputBlockWindows` keep our id forever and nothing is left
running to remove it: `GameCamera.UpdateMouseCapture`'s prefix keeps forcing the cursor free and
`Chat.HasFocus()` keeps answering true, so `Player.TakeInput()` and `PlayerController.TakeInput()` both refuse —
the player cannot move, attack, or open a panel, with nothing on screen to explain it. `UIFocus`'s header names
this exact outcome as the cost of an unbounded latch.

Narrow in practice (`ValkyriesCargo.OnDestroy` also runs `UnpatchSelf`, and their order is undefined), which is
why it is medium and not high. **Fix:** `CargoTerminal.Instance.Reset()` in `CargoTick.OnDestroy`.

### 6 — MEDIUM. Escape was the one key read through legacy `UnityEngine.Input`. *(fixed)*

`CargoTerminal.Tick` used `Input.GetKeyDown(KeyCode.Escape)`. Nothing in `assembly_valheim` reads legacy `Input`
at all — `Menu` (`:307`, `:364`), `FejdStartup` (`:1702`, `:1710`, `:1725`), `InventoryGui` (`:396`) and
`Minimap` (`:616`) all go through `ZInput`, which is a wrapper over the **new** Input System (`Keyboard.current`,
`Gamepad.current`).

I could not settle from the shipped artifacts whether this build's `activeInputHandling` is *Both* or *New only*
— the legacy `InputManager` axes are still serialised in `globalgamemanagers` and `UnityEngine.InputLegacyModule.dll`
is still listed in `ScriptingAssemblies.json`, which points at *Both*, but neither is proof. If it is *New only*,
`UnityEngine.Input.GetKeyDown` throws `InvalidOperationException` on **every frame the window is open**, `Tick`'s
catch fires, and the terminal closes itself instantly and permanently.

That is not a bet worth taking on the one way out of a modal window, and there is no cost to not taking it:
`ZInput.GetKeyDown` (`ZInput:1897`) is null-safe, honours the player's bindings, and is live at the main menu
because `FejdStartup.Awake` calls `ZInput.Initialize()` (`FejdStartup.cs:314`). **Fix:** one identifier.

### 7 — MEDIUM, cannot be fixed from these files. Escape closes the terminal *and* opens the game menu.

`Menu.Update` (`Menu.cs:363-367`):

```csharp
bool flag = !InventoryGui.IsVisible() && !Minimap.IsOpen() && !Console.IsVisible() && !TextInput.IsVisible()
            && !ZNet.instance.InPasswordDialog() && !ZNet.instance.InConnectingScreen() && !StoreGui.IsVisible()
            && !Hud.IsPieceSelectionVisible() && !UnifiedPopup.IsVisible()
            && !PlayerCustomizaton.IsBarberGuiVisible() && !Hud.InRadial();
if (((ZInput.GetKeyDown(KeyCode.Escape) || …) & flag) && !Chat.instance.m_wasFocused) Show();
```

Nothing in `flag` is reachable by an IMGUI window, and the chat term is **`Chat.m_wasFocused`**, a raw field
assigned at `Chat.cs:224` as `m_wasFocused = m_input.isFocused;` — **not** from `HasFocus()`. So `UIFocus`'s
`Chat.HasFocus` postfix, which covers `Player.TakeInput`, `InventoryGui.Update` and `Minimap.Update`, does **not**
reach this gate. `ZInput.ResetButtonStatus` is no help either: `Menu` reads the raw keyboard through
`ZInput.GetKeyDown(KeyCode)`, not the named `"Escape"` button, and named buttons are the only thing
`ResetButtonStatus` can touch.

So on a client in a world, Escape closes the terminal and the game menu comes up behind it in the same frame.
CLAUDE.md item 17 says only "Escape closes it and the cursor locks again"; the menu appearing is a real defect
against that.

**Partly mitigated on this branch:** `Menu.IsVisible()` joined the close rule, so at least the terminal is not
left drawing over the menu holding the cursor. **A real fix needs a new patch file**, which this round may not
add. Proposed, for whoever takes it (`Patches/Patch_Menu_Update.cs`, house style: prefix, `Priority.Low`,
`__runOriginal`, its own try/catch):

```csharp
[HarmonyPatch(typeof(Menu), "Update")]
internal static class Patch_Menu_Update
{
    private static int _throws;

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static bool Prefix(bool __runOriginal)
    {
        if (!__runOriginal) return false;
        try
        {
            // Menu.Update's own gate reads Chat.m_wasFocused, a field UIFocus's Chat.HasFocus postfix
            // cannot reach, so a token holder has to stand this frame down itself. Only while the menu is
            // NOT already up: closing it with Escape must keep working.
            if (SharedUI.UIFocus.BlocksGameInput && !Menu.IsVisible()) return false;
        }
        catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogError("Menu.Update prefix threw: " + ex); }
        return true;
    }
}
```

Skipping `Menu.Update` whole for one frame also skips its save-timer label and layout rebuild, which is why it is
gated on `BlocksGameInput` — it runs for no more than the frames a mod window is up. It would take the client
patch count from 13 to 14.

### 8 — LOW, report only. `HasRenderer` is not decided at Awake.

`ValkyriesCargo.cs`:

```csharp
/// <summary>Can this process draw anything at all? Decided at Awake, before ZNet exists.</summary>
public static bool HasRenderer => UnityEngine.SystemInfo.graphicsDeviceType != …GraphicsDeviceType.Null;
```

It is an expression-bodied property, so it is **re-evaluated on every read** — once per `Update`, once per
`OnGUI` **event** (so several times a frame), and again in `Patch_Terminal.Status`. The value cannot change at
runtime, so this is a doc-vs-code mismatch and a small pile of needless native interop, not a failure. Not
changed: it is not a bug, and the change would be a refactor. Proposed:

```diff
-        public static bool HasRenderer =>
-            UnityEngine.SystemInfo.graphicsDeviceType !=
-            UnityEngine.Rendering.GraphicsDeviceType.Null;
+        private static readonly bool _hasRenderer =
+            UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
+
+        /// <summary>Read once, at the static ctor of this type — which is our own Awake, before ZNet exists.</summary>
+        public static bool HasRenderer => _hasRenderer;
```

### 9 — LOW, report only. The name and icon caches remember a failure forever.

`CargoTerminal.Name()` and `DrawIcon()` write their result into `_names` / `_icons` **whether or not the lookup
succeeded**, and `Reset()` clears only `_counts`. A prefab resolved while `ObjectDB.instance` was momentarily null
(a scene transition) is pinned to its raw prefab name and to "no icon" for the life of the process, and the caches
survive a world change into a world whose synced catalogue names different prefabs.

Downgraded from "confirmed reachable" once `FejdStartup.SetupObjectDB()` (`FejdStartup.cs:716-720`) proved that
`ObjectDB.instance` is live *and populated* at the main menu — the one moment I expected this to bite. Proposed:

```diff
-            if (string.IsNullOrEmpty(name)) name = prefab;
-            _names[prefab] = name;
+            if (string.IsNullOrEmpty(name)) name = prefab;
+            // Cache a resolved name only. A miss here usually means ObjectDB was not there yet, and a
+            // negative cache would pin the raw prefab name for the life of the process.
+            if (!ReferenceEquals(name, prefab)) _names[prefab] = name;
             return name;
```

```diff
-                catch { }
-                _icons[prefab] = s;
+                catch { }
+                if (s != null) _icons[prefab] = s;
```

plus `_names.Clear(); _icons.Clear();` in `Reset()`.

### 10 — LOW, report only. `CanApply` measures room before the removals free it.

`DealApplier.CanApply` checks `inv.CanAddItem(prefab, count)` against the inventory **as it is**, while `Apply`
removes first and adds second. A deal whose additions only fit *because* its removals happened (paying 200 coins
out of a full pack for one stack of iron) is refused `inventory_full` and never sent. Conservative — it errs
towards not touching the pack, which is guarantee 5 — but it will read as "the terminal refuses a deal that
obviously fits". Fixing it means simulating the removals, which is a refactor of the check, so: report, and a
verify item.

### 11 — LOW, report only. Latent IMGUI control-id drift.

Two controls are conditionally emitted: the Barter toggle (`ModConfig.EnableBarter.Value`, a synced value) and
"Fill from my goods" (`_tray.Mode == Barter && _tray.Wanted != null`). Emitting a `GUI.Button` conditionally
shifts every later control's id. IMGUI only cares when the id set changes **between the `MouseDown` that made a
control hot and the `MouseUp` that fires it**, and neither condition can flip in that window (staging needs its
own mouse-down; ServerSync broadcasts on change, off-frame). The row loops have the same shape over `m.Rows`,
whose length is fixed for a visit. Not a bug today; it is one config broadcast landing mid-drag away from being
one, so it is written down.

### 12 — Verified clean, no change

* **`ComfortReporter`** — the interval is 2 s and holds on a `Reset`; the write is gated on
  `IsValid() && IsOwner()` so it only touches our own ZDO; no per-frame allocation; `GetComfortLevel()` and
  `HaveStatusEffect(s_statusEffectRested)` are both public and the hash is vanilla's own constant; it is called
  only under `if (ValkyriesCargo.HasRenderer)`, so a dedicated server never runs it.
* **`DealApplier`** apart from finding 3 — the signatures are right (`RemoveItem` void, `AddItem` stack-capped,
  `CountItems`/`RemoveItem` agreeing on the world-level filter), `Changed()` is correctly not called because it
  is private and vanilla calls it, the coins prefab is `"Coins"`, and the removals-then-additions order with a
  remembered rollback is sound.
* **`InboxStore`** — never throws on a missing or corrupt file; `.tmp` then delete-then-move. The replace is not
  atomic, so a kill in that window loses the inbox — which costs one redelivery that the server's ledger already
  handles. Acceptable as designed.
* **`AdminRpc`** — `OnRequest` refuses anything but a server, so a hostile server invoking `vc_admin` on a client
  does nothing; `Send` cannot reach `ZRoutedRpc.instance` without `Registered` having proved it non-null;
  `Reset()` drops the registration so the next world re-registers.
* **`CargoTransport.EnsureRegistered`** — keyed on `ReferenceEquals(rpc, _rpc)`, so a reconnect's new `ZRpc` is
  re-registered and a repeat is free (`ZRpc.Register` replaces by name). `Ready` re-asks `IsConnected()` on every
  send rather than trusting a flag.
* **Install order** — `AddComponent<CargoTick>()` runs `Awake` synchronously but the first `Update`/`OnGUI` is
  next frame, and `CargoTerminal.Install()` is before that; `OnGUI` null-checks `Instance` regardless.
* **House style** — every patch body and every entry point is its own try/catch capped at three logs; the two
  prefixes are `Priority.Low` and honour `__runOriginal`; no private member is named (`Inventory.Changed` and
  `RandEventSystem.SetRandomEvent` were both checked and both avoided); no coroutine; no `EnvMan` patch; no
  material, texture or shader is touched outside the vendored theme's own procedural bake.

---

## (c) What only a screen can settle — proposed additions to CLAUDE.md "What to verify in-game"

Fold these into items 17 and 18.

**17a — the demo at the main menu, after this branch.** `cargo terminal demo` with no world: the window opens,
the header reads **"Coins 840"** (not 0), Escape closes it, and `cargo status` shows
`terminal closed (last: escape)`. Before this branch the coins read 0 and Escape did nothing.

**17b — icons and names in the demo.** All 18 wares show an icon, and the names are localised
("Deer hide", not "DeerHide"). If icons are missing at the **main menu** but present **in a world**, that is
finding 9's negative cache and the proposed diff applies. If a specific item's icon is garbled or sideways, that
is the packed-sprite rotation case: report which item.

**17c — Escape and the game menu.** In a **world**, with the terminal open, press Escape once. Expected today:
the terminal closes **and Valheim's menu opens behind it** (finding 7). Record whether it does. If it does,
finding 7's `Patch_Menu_Update` prefix is the fix and the client patch count becomes 14.

**17d — Tab and M.** With the terminal open, press Tab: the terminal closes and the **inventory must not** open.
Then M: the terminal closes and the **map must not** open. This is finding 2's fix; it is order-dependent, so try
it several times and after a world reload.

**17e — the cursor is put down.** Close the terminal by every route (Escape, X, Use/E, Tab, M, "Send him off"
twice) and after each one confirm the camera turns with the mouse again and E interacts. Then log out with the
terminal open and confirm the same at the character-select screen.

**17f — the window at the extremes.** `TerminalScale` at 0.5 and at 2.0 on the smallest window the client can
make (1280x720) and on the largest: the two panes must still have positive height and the tray row must not
overlap the footer. The frame's `Band`/`Pad` are deliberately unscaled, so the content shrinks inside a
fixed-width frame.

**18a — a deal the pack refuses.** With a visit running, fill the inventory to one free slot, stage a buy of two
full stacks, and confirm. Expected: the client says "the server keeps it for you (cargo claim)", the server's
`cargo status` still shows **`owed ledger 1 row(s)`**, and after making room `cargo claim` delivers it. Before
this branch the row was cleared and the goods were lost — this is finding 3 and it is the single most valuable
line on this list.

**18b — the merchant vanishing under the window.** Once P5 exists: open the terminal on the merchant, have an
admin `cargo dismiss`. The terminal must close with `he is gone` (finding 4), not sit open.

**18c — a deal that only fits after its own removals.** Full pack, 200 coins, buy one stack of iron. If the
terminal refuses it `inventory_full`, that is finding 10 and it wants the `CanApply` rework.

---

## (d) The patch count a client boot should print

**`patches=13`** — the same as the dedicated server, and unchanged by this round (no patch was added or removed).

`ValkyriesCargo.Awake` prints `_harmony.GetPatchedMethods().Count()`, which is the count of **distinct methods**
patched by **our** Harmony instance (`com.raveniron.valkyriescargo`). `PatchAll()` with no arguments patches every
`[HarmonyPatch]` type in the **calling assembly**, and both vendored shared sources compile into that assembly, so
they are counted here rather than under their own ids:

| Source | Method | Count |
|---|---|---|
| `Libs/ServerSync.cs` | `ZRpc.HandlePackage` | 1 |
| | `ZNet.Awake` | 2 |
| | `ZNet.OnNewConnection` *(two patch classes, one method)* | 3 |
| | `ZNet.Shutdown` | 4 |
| | `ZNet.RPC_PeerInfo` *(two patch classes, one method)* | 5 |
| | `ZNet.Disconnect` | 6 |
| | `ConfigEntryBase.GetSerializedValue` | 7 |
| | `ConfigEntryBase.SetSerializedValue` | 8 |
| | `FejdStartup.ShowConnectError` | 9 |
| `Libs/SharedUI/UIFocus.cs` | `Chat.HasFocus` | 10 |
| | `GameCamera.UpdateMouseCapture` *(prefix + postfix, one method)* | 11 |
| `Patches/Patch_Terminal.cs` | `Terminal.InitTerminal` | 12 |
| `Patches/Patch_RandEventSystem_Awake.cs` | `RandEventSystem.Awake` | 13 |

Every one of those types lives in `assembly_valheim.dll` or BepInEx and exists in both the client and the
dedicated-server process, so the number does not move with the role. It matches the two headless runs already in
CLAUDE.md (`patches=10` before `UIFocus` and the event prefix, `patches=13` after), which also confirms the
mechanism: `ServerSync.VersionCheck`'s static constructor queues its own `PatchServerSync()` through
`ThreadingHelper.StartSyncInvoke`, and that runs a frame later, by which time our `PatchAll()` has already
applied those classes — so its `GetPatchInfo` guard returns early and nothing is double-patched under
`org.bepinex.helpers.ServerSync`.

**If a client boot prints anything other than 13, that is itself the finding**: 4 would mean `PatchAll()` did not
reach the vendored shared sources, and 22 would mean ServerSync patched itself a second time under its own id.
