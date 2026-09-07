# 12 — Chat, custom routed RPCs, and item-data-over-the-wire

**Added 2026-08-04**, from the BarrkUI chat-overhaul build and its post-implementation audit. Every claim
below is cited to `libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`, and the items
marked **IL-verified** were additionally confirmed by disassembling the live method body rather than
reading the decompiler's C# rendering — the reference DLL in `libs-Tools` was checked byte-identical to
the installed game's (SHA256 `3B26C851…`, 2,126,848 bytes) first.

This file exists because the chat surface breaks three reasonable assumptions at once: RPC handlers do
not live as long as your plugin, "the chat window is visible" does not mean what it sounds like, and the
method you want to patch does its real work inside a closure that runs later.

---

## 1. `ZRoutedRpc.instance` is null for the whole of plugin `Awake` — and it is replaced every world join

```
70869:	private static ZRoutedRpc s_instance;
70871:	public static ZRoutedRpc instance => s_instance;
70873:	public ZRoutedRpc(bool server)
70875:		s_instance = this;
```

`s_instance` is assigned **only** in that constructor, and the only `new ZRoutedRpc` in the assembly is:

```
66893:	private void Awake()                       // ZNet.Awake
66897:		m_routedRpc = new ZRoutedRpc(m_isServer);
```

`ZNet` lives in the **main scene**, loaded by `FejdStartup.LoadMainScene()` (`84646`). A BepInEx plugin's
`Awake` runs in the **start scene**, long before that. So:

```csharp
private void Awake()
{
    ZRoutedRpc.instance.Register<...>("MyRpc", Handler);   // NullReferenceException, every single boot
}
```

Worse, the handler table is an **instance** field:

```
70867:	private readonly Dictionary<int, RoutedMethodBase> m_functions = new Dictionary<int, RoutedMethodBase>();
71007:			if (m_functions.TryGetValue(data.m_methodHash, out var value))
```

A fresh `ZRoutedRpc` is constructed on **every** `ZNet.Awake`, i.e. every world entry. A one-shot
registration is therefore discarded on the next world join even if it had somewhere to land. Unregistered
hashes are dropped silently — no log, no error, the RPC simply never fires.

> **Register per session, from a method that vanilla itself uses `ZRoutedRpc` in.** `Chat.Awake` is a good
> anchor for anything chat-shaped, because vanilla registers `"ChatMessage"` there — proof the instance is
> live by that point:
> ```
> 34214:	public override void Awake()
> 34218:		ZRoutedRpc.instance.Register<Vector3, int, UserInfo, string>("ChatMessage", RPC_ChatMessage);
> ```
> `Game.Start` works equally well for non-chat RPCs. Both re-run per session, which is the property you
> actually need.

### Failure signature

If registration throws inside your plugin's `Awake`, Unity swallows the exception and **aborts the rest of
`Awake`**. Anything after it — including `Harmony.PatchAll` — never runs, so the whole mod goes quiet while
`Update`/`OnGUI` keep ticking. A mod that "does nothing at all, but logs no error" is usually this.

---

## 2. `Register` tops out at 6 generic args — use a `ZPackage` instead of designing around it

```
71049:	public void Register<T, U, V, B, K>(string name, RoutedMethod<T, U, V, B, K>.Method f)
71054:	public void Register<T, U, V, B, K, M>(string name, RoutedMethod<T, U, V, B, K, M>.Method f)
69935:	public delegate void Method(long sender, T p0, U p1, V p2, B p3, K p4, M p5);
```

Six is the ceiling, and the `long sender` is implicit and additional — it is **not** one of the six.

`ZPackage` is itself a legal parameter type, so one generic slot buys unlimited payload:

```
71381:			else if (obj is ZPackage)
71383:				pkg.Write((ZPackage)obj);
70500:	public void Write(ZPackage pkg)
70502:		byte[] array = pkg.GetArray();
```

`Write(ZPackage)` copies via `GetArray()`, so it does **not** consume the source package's read/write
position — one payload object can be handed to many `InvokeRoutedRPC` calls in a loop. On receipt,
`ReadPackage()` hands back a fresh package positioned at 0 (`70750`, and the `byte[]` ctor sets
`m_stream.Position = 0` at `70497`).

Types `ZRpc.Serialize` handles directly (`71349`–`71419`): `int`, `uint`, `long`, `float`, `double`,
`bool`, `string`, `ZPackage`, `List<string>`, `Vector3`, `Quaternion`, `ZDOID`, `HitData`, and anything
implementing `ISerializableParameter` (`60498`) — which includes `UserInfo` (`61474`).

**Version your payload.** Write an `int` version and an `int` count first; a client on an older build then
declines cleanly instead of desynchronising the read and consuming garbage.

---

## 3. Chat send/receive topology — the two routes are *not* symmetric

```
34561:	public void SendText(Talker.Type type, string text)
34568:		if (type == Talker.Type.Shout)
34570:			CheckPermissionsAndSendChatMessageRPCsAsync(delegate(long user, bool filterText)
34573:				ZRoutedRpc.instance.InvokeRoutedRPC(user, "ChatMessage", ..., 2, userInfoToSend, textToSend);
34578:			localPlayer.GetComponent<Talker>().Say(type, text);
```

| Channel | Transport | Distance filtering |
|---|---|---|
| **Shout** | routed `"ChatMessage"`, sent **per recipient** across the whole player list | **None.** Shout is server-wide |
| **Normal / Whisper** | `Talker.Say` → `m_nview.InvokeRPC(user, "Say", …)`, ZDO-targeted | On **receipt**, in `RPC_Say` |

```
26871:	public void Say(Type type, string text)
26881:	private void RPC_Say(long sender, int ctype, UserInfo user, string text)
26888:			case 0: num = m_visperDistance;      // 4f   (26854)
26891:			case 1: num = m_normalDistance;      // 15f  (26856)
26894:			case 2: num = m_shoutDistance;       // 70f  (26858)
26898:			if (Vector3.Distance(...) < num && (bool)Chat.instance)
26901:				Chat.instance.OnNewChatMessage(base.gameObject, sender, headPoint, (Type)ctype, user, text);
```

`m_shoutDistance = 70f` exists on `Talker` but is **dead for real shouts** — they never travel the `Say`
path. It only applies if something else calls `Say(Type.Shout, …)` directly.

Both routes converge on **`Chat.OnNewChatMessage`**, which is therefore the single funnel to patch for
anything display-related. Note the `go` argument differs: `RPC_Say` passes the speaker's GameObject (so
world text tracks them), while routed chat passes `null` (`34558`), leaving world text parked at `pos`.

---

## 4. `Chat.OnNewChatMessage` does its work in a closure that runs *later*

```
34364:	public void OnNewChatMessage(GameObject go, long senderID, Vector3 pos, Talker.Type type, UserInfo sender, string text)
34366:		if (senderID == ZNet.instance.LocalPlayerCharacterID.UserID)
34368:			OnCheckPermissionAsyncCompleted(RelationsManagerPermissionResult.Granted);
34372:			RelationsManager.CheckPermissionAsync(sender.UserId, Permission.CommunicateWithUsingText, isSender: false, OnCheckPermissionAsyncCompleted);
34374:		void OnCheckPermissionAsyncCompleted(RelationsManagerPermissionResult result)
34384:					text = text.Replace('<', ' ');
34385:					text = text.Replace('>', ' ');
34386:					if (result == RelationsManagerPermissionResult.GrantedRequiresFiltering)
34388:						CensorShittyWords.Filter(text, out text);
34390:					if (type != Talker.Type.Ping)
34393:						AddString(sender.UserId, text, type);
34397:						AddInworldText(go, senderID, pos, type, sender, text);
```

Two things fall out of this that are easy to get wrong:

**(a) Vanilla strips `<` and `>` from every chat message, sent or received.** That is the guard against a
player typing rich-text markup into chat. Do not weaken it globally — it is a real griefing vector. If you
need markup in chat, put a marker on the wire that contains no angle brackets (Unicode Private-Use-Area
characters `U+E000`+ work well; no keyboard or IME can produce them), expand it to markup in a **prefix**,
and write the markup's own brackets as a second PUA pair so the strip about to run cannot see them either.
Convert back to real brackets in a prefix on `AddString`, which runs *after* the strip.

**(b) A Harmony prefix with `ref string text` DOES reach the strip** — **IL-verified**. The strip operates
on the closure's captured field, but the compiler hoists the *argument* into the display class as the
method's first instructions, before any patched body logic:

```
IL_0000 newobj  <>c__DisplayClass12_0
IL_0008 stfld   <>c__DisplayClass12_0::<>4__this
IL_000E ldarg.s 6                                  // the `text` PARAMETER
IL_0010 stfld   <>c__DisplayClass12_0::text
```

A prefix writes the argument slot before the body runs, so the hoist picks up the modified value. This is
general: **capturing a parameter in a closure does not put it out of a prefix's reach**, because the copy
into the display class always happens at method entry.

`CensorShittyWords.Filter` (`34388`) runs *after* your prefix, and again inside `GetChatMessageData`
(`34636`) on the send side. It leaves PUA codepoints alone.

---

## 5. `IsChatDialogWindowVisible()` does not mean "the player opened chat"

```
34268:	public bool IsChatDialogWindowVisible()
34270:		return m_chatWindow.gameObject.activeSelf;
34179:	public float m_hideDelay = 10f;
34277:		m_chatWindow.gameObject.SetActive(m_hideTimer < m_hideDelay);
34392:					m_hideTimer = 0f;              // reset on EVERY incoming message
```

It means "a chat message arrived in the last ten seconds". Gating a UI panel, a cursor unlock, or an input
capture on it means firing for ten seconds every time *anybody* says anything, including system and death
messages.

The predicate you actually want is vanilla's own:

```
34259:	public bool HasFocus()
34261:		if (m_chatWindow != null && m_chatWindow.gameObject.activeInHierarchy)
34263:			return m_input.isFocused;
```

⚠ If your own mod postfixes `Chat.HasFocus` (the standard trick for making the game treat an IMGUI text
field as chat focus — see `SharedInfrastructure.md`, `SharedUI/UIFocus.cs`), **do not call `HasFocus()` to
answer this question** — you will read your own patch back and latch on. Replicate the two field reads
directly instead. `m_input` is `GuiInputField` (`35695`), which lives in **`gui_framework.dll`**, not
`assembly_valheim` — that reference has to be added explicitly.

Related field locations (all on `Terminal`, inherited by `Chat`):

```
35691:	public RectTransform m_chatWindow;
35693:	public TextMeshProUGUI m_output;      // usable as TMP_Text for FindIntersectingLink
35695:	public GuiInputField m_input;
35703:	protected List<string> m_chatBuffer;
35705:	protected const int m_maxBufferLength = 300;
37941:	private void UpdateChat()             // rebuilds m_output.text from m_chatBuffer
```

---

## 6. Forced case: which methods, which overloads — and the one that is dead

`Talker.Type.Shout` is upper-cased and `Whisper` is lower-cased, unconditionally, in three method bodies:

```
37834 / 37838   Terminal.AddString(PlatformUserID, string, Talker.Type, bool)
37886 / 37890   Terminal.AddString(string title, string, Talker.Type, bool)
34481 / 34485   Chat.AddInworldText(GameObject, long, Vector3, Talker.Type, UserInfo, string)
```

These are **local-variable reassignments** (`text = text.ToUpper()`), so no prefix or postfix can intercept
them. A transpiler that drops just the call instruction is stack-neutral — a 0-arg `string`→`string`
`callvirt` pops one and pushes one, so deleting it leaves `text = text`:

```csharp
if (instruction.Calls(ToUpperMethod) || instruction.Calls(ToLowerInvariantMethod)) continue;
```

Two details worth verifying rather than assuming, because both fail *silently* — **IL-verified** here:

- vanilla calls the **parameterless** `ToUpper()` / `ToLowerInvariant()`, not the `CultureInfo` overloads.
  `AccessTools.Method(typeof(string), "ToUpper", Type.EmptyTypes)` matches. Had it been the culture
  overload, `Calls()` would never match and the transpiler would be a no-op that still reports success.
- those are the **only** `ToUpper`/`ToLower` calls in any of the three bodies, so a blanket strip takes no
  collateral — nothing on a username, command keyword, or comparison key. (The `PlatformUserID` overload
  also contains 11 `String::Format`/`Concat`/`get_Length` calls; all are left alone.)

⚠ **`Terminal.AddString(string title, …)` has zero call sites anywhere in vanilla.** Chat uses the
`PlatformUserID` overload (`34393`, IL-confirmed). Patch the `title` overload only if you care about other
mods that call it directly.

`Talker.Type` for reference: `Whisper, Normal, Shout, Ping` (`26846`) — so `(int)Shout == 2`, which is the
literal `2` at `34573`.

---

## 7. Recipe: your own chat-shaped RPC without losing vanilla's guarantees

If you need data to arrive **atomically with** a chat line, do not send two RPCs — a routed broadcast and
a ZDO-targeted `Say` take different routes through a dedicated server and cannot be ordered. Send one, and
reuse vanilla's own pipeline on both ends:

```csharp
// SEND — same per-recipient permission gate vanilla uses (34582). Invokes the handler synchronously for
// the local user first (34593), then per peer as each relations check completes, skipping anyone who has
// you blocked. GetChatMessageData (34630) applies CensorShittyWords when that peer requires filtering.
Chat.CheckPermissionsAndSendChatMessageRPCsAsync(delegate (long user, bool filterText)
{
    Chat.GetChatMessageData(text, filterText, out UserInfo userInfo, out string textToSend);
    ZRoutedRpc.instance.InvokeRoutedRPC(user, "MyMod_ChatThing", pos, channel, userInfo, textToSend, payload);
});

// RECEIVE — do your side effects, THEN hand off. Cache-before-display is guaranteed, in one call stack.
private static void RPC(long sender, Vector3 pos, int type, UserInfo user, string text, ZPackage payload)
{
    ApplyPayload(sender, payload);
    if (!InRange(pos, (Talker.Type)type)) return;          // Shout is global; Normal/Whisper are not (§3)
    Chat.instance.OnNewChatMessage(null, sender, pos, (Talker.Type)type, user, text);
}
```

Both helpers are `public static` on `Chat`. This keeps platform mute/block enforcement, profanity
filtering, the `<`/`>` strip, the chat buffer, world text and the hide timer all behaving exactly as
vanilla — you replace only the transport.

**Trade-off to state out loud:** peers without your mod have no handler for the RPC and see *nothing*.
Fine for a mod-exclusive message type, wrong for anything that should degrade gracefully.

**Free anti-spoofing:** mint ids as `$"{senderUserId:x}-{counter:x}"`. The id then carries its own origin,
so both the payload handler (does this id belong to the peer that sent it?) and the display path (does it
belong to the sender of *this message*?) can validate with a string parse — no lookup, no cache hit, no
ordering assumption. `ZNet.instance.LocalPlayerCharacterID` is `m_characterID` (`66879`), a `ZDOID` whose
`.UserID` (`64421`) is the same `long` space `senderID` uses — vanilla compares them itself at `34366`.

⚠ `LocalPlayerCharacterID` is `ZDOID.None` (UserID **0**) until the local player has spawned. Minting ids
before then collides across every player on the server.

---

## 8. Sending item *stats* — what is safe to reconstruct on the receiver

Per [04-ITEMDROP-SHAREDDATA.md](04-ITEMDROP-SHAREDDATA.md), nothing in `SharedData` is ever networked by
vanilla; items cross the wire as a prefab name. Keep that shape — send the prefab name plus the instance
fields that vary per copy, and rebuild locally:

```csharp
ItemDrop.ItemData clone = itemDrop.m_itemData.Clone();   // shallow: 58178-58183
clone.m_quality  = Mathf.Max(1, quality);
clone.m_variant  = Mathf.Clamp(variant, 0, clone.m_shared.m_icons.Length - 1);
clone.m_worldLevel = Game.m_worldLevel;                  // ← see below
```

The receiver then gets tooltips and icons that reflect *its own* mod list, with no duplicated stat maths.

### ⚠ `m_worldLevel` is 0 on prefab `ItemData`, and `GetTooltip` scales by it

```
58148:			public int m_worldLevel = Game.m_worldLevel;
58391:		public string GetTooltip(int stackOverride = -1)
58393:			return GetTooltip(this, m_quality, crafting: false, m_worldLevel, stackOverride);
```

That field initializer **does not run for Unity-deserialized prefab data** — which is exactly why vanilla
assigns it explicitly at every real item-creation site (`11427`, `56664`, `56836`, `57630`). Clone a prefab's
`m_itemData` and you inherit `m_worldLevel == 0`, so every tooltip you render off it silently reports
world-level-0 numbers. One line to fix, invisible if you don't know to look.

### ⚠ `GetIcon()` indexes an array with no bounds check

```
58396:		public Sprite GetIcon()
58398:			return m_shared.m_icons[m_variant];
```

`m_variant` off the wire is attacker-controlled. Clamp it. Likewise `ObjectDB.GetItemPrefab` dereferences
its argument immediately (`90023`–`90025`), so a null prefab name throws rather than returning null.

`Clone()` is `MemberwiseClone()` plus a fresh `m_customData` dictionary (`58178`–`58183`) — `m_shared`
stays a **live reference to the prefab's** `SharedData`, which is what makes other mods' `SharedData` edits
and `GetTooltip()` postfixes show up automatically. Never mutate `clone.m_shared`; you are editing the
prefab for the whole game.

`m_dropPrefab` **is** populated for save-loaded inventory items (`ItemDrop.Awake`, `58848`–`58850`) and
points at the ObjectDB prefab rather than a scene instance, so `.name` carries no `(Clone)` suffix and
round-trips cleanly as a wire identifier.

### Drawing the icon in IMGUI

`Sprite.texture` is the whole atlas **page**, not the sprite's region of it — Valheim item icons are
atlased, so `GUI.DrawTexture(r, sprite.texture)` renders every icon in the atlas squashed into your rect:

```csharp
Rect tr = sprite.textureRect;
GUI.DrawTextureWithTexCoords(r, sprite.texture,
    new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
```

---

## NOT FOUND / does not exist

- **`ZRoutedRpc.instance.GetServerPeerID()`** — no such member. For "who am I", use
  `ZNet.instance.LocalPlayerCharacterID.UserID` (`66879` → `64421`), which is what vanilla compares
  `senderID` against at `34366`.
- **Any `Unregister` on `ZRoutedRpc`** — `m_functions` is add-only (`71041`–`71057`). Handlers die with the
  instance, which is also why re-registering per session does not leak.
- **A `Chat` API for inserting pre-formatted markup** — there is none; everything goes through
  `OnNewChatMessage`'s strip (§4) or bypasses `Chat` entirely via `Terminal.AddString`.
- **A distance constant for shouts that vanilla actually honours** — `Talker.m_shoutDistance` (`26858`)
  is only consulted on the `Say` path, which real shouts do not take (§3).
- **A "chat is focused" property distinct from `HasFocus()`** — `m_focused` (`35689`) is a `Terminal`
  protected field reset at the top of `Chat.Update` (`34275`); it is not a substitute.

## Could not verify from source

- **Canvas render mode of the chat root**, which decides whether `TMP_TextUtilities.FindIntersectingLink`
  should be passed `null` or a camera. Render mode is Unity scene/prefab data, present in neither the
  decompile nor assembly metadata — needs a runtime check.
