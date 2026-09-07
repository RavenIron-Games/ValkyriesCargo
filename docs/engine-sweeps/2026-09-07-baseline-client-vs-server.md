# Sweep: the baseline client against the baseline dedicated server

**2026-09-07.** Both builds are 0.221.12 / net 36 / player 43 / world 37
(`docs/ENGINE-BASELINE.md`). This is the sweep the design has been leaning on without ever having
been run: `docs/DESIGN.md` §3.2 and `CLAUDE.md` assert **exactly one behavioural difference between
the two builds in anything this mod touches**, and the whole authored-ZDO design rests on it.

```
node tools/diff-engine.js --surface docs/ENGINE-SURFACE.md ^
     --from C:\Users\donfr\valheim-shadows\src\baseline-client ^
     --to   C:\Users\donfr\valheim-shadows\src\baseline-server ^
     --out  %TEMP%\client-vs-server.md --all-types
```

```
surface 244: 238 unchanged, 6 body changed, 0 signature changed, 0 gone, 0 manifest problem(s)
types: 40 of 697 differ, 0 only in the client, 2 only in the server
```

**Point `--out` somewhere else, not at this file.** The tool's report is generated; this one is that
report plus the reading of it, which is the part that took the time. Two runs of the tool are
byte-identical, so a re-run and a diff against the numbers above is the check worth doing.

---

## The verdict on the one-difference assertion

**The pin is real, verbatim, and it is the only difference that changes what the server DOES with
our objects. The word "one" is wrong: six surface members differ. The other five are benign, and
all five are written down below rather than left to be rediscovered.**

Nothing in the flight, the event, the ZDO authoring, the zone maths, the market or the wire moved.
`ZDO`, `ZDOID`, `ZDOVars`, `ZNetView`, `ZNetScene`, `ZoneSystem`, `ZSyncTransform`, `ZSyncAnimation`,
`RandEventSystem`, `RandomEvent`, `Valkyrie`, `Character`, `Humanoid`, `Player`, `SEMan`,
`SE_Rested`, `ObjectDB`, `ItemDrop`, `MessageHud`, `Odin`, `World`, `ZRpc`, `ZRoutedRpc`, `ZPackage`,
`ZNetPeer`, `Version` — **none of their files differs at all between the two builds.** That is the
result the design wanted and it is stronger than "we found one": those files are byte-identical
after whitespace normalisation, so there is nothing hiding in them.

---

## 1. `Game.FixedUpdate` — THE difference. Confirmed.

```diff
--- baseline-client/assembly_valheim/Game.cs
+++ baseline-server/assembly_valheim/Game.cs
@@ -7,19 +7,5 @@
 			Logout();
 			ZLog.LogError("World load failed, exiting without save. Check backups!");
 		}
-		if (!m_haveSpawned && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
-		{
-			m_haveSpawned = true;
-			RequestRespawn(0f);
-		}
-		ZInput.FixedUpdate(Time.fixedDeltaTime);
-		if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connecting && ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
-		{
-			ZLog.Log("Lost connection to server:" + ZNet.GetConnectionStatus());
-			Logout();
-		}
-		else
-		{
-			UpdateRespawn(Time.fixedDeltaTime);
-		}
+		ZNet.instance.SetReferencePosition(new Vector3(1000000f, 0f, 1000000f));
 	}
```

`Server/Spawner.cs`'s class comment says the server build's `Game.FixedUpdate` "ends with exactly
that line, and the client build has no such line at all". **Both halves are true.** The server pins
its reference position to a million every fixed frame; the client instead spends the same method on
respawn, `ZInput` and the connection watchdog, and never calls `SetReferencePosition` here at all.

Everything Spawner reasons from this holds:

- the server's `ZNetScene` active area never covers a real world position, so it instantiates
  **nothing** of ours — not the bird, not the merchant;
- the "destroy a ZDO whose prefab will not resolve" branch in `ZNetScene.CreateObjectsSorted` walks
  the server's own sector list, out at a million, and can never reach us;
- every line of `Client/CargoFlight.cs` runs on a player's machine.

The client's half of the diff is worth one line of its own: the client's `FixedUpdate` is where
`Logout()` happens on a lost connection. That is not a difference this mod touches, but it is why
`CargoTick.EndSession` flushes the sidecar on `ZNet` going away rather than trusting a shutdown hook.

---

## 2. `ZNet.IsDedicated` — the difference the mod already refuses to use

```diff
 	public bool IsDedicated()
 	{
-		return false;
+		return true;
 	}
```

The client build's `IsDedicated()` is a **hardcoded `return false`**, not a runtime test. This is
exactly what `ValkyriesCargo.cs` records — the mod compiles against the client's reference assembly,
so the JIT would happily constant-fold a `false` that is wrong on a server — and it is why
`HasRenderer` is `SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null` instead. **The fact
stands and the workaround is right.** (`ZNet.IsCurrentServerDedicated` flips the same way; the mod
names neither.)

Independent support for the same choice turned up in `FejdStartup.Awake`, which is not in the
surface: the **server build quits on startup** if `SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null`
("Server can only run in headless moed" — Iron Gate's typo). The renderer test and the role are the
same question, in the engine's own opinion.

---

## 3. `Terminal.AddString(string)` — the server logs every console line. NOT recorded anywhere.

```diff
 	public void AddString(string text)
 	{
+		ZLog.Log("Console: " + text);
 		while (m_maxVisibleBufferLength > 1)
```

**This is new information and it is worth keeping.** Every line the `cargo` console prints on a
dedicated server also lands in `BepInEx\LogOutput.log`, prefixed `Console:`. On a client it does not.

Two consequences:

- **For `docs/PROOF-CLIENT.md`**: on a dedicated server, `cargo status` and `cargo prefab <name>`
  can be read straight out of the log. Every headless verification so far has been reading lines the
  mod logged deliberately; this says the console verbs are readable there too, which makes several
  items in "What to verify in-game" cheaper than they look.
- **For P11b's trust boundary**: an admin's console output on a dedicated server is written to a
  file. Nothing the `cargo` verbs print is secret, but the write-up should say so rather than
  assume console output is ephemeral.

(The other hunk in `Terminal.cs` is in the four-argument `AddString(PlatformUserID, ...)` chat
overload, whose Xbox display-name block the server build does not carry. The mod never calls it.
Both overloads fold into the one `Terminal.AddString` surface row, which is why this shows up at all.)

---

## 4. `GameCamera.UpdateMouseCapture` — an empty body on the server, still patched

```diff
 	public void UpdateMouseCapture()
 	{
-		if (ZInput.GetKey(KeyCode.LeftControl) && ZInput.GetKeyDown(KeyCode.F1))
-		...
-		else if (!Menu.IsVisible() || UnifiedPopup.IsVisible())
-		{
-			Cursor.lockState = CursorLockMode.None;
-			Cursor.visible = ZInput.IsMouseActive();
-		}
 	}
```

The server build keeps the method and empties it. `Libs/SharedUI/UIFocus.cs` — **not ours** — puts a
prefix and a `Priority.First` postfix on it. The sweep confirms:

- **the patch still applies on a dedicated server**, because the method exists. That is the two
  patched methods `UIFocus` contributes to the boot line's `patches=13` on StormTest
  (`Chat.HasFocus` and this one), and it means the count is the same on a server as on a client;
- **it is inert there**: `WantsCursor` is only ever true while the Cargo Terminal is open, and the
  terminal only exists where `HasRenderer`. The prefix returns `true` and the postfix returns early.

Nothing to change. Recorded because "a patch on a method whose body is empty on one of the two
builds" is exactly the shape of thing that reads as a bug later.

---

## 5. `ZNet.Awake` — a ServerSync postfix target, three lines shorter on the server

```diff
-		string personaName = SteamFriends.GetPersonaName();
-		ZLog.Log("Steam initialized, persona:" + personaName);
 		ZSteamMatchmaking.Initialize();
 		ZPlayFabMatchmaking.Initialize(m_isServer);
-		m_backupCount = PlatformPrefs.GetInt("AutoBackups", m_backupCount);
-		m_backupShort = PlatformPrefs.GetInt("AutoBackups_short", m_backupShort);
-		m_backupLong = PlatformPrefs.GetInt("AutoBackups_long", m_backupLong);
```

Steam persona logging and the three auto-backup preferences. `ServerSync`'s `RegisterRPCPatch` is a
**postfix**, which runs after the whole body on either build, so nothing about its insertion point
moves. Benign. Recorded because `ZNet.Awake` is where ServerSync's RPC registration happens, and the
line `Registered 'com.raveniron.valkyriescargo ConfigSync' RPC` in every boot log comes from it.

---

## 6. `ZNet.RPC_PeerInfo` — a ServerSync prefix target, one refusal block shorter on the server

```diff
-				if (PlatformManager.DistributionPlatform.PrivilegeProvider.CheckPrivilege(Privilege.CrossPlatformMultiplayer) != PrivilegeResult.Granted && PlatformManager.DistributionPlatform.Platform != platformUserId.m_platform)
-				{
-					rpc.Invoke("Error", 10);
-					ZLog.Log("Peer diconnected due to server platform privileges disallowing crossplay. ...");
-					return;
-				}
 				PlayFabManager.CheckIfUserAuthenticated(...)
```

The dedicated server does not refuse a cross-platform peer on privilege grounds; the client build
does. `ServerSync` carries **two** prefixes on this method (the buffering socket, and
`VersionCheck`'s), and a prefix runs before the body on either build, so neither moves. Benign.

Recorded loudly anyway, because `RPC_PeerInfo` is the most heavily patched method in this mod's
whole dependency chain and it is where the version wall lives (`CLAUDE.md` "What to verify in-game"
item 3). A future change *inside* it is the one that would matter.

---

## Every type whose file differs (40 of 697)

`--all-types`. Read in full, and grouped by what reading them showed. **Two files exist only in the
server build** — `PlayFabAuthWithCustomID` and the compiler's `<PrivateImplementationDetails>` — and
neither is named anywhere in this mod.

### Declares a surface member that CHANGED (4 files, the six members above)

`Game.cs` · `GameCamera.cs` · `Terminal.cs` · `ZNet.cs`

`ZNet.cs` has ten differing hunks and only three are surface members. The other seven are
`OpenServer` (an indentation-only change around `ZSteamMatchmaking.RegisterServer`), `SaveWorld`
(one auto-backup preference), `LoadWorld` and `CheckDataVersion` (the server calls
`Application.Quit()` on a failed world load where the client shows a dialog),
`IsCurrentServerDedicated` (`false` → `true`, the twin of `IsDedicated`), `UpdatePlayerHistory` (the
client filters itself out and reports recent players to the platform; the server does neither) and
`GetNetStats` (the server averages over its peers; the client reads its one connection). **None is
in the surface, and none is reachable from anything this mod calls.**

### Declares a surface member that did NOT change (5 files)

| file | what differs | why it does not matter |
|---|---|---|
| `assembly_guiutils/Localization.cs` | `SetStartupLanguage`: the server does not fall back to the OS locale | `Localization.instance` and `Localization.Localize` are byte-identical. The terminal's item names come out the same. |
| `assembly_utils/FileHelpers.cs` | `CloudStorageSupported` is a hardcoded `false` on the server | `FileHelpers.FileSource` is identical, and `MarketStore` asks for `Local` explicitly for exactly this class of reason. |
| `assembly_valheim/FejdStartup.cs` | `Awake`, `Start`, `InitializeSteam`: the server parses command-line arguments and refuses a non-null graphics device | `ShowConnectError`, ServerSync's patch target, is identical. |
| `assembly_valheim/Inventory.cs` | `CountItemsByName`, `CountItemsByType`, `GetAllItemsOfType` | Rendering only: `names.Contains(x)` versus `Enumerable.Contains(names, x)` is the same call. `AddItem`, `CanAddItem`, `RemoveItem` and `CountItems` — everything `DealApplier` touches — are identical. |
| `assembly_valheim/ZDOMan.cs` | `Load`: one `ZLog.Log` written as `string.Concat` of six parts on the client and as `+` on the server | Rendering only. `CreateNewZDO`, `GetZDO`, `DestroyZDO`, `GetSessionID` and `RemoveOrphanNonPersistentZDOS` are identical. |

### Real platform differences, in types this mod never names (21 files)

The Steam/PlayFab/Xbox split, and the client-only rendering and UI stack — most of these are whole
method bodies that the server build simply does not carry:

`CensorShittyWords` (the whole UGC filter is gone) · `DLCMan` (no `CheckDLCsSTEAM`) · `DepthCamera` ·
`FrameBufferScaler` · `GraphicsSettingsManager` · `LoadingIndicator` · `PlatformInitializer` ·
`PlayFabManager` (custom-ID login instead of Steam) · `PresentManager` · `RelationsManager` ·
`ServerListEntryData` (a longer `Equals`) · `ServerListGui` · `SteamManager` · `TabHandler` ·
`UIGamePad` (empty bodies where the client reads the gamepad) · `UpscaledFrameBuffer` ·
`ZPlayFabMatchmaking` · `ZSteamMatchmaking` · `ZSteamSocket` and `ZSteamSocketOLD` (the server uses
`SteamGameServerNetworkingSockets` and `Callback.CreateGameServer` where the client uses the client
equivalents) · `assembly_utils/PlatformPrefs` (no Steam Deck key prefix, no preferences provider).

None of these is named in `ValkyriesCargo/`, and none is reachable from a member that is. The
socket split is worth one sentence: `ZNetPeer.m_socket` and `ISocket.GetHostName` — which the owed
ledger and `AdminGate` both key on — are **identical**; what differs is which concrete socket class
the engine constructs, and `GetHostName` is the interface either way.

### Decompiler rendering, same behaviour (10 files)

Extension-method syntax, a boxing temporary the decompiler kept, a switch expression written out as
a switch statement, or a static constructor lowered instead of inlined. Nothing behavioural:

`assembly_utils/NetworkingUtils/IPv4Address` · `assembly_valheim/ServerJoinDataDedicated` ·
`ServerJoinDataUtils` (`address.AsSpan(..)` versus `MemoryExtensions.AsSpan(address, ..)`) ·
`ServerListElement` · `TriggerSpawner` · `Valheim.UI/RadialBase` · `Valheim.UI/RadialDataSO` ·
`WearNTear` · `ZDOHelper` (its static constructor is written out on the client and folded into the
field initialisers on the server) · `UnitySourceGeneratedAssemblyMonoScriptTypes_v1` (Unity's
generated type table; 2951 differing lines and no code at all).

---

## What to change in the documents

1. **`docs/DESIGN.md` §3.2 and `Server/Spawner.cs`'s class comment**: the reference-position pin is
   confirmed word for word and every consequence drawn from it holds. The sentence "It is the one
   real behavioural difference between the two builds in anything this mod touches" wants softening
   to *the one that matters* — five more surface members differ, and this file names them.
2. **`CLAUDE.md` "Engine facts"**: the flight bullet's "A dedicated server pins its reference
   position to (1000000, 0, 1000000) every fixed frame and instantiates nothing of ours" is correct
   as written. Nothing to fix.
3. **Worth adding somewhere**: `Terminal.AddString` logs on a dedicated server and not on a client.
   That is a fact about how every headless proof is read.
4. **`ZNet.IsDedicated` is a hardcoded `false` in the client's assembly.** Already recorded in
   `ValkyriesCargo.cs`; now confirmed against both real builds rather than inferred.
