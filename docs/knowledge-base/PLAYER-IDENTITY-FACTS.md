# Player Identity Facts — which number is a player, and which one only looks like one

**Applies to every project in `WubarrkCODING`.** Written 2026-08-17 after TortalPortal 1.4.0 shipped a
per-player portal report that knew every count and not one name. Everything below was read out of
`DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs` (line numbers are from that file) and
cross-checked against `assembly_valheim_SERVER.decompiled.cs` where the two differ.

Valheim has **four** different longs floating around that all read like "the player". Three of them are
not. Picking the wrong one compiles clean, runs clean, and produces a file full of IDs that match
nothing.

---

## 1. The four numbers

| Number | Where it lives | What it actually is | Stable? |
|---|---|---|---|
| `PlayerProfile.GetPlayerID()` | `m_playerID`, in the character save | **The player identity.** `Utils.GenerateUID()` once, at profile construction, then persisted | Forever, across worlds and servers |
| `Player.GetPlayerID()` | `ZDOVars.s_playerID` on the character ZDO | The same number, copied into the world so anyone can read it | Forever |
| `ZNetPeer.m_uid` | The network layer | A **connection**. Fine as a session key, meaningless on disk | This session only |
| `ZDOID.UserID` | Any ZDO's id | **`ZDOMan.m_sessionID`** — see the trap below | This session only |

**The one to write down is `s_playerID`.** It is the number a piece records as its creator, so it is the
only one that joins world data to a person.

## 2. THE TRAP — `ZDOID.UserID` is not a user

```csharp
// WRONG. Compiles, runs, returns a large plausible long that matches nothing.
long playerID = peer.m_characterID.UserID;
```

`ZDOID.UserID` (64421) resolves a key into a table of *ZDO owners*, and a ZDO's owner is the
**ZDOMan session**, not the human:

```csharp
private readonly long m_sessionID = Utils.GenerateUID();          // 64619 — new every boot
ZDOID zDOID = new ZDOID(m_sessionID, m_nextUid++);                // 64933 — every ZDO ever created
```

So `peer.m_characterID.UserID` is "which running game instance minted this object", regenerated on every
launch. It will never equal `s_creator`, `s_playerID`, or anything you can persist. The name is the whole
trap: it says *User*, it means *session*.

**The correct one-hop:**

```csharp
ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
if (zdo != null && zdo.IsValid())
{
    long id   = zdo.GetLong(ZDOVars.s_playerID, 0L);      // the real identity
    string nm = zdo.GetString(ZDOVars.s_playerName, "");  // written beside it, same call
}
```

`Player.SetPlayerID` (15966) writes **both keys together, once**, guarded by `GetPlayerID() == 0` — so a
character ZDO either has both or neither, and neither is ever rewritten.

## 3. Why `s_playerID` is the join key for build data

```csharp
component.SetCreator(GetPlayerID());                       // 18192, in Player.PlacePiece
...
m_nview.GetZDO().Set(ZDOVars.s_creator, uid);              // 117736, Piece.SetCreator
```

`Player.GetPlayerID()` is `s_playerID` (15975). So **`s_creator` on any placed piece == `s_playerID` on
its builder's character ZDO**, exactly, with no conversion. That is the join, and it is the only one.

`Piece.SetCreator` also refuses to overwrite (`GetCreator() == 0` guard), so a creator is permanent.

## 4. Names: the server is never told, except three times by accident

`ZDOVars.s_creatorName` is written in **exactly one place in the entire assembly**:

```csharp
public void Setup(string name) { m_nview.GetZDO().Set(ZDOVars.s_creatorName, name); }   // 118637, PrivateArea
```

and it is called from a null-conditional in `Player.PlacePiece` (18194) that skips silently over every
piece without a `PrivateArea` component. **So a piece's creator name is blank on everything except a
ward** — this is why TortalPortal's "Owner" column was empty on every world from 1.0.0 to 1.4.0, and it
will be blank in your mod too unless you go and get it.

Three vanilla things do leave a **persistent (id, name) pair** in world data, and they are the only
retroactive source of names there is:

| Source | ID key | Name key | Written by |
|---|---|---|---|
| Ward | `s_creator` | `s_creatorName` | `PrivateArea.Setup` (118637) |
| Bed | `s_owner` | `s_ownerName` | `Bed.RPC_SetOwner` (~101049) |
| Tombstone | `s_owner` | `s_ownerName` | `TombStone.Setup` (27770) |

Beds and tombstones share both keys, so **one read covers both** — and anything modded that follows the
same convention comes along free. These outlive the session that made them, which means a long-running
world can name players who have not logged in since your mod was installed. Nothing else can.

**Freshness caveat:** a mark can be years old and carry a name its owner has since changed. Treat world
marks as *gap-fill only* and let a live read from a connected player win.

## 5. Placeholder names that will poison a register

```csharp
m_playerName = "Stranger";                 // 91049 — PlayerProfile ctor
m_playerID   = Utils.GenerateUID();        // 91050 — and a brand new random ID with it
```

```csharp
return m_nview.GetZDO().GetString(ZDOVars.s_playerName, "...");   // 15990 — Player.GetPlayerName default
```

**A dedicated server holds exactly such a profile**: constructed, never loaded, nobody playing it.
`Game.instance.GetPlayerProfile()` on a headless box therefore returns **`("Stranger", <random long>)`** —
an ID generated at that server's boot, belonging to no character in any world. Any code shaped like

```csharp
if (ZNet.instance.IsServer()) Record(profile.GetPlayerID(), profile.GetName());   // listen-host shortcut
```

writes that garbage pair on every dedicated server. Guard with `IsDedicated()` **and** reject the literal
names `"Stranger"` and `"..."` at the point of record. A placeholder written down is worse than a null,
because it reads as an answer.

## 6. `ZNet.IsDedicated()` cannot be tested from the client decompile

```csharp
public bool IsDedicated() { return false; }   // 68610, assembly_valheim.decompiled.cs
public bool IsDedicated() { return true;  }   // 68414, assembly_valheim_SERVER.decompiled.cs
```

It is a **compile-time constant, different per assembly**. Reading the client decompile to reason about a
dedicated-server branch will tell you the branch is dead. It is not. Always check the SERVER decompile
before concluding anything about headless behaviour.

## 7. `ZNetPeer`, in full (66595)

```csharp
public long   m_uid;              // connection id — session only
public ZDOID  m_characterID;      // the character's ZDO id; ZDOID.None until RPC_CharacterID arrives (68459)
public string m_playerName;       // whatever the client said at handshake — a fallback, not a source of truth
```

`m_characterID` arrives **after** the peer is otherwise ready, so resolving it needs a retry or a poll, not
a one-shot on connect. `IsReady()` is `m_uid != 0` and says nothing about the character.

## 8. Enumerating ZDOs when you need to sweep the world

```csharp
List<ZDO>[] bySector                       = ZDOMan.instance.m_objectsBySector;          // fixed-size array
Dictionary<Vector2i, List<ZDO>> byOutside  = ZDOMan.instance.m_objectsByOutsideSector;
```

Both need the publicizer. The array is allocated once and **never resized**, so holding references to its
lists across frames is safe as long as every read re-checks `list.Count` — walk by index, never with a
`foreach` enumerator you keep. A chunked walk (a few thousand ZDOs, `yield return null`, repeat) crosses a
large world in a few seconds with no visible stutter.

For a targeted scan there is a public API that chunks itself:

```csharp
int index = 0;
while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("guard_stone", list, ref index)) { }   // 65675
```

`ZDOMan.GetPortals()` is a maintained live list and costs nothing — prefer a maintained list over a sweep
whenever one exists.

---

## Checklist before you persist a player identity

1. Is the number `s_playerID` / `PlayerProfile.GetPlayerID()`? If it came from a `ZDOID`, it is wrong.
2. Does the write path run on a dedicated server with `Game.instance.GetPlayerProfile()`? Guard it.
3. Are `"Stranger"` and `"..."` rejected?
4. Does a failed load leave the map empty and the next write truncate the file? Refuse writes after a bad
   read.
5. Is the client reading a **ServerSync'd** config value early in the session to decide whether to send
   something? That is a race it usually loses — decide on the server, or send unconditionally and filter
   on arrival.
