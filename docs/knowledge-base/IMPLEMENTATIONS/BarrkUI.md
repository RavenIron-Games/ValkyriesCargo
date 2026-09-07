# BarrkUI — Valheim UI overhaul mod

**Started 2026-08-04.** BepInEx/Harmony, `net48`, ServerSync. GUID `wubarrk.BarrkUI`.
Source: `c:\WubarrkCODING\BarrkUI`.

Two things make this project worth reading beyond its own feature set:

1. **It is the first project in this workspace built entirely on the `<Compile Include>` shared-source
   pattern** rather than vendoring copies of `ServerSync.cs` and the gilt theme. See
   [SharedInfrastructure.md](SharedInfrastructure.md) for the standing rule it was built to honour.
2. **Its chat item-share went through a design correction that generalises**: two RPCs that cannot be
   ordered became one RPC that cannot race itself. See §3.

Vanilla-API findings extracted from this build live in
[../VALHEIM-API-REFERENCE/12-CHAT-AND-CUSTOM-RPC.md](../VALHEIM-API-REFERENCE/12-CHAT-AND-CUSTOM-RPC.md) —
read that for the decompile citations. This file covers the mod's own decisions.

---

## 1. Layout

| Path | Role |
|---|---|
| `Plugin.cs` | `BaseUnityPlugin`. Config init + a per-type `PatchAll` loop; `Update()`→`Tick()`, `OnGUI()`→`Draw()` |
| `Configuration/ConfigManager.cs` | `ConfigSync` + `BindLocal`/`BindLocalRanged`/`BindSynced` split |
| `Chat/ChatMessagePatch.cs` | Case-preservation transpilers + item-share link rendering |
| `Chat/AlwaysShoutPatch.cs` | `Chat.SendText` prefix forcing `Talker.Type.Shout` |
| `Chat/ItemShareRpc.cs` | The `BarrkUI_ChatItem` RPC, its per-session registration patch, and receiver-side item resolution |
| `Chat/ItemShareProtocol.cs` | PUA-sentinel wire marker and the escaped-markup output stages |
| `Chat/ItemShareCache.cs` | Bounded (300) share cache, capped to match `Terminal.m_maxBufferLength` |
| `Chat/ChatOverhaulWindow.cs` | The IMGUI panel: always-shout toggle, item drop zone, link hover tooltip |
| `compat-against/` | Read-only decompiles of mods we must not break. **Excluded from compilation** — SDK projects glob every `.cs`, so `<Compile Remove="compat-against\**\*.cs" />` is mandatory |

Shared code is referenced, never copied:

```xml
<Compile Include="..\libs-Tools\ServerSync.cs" Link="Libs\ServerSync.cs" />
<Compile Include="..\libs-Tools\SharedUI\GiltFrameTheme.cs" Link="Libs\SharedUI\GiltFrameTheme.cs" />
<Compile Include="..\libs-Tools\SharedUI\UIFocus.cs" Link="Libs\SharedUI\UIFocus.cs" />
```

### Reference assemblies that are not obvious

- **`Splatform.dll`** — `PlatformUserID`, the first parameter of the `Terminal.AddString` overload chat
  actually uses. Not in `assembly_valheim`.
- **`gui_framework.dll`** — `GuiInputField`, the type of `Terminal.m_input`. Needed to read chat focus
  directly (§4).
- **`UnityEngine.InputLegacyModule.dll`** — `UnityEngine.Input`.

⚠ **`libs-Tools\assembly_publicizer.dll` carries the assembly *identity* `assembly_valheim`** (a stale
publicized copy, 2,215,936 bytes vs the real 2,126,848). The `.csproj` doesn't reference it so the build is
unaffected, but it **shadows the real assembly for any tool that scans that folder by identity** — it did
exactly that to an audit pass's reflection probe. Left in place; know it is there.

---

## 2. Chat features

### Case preservation
Vanilla upper-cases Shout and lower-cases Whisper in three method bodies, as **local reassignments** no
pre/postfix can reach. A transpiler drops just the `ToUpper()`/`ToLowerInvariant()` call instructions —
stack-neutral, so nothing else in those bodies is touched. Both of the ways this fails *silently* (culture
overload mismatch; collateral `ToLower` on a comparison key) were ruled out by IL disassembly, not by
reading the decompiler output. Details and citations in the API reference, §6.

### Always Shout
A `Chat.SendText` prefix flipping `ref Talker.Type type`. `SendText` is the choke point both vanilla's
`/s`,`/w`,`say` command parsing and other mods' outgoing chat funnel through, so patching it beats patching
the input parser. ⚠ The item-share path deliberately **does not** go through `SendText`, so it applies the
same override itself — a second call site to keep in sync.

### Item sharing
Drag an item from the inventory onto the panel's drop zone. Drag state is read from
`InventoryGui.m_dragGo`/`m_dragItem` **directly in `Update()`**, not by patching drop handlers, because
Azumatt's `AzuExtendedPlayerInventory` prefixes `InventoryGui.OnSelectedItem` / `InventoryGrid.DropItem`
(`EpiDropRouter.ValidatePlannedDrop`) and can cancel a drop before those return.

The resulting chat line carries a hoverable `<link>` region; hovering it draws a gilt tooltip with the
item's real stats, resolved against **the viewer's own** `ObjectDB` so other mods' `SharedData` edits and
`GetTooltip()` postfixes show through for free.

---

## 3. ⭐ The design correction: two RPCs that cannot be ordered → one that cannot race itself

**First design.** Broadcast item data on `BarrkUI_ShareItem`, then send an ordinary vanilla chat message
containing a marker; the receiver looks the marker up in a cache the RPC populated.

**Why it was wrong.** Those are two independent messages on two different routes — vanilla chat leaves as
a routed `"ChatMessage"` for Shout but as a ZDO-targeted `"Say"` for Normal/Whisper, and a dedicated server
relays them with no cross-route ordering guarantee. Lose that race and the marker resolved to dead
`[shared item]` text **permanently**, because resolution ran once as the message displayed and never again.
The bounded cache added a second, slower version of the same failure via eviction.

**The fix.** One RPC carrying the chat line *and* the item payload:

```csharp
ZRoutedRpc.instance.Register<Vector3, int, UserInfo, string, ZPackage>("BarrkUI_ChatItem", RPC_ChatItem);
```

The handler caches the item, **then** hands the text to `Chat.instance.OnNewChatMessage(...)`, in the same
call stack. The cache hit is guaranteed by construction; there is no retry logic because there is nothing
to retry. `ZPackage` also removes the 6-generic ceiling that had forced `worldLevel` off the wire.

**What was deliberately NOT replaced.** Only the transport. Sending still goes through
`Chat.CheckPermissionsAndSendChatMessageRPCsAsync` + `Chat.GetChatMessageData`, so platform mute/block
checks and profanity filtering run per recipient exactly as vanilla runs them; receiving still lands in
`OnNewChatMessage`, so the `<`/`>` strip, chat buffer, world text and hide timer behave normally. **Ordinary
chat is untouched vanilla** — only share lines use the custom RPC.

**Cost, stated plainly:** a player without BarrkUI has no handler and sees share lines not at all.
Acceptable for a mod-exclusive message type; would be wrong for anything that should degrade gracefully.

**Two bugs it killed for free.** Sender spoofing (a peer can no longer attach another player's item data to
their own message, since both arrive together and share ids encode their origin) and the eviction-race
degradation above.

### Getting markup past vanilla's guard
`OnNewChatMessage` strips `<` and `>` from every message — a griefing guard that must not be weakened
globally. So no real angle bracket is ever put on the wire: the marker is a Private-Use-Area sentinel
(`U+E000`/`U+E001`), expanded in a **prefix** into a `<link>` fragment whose own brackets are a *second* PUA
pair (`U+E002`/`U+E003`), which prefixes on `Terminal.AddString` convert to real brackets after the strip
has already run. The only string ever promoted into live markup is our own `DisplayName`, resolved locally
from `Localization`/`ObjectDB` — never a byte read verbatim out of an incoming message.

⚠ Write PUA codepoints as `(char)0xE000`, not as `\uE000` escapes or literal characters — both render
invisibly in editors and diffs.

---

## 4. Audit findings worth carrying forward

A full post-implementation audit (2026-08-04, decompile + IL) found the mod **completely inert on boot**
and several issues that generalise beyond this project:

| Finding | Generalises to |
|---|---|
| `ZRoutedRpc.instance` is null in plugin `Awake`; the throw aborted `Awake` **before `PatchAll`**, so no patch applied at all | Any mod registering an RPC. The "does nothing, logs nothing" signature. API ref §1 |
| `IsChatDialogWindowVisible()` means "someone spoke in the last 10s", so the cursor unlocked constantly | Any UI gated on chat visibility. API ref §5 |
| A visibility flag set once and never cleared (`_initialized`) left an invisible drag hot-zone over the inventory for the session | Any screen-space hit test that outlives its panel |
| `m_worldLevel` is 0 on prefab `ItemData`, so cloned tooltips silently showed world-level-0 stats | Anything rendering a tooltip from a cloned prefab. API ref §8 |
| `Sprite.texture` is the whole atlas page — `GUI.DrawTexture` on an item icon draws every icon at once | Any IMGUI that draws a game sprite. API ref §8 |
| `CalcSize` on a wrapping style measures one unwrapped line; and every `GiltFrameTheme` style is `wordWrap=false`/`clipping=Clip` | Any multi-line IMGUI content. Derive a wrapping style; use `CalcHeight` when laying out raw `Rect`s, but inside `GUILayout` the label sizes itself. ✅ fixed in both — see Fatty's `CLAUDE.md` for why only BarrkUI needed the manual `CalcHeight` |
| Writing window position to a `ConfigEntry` every frame rewrites the whole `.cfg` to disk per frame while dragging (BepInEx `SaveOnConfigSet` defaults true) | Every draggable IMGUI window. Was also present in Fatty's `FeastLedgerGui.cs` — fixed there 2026-08-04 too, see `Fatty/CLAUDE.md` |
| A persisted off-screen position had no clamp and no in-game recovery | Every persisted window rect. Also fixed in Fatty 2026-08-04 |
| `?.` on a `UnityEngine.Object` bypasses the fake-null operator | Everywhere. Use `!= null` |
| `MinimumRequiredVersion = PluginVersion` on a mod with **zero** synced entries would hard-block players on the old build for nothing | Any client-side mod that instantiates `ConfigSync` out of habit |
| A comment asserted a sender-verification check that the code did not implement | Comments that promise guarantees are worse than no comment |

---

## 5. ⭐ Debug mode, and the difference between TRACE and HEALTH

`Diagnostics.cs` deliberately separates two things that get conflated into one "verbose logging" flag:

| | Gated? | Answers | Example |
|---|---|---|---|
| **TRACE** | Yes — `99 - Debug/Debug Logging` | *What happened* | "share `a3f-2` resolved to Iron Sword" |
| **HEALTH** | **Never** | *Does this feature still exist* | "Case preservation (chat log) is not working: found no ToUpper() calls to remove" |

The distinction earns its keep because **this mod's two riskiest mechanisms both fail silently**:

- **A transpiler that matches nothing still reports success.** Harmony has no opinion about whether your
  edit did anything. If a game update moves those `ToUpper()` calls, case preservation stops working with
  a completely clean log. `RemoveForcedCase` now counts removals and reports through `Diagnostics.Health`,
  warning if the count is not exactly 2 per method. *(Implementation note: the tail of an iterator runs when
  enumeration completes, and Harmony fully enumerates transpiler output before compiling — so the report
  fires exactly once per patched method.)*
- **An RPC registered against a null or replaced `ZRoutedRpc` never fires and logs nothing.** Registration
  reports health per session.

Trace takes a `Func<string>`, not a `string` — these sit on per-frame and per-message paths, and a passed
interpolated string would be built every call regardless of the gate, which is the exact cost the gate exists
to avoid.

`Debug Overlay` is a **separate** setting from `Debug Logging`, not a sub-option, because it answers a
question logging structurally cannot: *why did nothing happen*. The interesting failures here — a closed
visibility gate, a drag that was never seen, a pointer just outside the drop zone — are cases where **no code
path runs**, so there is nothing to log. The overlay draws current state instead, and deliberately still
draws when the panel itself is hidden.

`barrkui_debug` is registered **without a Harmony patch**. The obvious anchor — a postfix on
`Terminal.InitTerminal` — is actively worse: it is called from `Terminal.Awake` (37658) and guarded by
`m_terminalInitialized` (35717), so if any Terminal wakes before BepInEx applies patches, the command
silently never exists. A `ConsoleCommand` registers itself into a static dictionary on construction (35518)
that is a plain field initialiser (35659) and is only ever added to — so registering directly at plugin Awake
has no timing window at all. **Generalises: check whether you need an anchor before reaching for one.**

## 6. Waiting for the world — `SessionState.cs`

Readiness is **computed from the live objects**, never tracked with a bool set by a "world loaded" event:

```csharp
public static bool IsLive => PlayerReady && ContentReady;
```

A tracked flag has to be correct on every path that could change it — failed joins, dropped connections,
logout-to-menu, character switches — and is wrong forever the first time one is missed. A computed property
cannot desync because there is nothing to keep in sync. Harmony patches are used **only** for the thing a
computed property genuinely cannot do: tearing down per-session state (`Game.Logout`, `ZNet.Shutdown`, both
patched because neither implies the other; `Reset` is idempotent).

Three gates worth knowing individually, since "not ready" has very different causes:

- `ZNet.instance.LocalPlayerCharacterID != ZDOID.None` — it is `ZDOID.None` (UserID **0**) until the player
  has actually spawned, so anything keyed off it before then collides across every player on the server.
- `ObjectDB.instance.m_items.Count > 0` — **ObjectDB exists in the main menu too** (character preview), so
  the instance check alone is not enough.
- `Player.m_localPlayer != null`.

`SessionState.Describe()` returns which gate is closed, in words, for the overlay and status command.

⚠ **The cursor-request leak this caught is worth remembering.** `SharedUI.UIFocus` replaces
`GameCamera.UpdateMouseCapture` for as long as *any* window has a request registered. Logging out while a
window held one left the cursor unlocked in the main menu and in every world joined afterwards, with nothing
on screen to explain it. Any registry-style shared state needs an explicit release on session end, not just
on window close.

## 7. Compat

Audited against decompiles kept in `compat-against/`:

- **`Azumatt-AzuExtendedPlayerInventory-2.4.3`** — two real conflicts. Its `EpiDropRouter` prefixes can
  cancel drops (hence reading drag state directly, §2), and its `JCBPUIUseBleedGuard` postfix on
  `GameCamera.UpdateMouseCapture` can re-lock the cursor after `UIFocus`'s prefix releases it. Handled by a
  `[HarmonyPriority(Priority.First)]` postfix in `SharedUI/UIFocus.cs` that reasserts two fields we already
  own — explicitly **not** the retracted broad max/min-priority ordering steamroll documented in
  [ShadowsOfMidgard.md](ShadowsOfMidgard.md).
- **`Marsarah-MarsarahTweaks-1.5.1`** — no conflicts found.

---

## 6. Config

All entries are `BindLocal` (unsynced) except the ServerSync locking entry. Window geometry is **never** a
sync candidate — where you dragged your own window is as personal as your font size.

| Section | Keys |
|---|---|
| `1 - General` | `Lock Configuration` |
| `2 - UI Theme` | `UIGoldColour`, `UITextScale`, `UIFontSizeDelta` — shared with Fatty/TortalPortal; **match the values across mods**, see the theme-conflict note in [SharedInfrastructure.md](SharedInfrastructure.md) |
| `3 - Chat` | `Always Shout` |
| `10 - Window Positions` | `Chat Window X/Y/Width/Height` |

---

## 8. Four Unity/Valheim traps worth carrying to any UI mod (added 2026-08-12, 0.9.4)

Everything about the **input-focus timing** trap has been promoted to `SharedInfrastructure.md` under
`UIFocus.cs`, because it belongs to the shared file rather than to this project. The four below are not
shared-file material but are general.

- **"Closed" and "destroyed" are different questions, and Valheim almost always means the first.**
  `PanelFrame` cached the inventory's background `RectTransform`s and invalidated the cache when an entry
  went Unity-null. Valheim closes a panel with `SetActive(false)`, so nothing ever went null: opening a
  chest left the gilt frame wrapped around the previous set of boxes for the full length of the rescan
  timer and then jumped. **Any cache of "what is on screen" needs an `activeInHierarchy` test as well as a
  null test**, plus a cheap signature of the containers so an *appearance* is caught too — a null sweep can
  only ever notice things leaving.

- **A subtree you cached can be rebuilt wholesale underneath you.** `Hud.UpdateStatusEffects` (decompile
  40455) does not add or remove one entry when a buff changes — the moment the count differs it `Destroy`s
  every entry and `Instantiate`s the whole row again. `LayoutUpright` cached text nodes keyed on the
  *owner's* instance id, which never changed, so the cached nodes were dead and their replacements had
  never been processed. `childCount` on the list root is an exact O(1) signal here. **And restore the old
  set before re-collecting**, or a node that survived the rebuild has your own correction recorded as its
  baseline — the "never let a restore record its own output as the original" trap, reached from a new
  direction.

- **A stored position must name a point on the box, not always its corner.** Every VikingOS widget is
  measured from its own text, so pinning them by their top-left corners meant no pair of default fractions
  could line two of them up at more than one text size — reported as *"nothing is in line with each
  other"*. The fix is an **anchor** per widget (centre / right / left) with the stored fraction naming that
  point; a centred widget then grows symmetrically about the line it sits on and stays aligned for free at
  any resolution, any text size. `HudWidgets.AnchorInset` / `AnchorFraction` must stay each other's
  inverse or a widget creeps every time it is picked up and put down.

- **TMP sprite size is `fontSize / spriteAsset.faceInfo.pointSize`.** Setting `pointSize` to the atlas cell
  height is what makes an emote track the chat font — and it silently fixes the drawn size at exactly one
  line of text, which is far too small for an animation (*"GIFs are way too small in the chat"*). Divide
  `pointSize` by the wanted multiple to scale it, dividing `lineHeight`/`ascentLine`/`descentLine` with it
  so the line box grows to hold the sprite. It re-applies to a live atlas with no rebuild — only the ratio
  changes, not a pixel — but TMP caches the metrics it laid out with, so call `SetAllDirty()` on the text.

## 7. Status

**0.9.2 ran in game and was reported a success (2026-08-12).** That release contained everything 0.9.0 and
0.9.1 added, so the frame styles, the compass and its map pins, the per-style text sizes and the colour set
have all now been exercised at runtime, along with 0.9.2's own six fixes. It was a single verdict rather
than a per-item sheet, so treat individual items as "believed working" rather than "confirmed".

**0.9.4 builds clean (0 warnings, 0 errors) and is packaged, but has not been run.** It is five items: the
input-focus timing fix above, the panel-frame cache fix, a computed first-run default arrangement with
anchored widgets, upright status-effect icons, and an emote size setting.

Still never exercised at runtime: `vikingos_whispertest` (built, never run), and trading's negotiation half,
which needs a second client. Trading's *settlement* path passes — `vikingos_tradetest`, 32 checks.
