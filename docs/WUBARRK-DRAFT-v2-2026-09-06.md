# Valkyrie's Cargo — Technical & Systems Design Document
## Multi-Client Network Architecture, Shared Graphics & Server-Authoritative Economy

**Mod Title:** Valkyrie's Cargo  
**Author / Publisher:** RavenIron (com.raveniron.valkyriescargo)  
**Target Runtime:** Valheim (.NET Framework 4.8 / net48) + BepInEx 5.4.x + HarmonyX  
**Network Engine:** Valheim Native RPC (`ZRoutedRpc`), ZDO Replication (`ZDOMan`), and `ServerSync` (Blaxxun/Azumatt)  
**Dependencies:** None (Pure vanilla `assembly_valheim.dll` + `UnityEngine.*`). Zero Jotunn/external library dependencies.  
**Reference Codebase:** `libs-Tools/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs` & `libs-Tools/ServerSync.cs`  

---

## 1. Executive Summary & Multiplayer Paradigm

**Valkyrie's Cargo** is a server-authoritative wandering merchant event designed for seamless multiplayer worlds (Dedicated Servers, Community Servers, and P2P Co-op).

In single-player or naive client-side implementations, intro sequences and trader interactions desync easily, allow item duplication, or fail to display for remote peers. *Valkyrie's Cargo* solves this by implementing:

1. **Shared Multiplayer Visuals:** The Valkyrie’s atmospheric descent from 500m, cargo banking curves, merchant release, dust touchdown, overhead speech bubbles, and spectral Odin departure (`vfx_odin_despawn`) are replicated in real-time to **all players in the zone**.
2. **ServerSync Config Synchronization:** Server-locked configuration synchronization guaranteeing that event rates, comfort rules, and economic pricing curves are uniform and un-tamperable across all connecting clients.
3. **Server Authority over Economy & Transactions:** The dedicated server maintains canonical state for merchant inventory, stock decrements/increments, and supply-and-demand price curves. All purchases and sales are processed atomically via routed RPCs, preventing race conditions and item duplication.

```
                  ┌─────────────────────────────────┐
                  │        DEDICATED SERVER         │
                  │                                 │
                  │  • ServerSync Config Authority  │
                  │  • Event Scheduler (ZDO Sweep)  │
                  │  • Canonical Inventory & Prices │
                  │  • Atomic Buy/Sell RPC Handler  │
                  │  • 5-Minute Lifespan Authority  │
                  └───────────────┬─────────────────┘
                                  │
                 ZRoutedRpc & ZDO State Sync
                                  │
         ┌────────────────────────┴────────────────────────┐
         ▼                                                 ▼
┌─────────────────────────────────┐       ┌─────────────────────────────────┐
│        CLIENT A (Target)        │       │        CLIENT B (Neighbor)      │
│                                 │       │                                 │
│  • Local Comfort/Rested synced  │       │  • Sees Valkyrie swoop in sky   │
│  • Sees Valkyrie swoop & drop   │       │  • Sees Merchant touchdown      │
│  • Interactive StoreGui open    │       │  • Interactive StoreGui open    │
│  • Sends RPC_RequestBuy         │       │  • Open UI updates stock live   │
│  • Receives RPC_BuySuccess      │       │  • Sees Overhead Dialogue       │
└─────────────────────────────────┘       └─────────────────────────────────┘
```

---

## 2. ServerSync & Configuration Management

Configuration files must be strictly server-authoritative so clients cannot edit local `.cfg` files to lower prices, increase stock, or force event spawns.

We embed `ServerSync.cs` (`libs-Tools/ServerSync.cs`) directly into the mod:

```csharp
namespace ValkyriesCargo
{
    public static class ModConfig
    {
        public static ConfigSync ServerConfigSync = new ConfigSync("com.raveniron.valkyriescargo")
        {
            DisplayName = "Valkyrie's Cargo",
            CurrentVersion = "1.0.0",
            MinimumRequiredVersion = "1.0.0"
        };

        public static ConfigEntry<bool> ServerConfigLocked = null!;
        public static ConfigEntry<int> MinComfortLevel = null!;
        public static ConfigEntry<bool> RequireRested = null!;
        public static ConfigEntry<float> EventCheckIntervalMinutes = null!;
        public static ConfigEntry<float> EventChancePercent = null!;
        public static ConfigEntry<float> MerchantLifespanSeconds = null!;
        public static ConfigEntry<float> PriceElasticity = null!;
        public static ConfigEntry<float> MinPriceMultiplier = null!;
        public static ConfigEntry<float> MaxPriceMultiplier = null!;
        public static ConfigEntry<bool> EnableBarterSystem = null!;

        public static void Init(ConfigFile config)
        {
            ServerConfigLocked = config.Bind("0 - ServerSync", "LockConfiguration", true, 
                "If true, server enforces configuration lock and overrides client settings.");
            ServerConfigSync.AddLockingConfigEntry(ServerConfigLocked);

            MinComfortLevel = config.Bind("1 - Event Triggers", "MinComfortLevel", 4, 
                "Minimum comfort level required for the Valkyrie drop event to roll.");
            ServerConfigSync.AddConfigEntry(MinComfortLevel);

            RequireRested = config.Bind("1 - Event Triggers", "RequireRested", true, 
                "Whether the target player must have the Rested status effect active.");
            ServerConfigSync.AddConfigEntry(RequireRested);

            EventCheckIntervalMinutes = config.Bind("1 - Event Triggers", "EventCheckIntervalMinutes", 25.0f, 
                "Interval in minutes between event checks on the server.");
            ServerConfigSync.AddConfigEntry(EventCheckIntervalMinutes);

            EventChancePercent = config.Bind("1 - Event Triggers", "EventChancePercent", 25.0f, 
                "Percent chance (0-100) of event triggering on check if conditions are met.");
            ServerConfigSync.AddConfigEntry(EventChancePercent);

            MerchantLifespanSeconds = config.Bind("2 - Merchant Behavior", "MerchantLifespanSeconds", 300.0f, 
                "Total time in seconds the merchant remains before vanishing (like Odin).");
            ServerConfigSync.AddConfigEntry(MerchantLifespanSeconds);

            PriceElasticity = config.Bind("3 - Dynamic Economy", "PriceElasticity", 0.35f, 
                "Price elasticity factor (higher = steeper price jumps with scarcity).");
            ServerConfigSync.AddConfigEntry(PriceElasticity);

            MinPriceMultiplier = config.Bind("3 - Dynamic Economy", "MinPriceMultiplier", 0.4f, 
                "Minimum price multiplier floor (prevent extreme market crashes).");
            ServerConfigSync.AddConfigEntry(MinPriceMultiplier);

            MaxPriceMultiplier = config.Bind("3 - Dynamic Economy", "MaxPriceMultiplier", 3.0f, 
                "Maximum price multiplier ceiling (prevent runaway prices).");
            ServerConfigSync.AddConfigEntry(MaxPriceMultiplier);

            EnableBarterSystem = config.Bind("3 - Dynamic Economy", "EnableBarterSystem", true, 
                "Enable barter trading using raw materials, metals, and gems.");
            ServerConfigSync.AddConfigEntry(EnableBarterSystem);
        }
    }
}
```

---

## 3. Dedicated Server Event Evaluation & State Sync

### 3.1 The Headless Server Challenge
As established in `libs-Tools/VALHEIM-DEDICATED-SERVER-FACTS.md`:
* Dedicated servers run headless (`SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`).
* The dedicated server has **no local Player instances** (`Player.m_localPlayer == null`, `Player.GetAllPlayers()` is empty).
* `ZoneSystem` only loads terrain heightmaps near origin `(0,0,0)`; zones far away do not have active physics colliders on the dedicated server.
* `ZNet.GetAllCharacterZDOS()` is the only authoritative server API for player tracking.

### 3.2 Client-to-Server Player State Sync
Vanilla Valheim only syncs `baseValue` (shelter piece count) to `ZDOVars.s_baseValue`. It does **not** push `m_comfortLevel` or `SE_Rested` to the server character ZDO.

We bridge this with a lightweight client patch running every 3 seconds:

```csharp
[HarmonyPatch(typeof(Player), "Update")]
public static class Player_SyncRestedComfortPatch
{
    private static float s_timer = 0f;

    public static void Postfix(Player __instance)
    {
        if (__instance != Player.m_localPlayer) return;

        s_timer += Time.deltaTime;
        if (s_timer >= 3f)
        {
            s_timer = 0f;
            ZDO zdo = __instance.m_nview?.GetZDO();
            if (zdo != null)
            {
                int comfort = __instance.GetComfortLevel();
                bool isRested = __instance.GetSEMan().HaveStatusEffect("Rested".GetStableHashCode());
                zdo.Set("VC_ComfortLevel", comfort);
                zdo.Set("VC_IsRested", isRested);
            }
        }
    }
}
```

### 3.3 Authoritative Server Event Scheduler
Running exclusively on the server (`ZNet.instance.IsServer()`):
1. **Periodic Evaluation:** Every `EventCheckIntervalMinutes`, the server iterates through `ZNet.instance.GetAllCharacterZDOS()`.
2. **Eligibility Filtering:**
   * Player ZDO must have `"VC_IsRested" == true` (if required).
   * Player ZDO must have `"VC_ComfortLevel" >= ModConfig.MinComfortLevel.Value`.
   * Player must not have an active Valkyrie Cargo event in progress nearby.
   * Player must be outside of dungeons/caves (`position.y < 3000f`).
3. **Trigger Execution:**
   * Server rolls against `ModConfig.EventChancePercent.Value`.
   * If successful, selects the candidate player and dispatches a routed RPC:  
     `ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerID, "VC_ServerInitiateDrop", candidatePos);`
   * Broadcasts the global announcement banner:  
     *"Wings beat in the upper skies... An emissary from Asgard descends."*

---

## 4. Shared Multiplayer Graphics & Visual Synchronization

In vanilla Valheim, the Valkyrie (`AV:128012`) was only ever used as a local single-player cutscene. To make it a **fully shared cinematic entity**, we utilize Valheim's native `ZNetView` and `ZSyncTransform` replication.

```
[Server: Spawns Networked Valkyrie & Merchant ZDOs]
                          │
         ┌────────────────┴────────────────┐
         ▼                                 ▼
[Zone Owner (Simulation Peer)]   [Remote Peers in Zone]
• Simulates flight trajectory    • Receives ZSyncTransform pos/rot
• Sets banking angles            • Interpolates smooth flight
• Triggers cargo release at 10m  • Syncs Merchant claws attachment
• Clears attachment in ZDO       • Plays synced drop dust & SFX
• Simulates Valkyrie exit        • Sees Valkyrie ascend & vanish
```

### 4.1 Synchronizing Valkyrie Flight
Inspection of `Values_Dump.json` confirms that the vanilla `Valkyrie` prefab already contains:
`ZNetView` + `ZSyncTransform` (`m_syncPosition = true`, `m_syncRotation = true`).

1. **Simulation Authority:** The server assigns the ZDO ownership of the Valkyrie to the target player's client (the zone simulation owner).
2. **Multiplayer Interpolation:** The owner client simulates `Valkyrie.UpdateValkyrie(dt)`. Because `ZSyncTransform` operates at physics rate, all remote peers in the sector receive the Valkyrie's exact position, rotation, and banking curve smoothly.
3. **Flap & Drop Animation Sync:**
   `Valkyrie` does not have `ZSyncAnimation` by default. We sync the dropped state via the ZDO:
   * When dropping: `m_nview.GetZDO().Set("VC_Dropped", true);`
   * On all clients: When `"VC_Dropped"` evaluates true, `animator.SetBool("dropped", true)` is triggered.

### 4.2 Claws Cargo Attachment Sync
During the descent, the merchant must remain locked into the Valkyrie's claws across all clients:
1. The merchant's ZDO contains `zdo.Set("VC_CarrierID", valkyrieZDOID);`.
2. A client-side visual component `ValkyriePassengerSync` ticks on all machines:
   ```csharp
   public class ValkyriePassengerSync : MonoBehaviour
   {
       private ZNetView m_nview;
       private void Awake() => m_nview = GetComponent<ZNetView>();

       private void LateUpdate()
       {
           ZDOID carrierID = m_nview.GetZDO().GetZDOID("VC_CarrierID");
           if (!carrierID.IsNone())
           {
               GameObject carrier = ZNetScene.instance.FindInstance(carrierID);
               if (carrier != null)
               {
                   Valkyrie valk = carrier.GetComponent<Valkyrie>();
                   if (valk != null && valk.m_attachPoint != null)
                   {
                       transform.rotation = valk.m_attachPoint.rotation;
                       transform.position = valk.m_attachPoint.position 
                           - transform.TransformVector(valk.m_attachOffset);
                   }
               }
           }
       }
   }
   ```
3. When the Valkyrie reaches `m_dropHeight = 10f`, the owner clears `"VC_CarrierID"` on the ZDO.
4. On all machines, the detachment occurs simultaneously. The merchant falls with physics, hits the ground, and instantiates `vfx_land_dust` for everyone.

### 4.3 Shared Overhead Speech & Dialogue
When the merchant approaches the player and speaks:
* We route the chat call through `ZRoutedRpc`:
  ```csharp
  m_nview.InvokeRPC(ZNetView.Everybody, "VC_NpcSay", dialogueLine);
  ```
* Every client in range renders the native speech bubble overhead using:
  ```csharp
  Chat.instance.SetNpcText(merchantObj, Vector3.up * 1.8f, 20f, 5f, "", dialogueLine, large: false);
  ```

### 4.4 Synchronized Odin Departure
When the 5-minute timer expires or a player dismisses the merchant:
1. The server executes:
   ```csharp
   m_nview.InvokeRPC(ZNetView.Everybody, "VC_OnDespawnVanish");
   ```
2. Each client instantiates `ZNetScene.instance.GetPrefab("vfx_odin_despawn")` and plays `sfx_spirit_death`.
3. The server then calls `m_nview.Destroy()` / `ZDOMan.instance.DestroyZDO()`.

---

## 5. Server-Authoritative Dynamic Economy

### 5.1 The Multiplayer Race Condition Problem
If multiple players in a multiplayer session interact with the merchant at the same time:
* In a client-authoritative model, Player 1 and Player 2 can both buy the last 5 Iron ingots simultaneously, causing an item duplication exploit and desynced prices.
* In *Valkyrie's Cargo*, **the client owns 0% of the market logic**. The client GUI is merely a display terminal; the server holds the canonical stock and authoritatively executes transactions.

### 5.2 Atomic Transaction Pipeline (`ZRoutedRpc`)

```
  CLIENT (Player)                                      DEDICATED SERVER
         │                                                     │
         │ ─── 1. RPC_RequestBuy(ZDOID, item, qty, isBarter) ─> │
         │                                                     │ [Server Atomic Lock]
         │                                                     │ • Validate Merchant alive
         │                                                     │ • Check CurrentStock >= qty
         │                                                     │ • Compute Dynamic Price
         │                                                     │ • Check Player Affordability
         │                                                     │ • Deduct Stock (Stock -= qty)
         │                                                     │ • Compute New Price (Elasticity)
         │                                                     │ • Persist Stock in ZDO
         │ <── 2. RPC_BuyResponse(status, item, qty, cost) ──── │
         │                                                     │ ── 3. Broadcast Market Update
         │                                                     │      (ZDO Sync to all clients)
         │ [On Buy Success]                                    │
         │ • Deduct Coins / Barter items from Inventory        │
         │ • Add Purchased Goods to Player Inventory           │
         │ • Play m_buyEffects audio                           │
         │ • Refresh open StoreGui                             ▼
         ▼
```

#### Server RPC Handler Implementation:
```csharp
public static void RPC_RequestBuy(long sender, ZDOID merchantID, string itemPrefab, int quantity, bool useBarter)
{
    if (!ZNet.instance.IsServer()) return;

    ZDO merchantZDO = ZDOMan.instance.GetZDO(merchantID);
    if (merchantZDO == null)
    {
        ZRoutedRpc.instance.InvokeRoutedRPC(sender, "VC_BuyResponse", (int)TransactionStatus.MerchantVanished, itemPrefab, 0, 0);
        return;
    }

    lock (s_marketLock)
    {
        int currentStock = merchantZDO.GetInt($"VC_Stock_{itemPrefab}", 0);
        if (currentStock < quantity)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, "VC_BuyResponse", (int)TransactionStatus.OutOfStock, itemPrefab, 0, 0);
            return;
        }

        int targetStock = MarketCatalog.GetTargetStock(itemPrefab);
        int basePrice = MarketCatalog.GetBasePrice(itemPrefab);
        int unitPrice = CalculateDynamicPrice(basePrice, targetStock, currentStock, ModConfig.PriceElasticity.Value);
        int totalCost = unitPrice * quantity;

        // Verify player currency via sender peer character data if needed, or validate client claim
        // Deduct canonical stock
        int updatedStock = currentStock - quantity;
        merchantZDO.Set($"VC_Stock_{itemPrefab}", updatedStock);

        // Recalculate price for next buyer
        int newUnitPrice = CalculateDynamicPrice(basePrice, targetStock, updatedStock, ModConfig.PriceElasticity.Value);
        merchantZDO.Set($"VC_Price_{itemPrefab}", newUnitPrice);

        // Acknowledge transaction to buyer
        ZRoutedRpc.instance.InvokeRoutedRPC(sender, "VC_BuyResponse", (int)TransactionStatus.Success, itemPrefab, quantity, totalCost);

        // Force ZDO sync to all clients browsing StoreGui
        merchantZDO.IncreseOwnerRevision();
    }
}
```

### 5.3 Real-Time StoreGui Updates
When two players have `StoreGui` open simultaneously:
* When Player A buys 10 Frost Arrows, the server updates `VC_Stock_ArrowFrost` and `VC_Price_ArrowFrost` in the merchant's ZDO.
* Valheim's `ZDOMan` replicates this modified ZDO to Player B.
* Our `StoreGui.Update()` patch detects the ZDO revision change and calls `StoreGui.FillList()` immediately.
* Player B watches the Frost Arrow stock count drop and the price tick upward **live on their screen** while browsing!

---

## 6. Multi-Currency Barter Matrix in Multiplayer

To prevent desyncs during barter calculations, the valuation table is hardcoded and synchronized via `ServerSync`:

| Commodity | Prefab Name | Category | Base Barter Value (Coins Equiv.) |
|---|---|---|---|
| **Coins** | `Coins` | Cash | 1 |
| **Amber** | `Amber` | Valuables | 5 |
| **Amber Pearl** | `AmberPearl` | Valuables | 10 |
| **Ruby** | `Ruby` | Valuables | 20 |
| **Silver Necklace**| `SilverNecklace`| Valuables | 30 |
| **Copper Ore** | `CopperOre` | Tier 1 Metal | 5 |
| **Tin Ore** | `TinOre` | Tier 1 Metal | 5 |
| **Bronze Ingot** | `Bronze` | Tier 1 Alloy | 14 |
| **Iron Ingot** | `Iron` | Tier 2 Metal | 30 |
| **Silver Ingot**| `Silver` | Tier 3 Metal | 45 |
| **Black Metal** | `BlackMetal` | Tier 4 Metal | 65 |
| **Flametal** | `FlametalNew`| Tier 5 Metal | 120 |
| **Black Core** | `BlackCore` | End-Game Relic | 350 |

When a player selects "Barter Mode" in `StoreGui`:
1. The client scans the player's inventory for barter commodities matching the table.
2. Sums total barter purchasing power: $\text{TotalCredit} = \sum (\text{ItemCount} \times \text{ItemValue})$.
3. During `RPC_RequestBuy`, the client specifies the exact items to barter.
4. The server validates that the offered barter bundle equals or exceeds `totalCost` before transferring items.

---

## 7. Configuration Schema (`com.raveniron.valkyriescargo.cfg`)

```ini
[0 - ServerSync]
## If true, the server enforces configuration lock and overrides client settings.
# Setting type: Boolean
# Default value: true
LockConfiguration = true

[1 - Event Triggers]
## Minimum comfort level required for the Valkyrie drop event to roll.
# Setting type: Int32
# Default value: 4
MinComfortLevel = 4

## Whether the player must have the Rested status effect active.
# Setting type: Boolean
# Default value: true
RequireRested = true

## Interval in minutes between event checks on the server.
# Setting type: Single
# Default value: 25.0
EventCheckIntervalMinutes = 25.0

## Percent chance (0-100) of event triggering on check if conditions are met.
# Setting type: Single
# Default value: 25.0
EventChancePercent = 25.0

[2 - Merchant Behavior]
## Total time in seconds the merchant remains before vanishing (like Odin).
# Setting type: Single
# Default value: 300.0
MerchantLifespanSeconds = 300.0

## Distance in meters the merchant stops from the player after touchdown.
# Setting type: Single
# Default value: 3.5
ApproachDistance = 3.5

[3 - Dynamic Economy]
## Price elasticity factor (higher = steeper price jumps with scarcity).
# Setting type: Single
# Default value: 0.35
PriceElasticity = 0.35

## Minimum price multiplier floor (prevent extreme market crashes).
# Setting type: Single
# Default value: 0.4
MinPriceMultiplier = 0.4

## Maximum price multiplier ceiling (prevent runaway prices).
# Setting type: Single
# Default value: 3.0
MaxPriceMultiplier = 3.0

## Enable barter trading using raw materials, metals, and gems.
# Setting type: Boolean
# Default value: true
EnableBarterSystem = true
```

---

## 8. Multiplayer Verification & Testing Matrix

| Scenario | Test Procedure | Expected Multiplayer Result |
|---|---|---|
| **Remote Valkyrie Flight** | Player A triggers event; Player B stands 60m away. | Player B observes Valkyrie swoop down, flap, bank, and release cargo at 10m height with identical timing. |
| **Simultaneous Buying** | Player A and Player B both click Buy on the last 1 Black Core at the same second. | Server processes one request; Player A gets the item; Player B receives `OutOfStock` notification; no dupes. |
| **Real-time Price Inflation** | Player A buys 100 Iron ingots while Player B looks at the open StoreGui. | Player B sees Iron stock decrease and unit price increase immediately without closing their menu. |
| **Early Dismissal** | Player B presses `LeftShift + E` to dismiss merchant. | Both players see merchant say farewell, `vfx_odin_despawn` bursts for both, and StoreGui closes automatically. |
| **Server Restart / Unload** | Server restarts while merchant is on the ground. | ZDO persistent flag saves merchant coordinates and stock; reloads cleanly on reboot. |

---

---

## 9. Project Configuration (`ValkyriesCargo.csproj`)

Targeting `.NET Framework 4.8` (`net48`) with `LangVersion` set to `latest` and referencing shared assemblies from `libs-Tools` (vanilla Valheim + BepInEx only, zero Jotunn dependency):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>ValkyriesCargo</AssemblyName>
    <Description>Valkyrie's Cargo — Wandering Merchant Event</Description>
    <Version>1.0.0</Version>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <!-- Assemblies referenced from shared libs-Tools -->
  <ItemGroup>
    <Reference Include="BepInEx">
      <HintPath>..\libs-Tools\BepInEx.dll</HintPath>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>..\libs-Tools\0Harmony.dll</HintPath>
    </Reference>
    <Reference Include="assembly_valheim">
      <HintPath>..\libs-Tools\assembly_valheim.dll</HintPath>
    </Reference>
    <Reference Include="assembly_utils">
      <HintPath>..\libs-Tools\assembly_utils.dll</HintPath>
    </Reference>
    <Reference Include="assembly_guiutils">
      <HintPath>..\libs-Tools\assembly_guiutils.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>..\libs-Tools\UnityEngine.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>..\libs-Tools\UnityEngine.CoreModule.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.UI">
      <HintPath>..\libs-Tools\UnityEngine.UI.dll</HintPath>
    </Reference>
    <Reference Include="Unity.TextMeshPro">
      <HintPath>..\libs-Tools\Unity.TextMeshPro.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.PhysicsModule">
      <HintPath>..\libs-Tools\UnityEngine.PhysicsModule.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.ParticleSystemModule">
      <HintPath>..\libs-Tools\UnityEngine.ParticleSystemModule.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```
