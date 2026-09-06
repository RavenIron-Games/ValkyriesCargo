# Valkyrie's Cargo — Technical & Systems Design Document

**Mod Title:** Valkyrie's Cargo  
**Author / Publisher:** RavenIron (com.raveniron.valkyriescargo)  
**Target Runtime:** Valheim (.NET Standard 2.1) + BepInEx 5.4.x + HarmonyX  
**Engine & Dependencies:** Unity 2022.3.x, Pure vanilla assemblies (`assembly_valheim.dll`, `UnityEngine.*`).  
**External Frameworks:** None (Zero dependencies on Jotunn or third-party libraries).  
**Reference Codebase:** `libs-Tools/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`  

---

## 1. Executive Summary & Core Gameplay Loop

**Valkyrie's Cargo** introduces a dynamic celestial wandering merchant event into Valheim. Unlike static vanilla traders (Haldor and Hildir), this trader is delivered directly from the skies via the iconic **Valkyrie flight and drop sequence** (reused from the new character intro), arriving exclusively when a player has earned comfort and rest.

Upon touchdown, the trader disembarks, approaches the player's hearth, and calls out in the native overhead speech bubble system with atmospheric, lore-rich banter. The merchant maintains a **live, depleting inventory**, accepts both **coins and raw bartering commodities** (metals, gems, trophies, meads), and dynamically recalculates prices in real-time based on **supply and demand**.

After a maximum of **5 minutes** (matching Odin's apparition timer) or upon **early player dismissal**, the merchant bids farewell and vanishes in a burst of spectral mist using Odin's exact despawn visual effect (`vfx_odin_despawn`).

```
[Event Trigger: Player Rested + Comfort Level Met]
                      │
                      ▼
        [Valkyrie Descent & Swoop]
   (500m altitude -> 100m glide -> 10m drop)
                      │
                      ▼
          [Merchant Cargo Drop]
    (Valkyrie releases cargo & ascends away)
                      │
                      ▼
     [Approach Player & Overhead Callout]
(Walks to 3.5m radius; speaks overhead speech bubble)
                      │
                      ▼
           [Active Trade Session]
 ┌──────────────────────────────────────────────┐
 │ • Live inventory stock tracking              │
 │ • Real-time supply & demand price adjustments│
 │ • Multi-currency: Gold Coins & Barter goods  │
 └──────────────────────────────────────────────┘
                      │
        ┌─────────────┴─────────────┐
        ▼                           ▼
 [5-Minute Timer Expiry]    [Player Dismissal]
        └─────────────┬─────────────┘
                      ▼
    [Odin Spectral Apparition Vanish]
   (vfx_odin_despawn + sfx_spirit_death)
```

---

## 2. Event & Trigger Subsystem (`ValkyrieCargoEventManager`)

### 2.1 Trigger Requirements & State Verification
Vanilla Valheim raids (`RandEventSystem`) evaluate player base piece counts, global keys, and biomes. Because this event specifically demands that the player has earned **Rested status** and a **Comfort level**, a custom event evaluator runs on the server/host instance:

* **Rested Check:**
  ```csharp
  // Verified in assembly_valheim: SEMan & SE_Rested (AV:24219, AV:25351)
  bool isRested = player.GetSEMan().HaveStatusEffect("Rested".GetStableHashCode());
  ```
* **Comfort Level Check:**
  ```csharp
  // Verified in assembly_valheim: Player.GetComfortLevel() (AV:1834)
  int currentComfort = player.GetComfortLevel();
  bool comfortSufficient = currentComfort >= ModConfig.MinComfortLevel.Value; // Default: >= 4
  ```
* **Sky Clearance & Landing Zone Validation:**
  The Valkyrie descends from high altitude (500m) down to ground height (`m_dropHeight = 10f`). To prevent spawning inside enclosed caves or clipping heavily into rooftops:
  ```csharp
  // Raycast upward from the prospective drop point to verify open sky
  bool hasOpenSky = !Physics.Raycast(candidatePos + Vector3.up * 2f, Vector3.up, 60f, LayerMask.GetMask("Default", "piece", "terrain"));
  ```

### 2.2 Event Frequency & Scheduling
* **Check Interval:** Evaluated every 20–30 minutes (configurable via BepInEx config).
* **Roll Chance:** 25% base probability whenever Rested and Comfort conditions are satisfied.
* **Cooldown Protection:** Minimum 60-minute in-game lockout between arrivals to preserve excitement and economic balance.
* **HUD Announcement:** When the roll succeeds, a subtle horn sounds and a top-center message displays:
  > *"Wings beat in the upper skies... An emissary from Asgard descends."*

---

## 3. Valkyrie Transit & Cargo Drop Subsystem (`ValkyrieTransitManager`)

### 3.1 Mechanics of Vanilla `Valkyrie.cs` (`AV:128012`)
In vanilla Valheim, the intro Valkyrie operates via fixed trajectory math:
* `m_startAltitude = 500f`, `m_descentAltitude = 100f`, `m_dropHeight = 10f`.
* `m_startDistance = 500f`, `m_startDescentDistance = 200f`.
* `m_targetPoint = Player.m_localPlayer.transform.position + new Vector3(0, m_dropHeight, 0)`.
* `SyncPlayer()` hardcodes moving `Player.m_localPlayer` to `m_attachPoint`.
* `DropPlayer()` releases attachment, triggers `animator.SetBool("dropped", true)`, and flies away to `m_flyAwayPoint` (altitude 500m) before calling `m_nview.Destroy()`.

### 3.2 Hijacking Valkyrie for Merchant Delivery
The mod instantiates the Valkyrie prefab (loaded through `Player.m_localPlayer.m_valkyrie` `SoftReference` or from `Game.instance.m_playerPrefab`), attaches a lightweight tracking component `ValkyrieCargoCarrier`, and patches `Valkyrie` via Harmony:

```csharp
public class ValkyrieCargoCarrier : MonoBehaviour
{
    public GameObject CarriedMerchant;
    public bool IsCargoMission = true;
}
```

#### Harmony Patches on `Valkyrie`:
1. **`Valkyrie.Awake` (Prefix):**
   When `IsCargoMission` is true, sets `m_targetPoint` to a clear ground position 12–15m in front of the target player instead of directly on top of the player.
2. **`Valkyrie.SyncPlayer` (Prefix):**
   ```csharp
   [HarmonyPatch(typeof(Valkyrie), "SyncPlayer")]
   public static bool InterceptSync(Valkyrie __instance, bool doNetworkSync)
   {
       var carrier = __instance.GetComponent<ValkyrieCargoCarrier>();
       if (carrier != null && carrier.CarriedMerchant != null)
       {
           // Sync the Merchant to the Valkyrie talons instead of the local player
           carrier.CarriedMerchant.transform.rotation = __instance.m_attachPoint.rotation;
           carrier.CarriedMerchant.transform.position = __instance.m_attachPoint.position 
               - carrier.CarriedMerchant.transform.TransformVector(__instance.m_attachOffset);
           return false; // Suppress vanilla Player.m_localPlayer kidnapping
       }
       return true;
   }
   ```
3. **`Valkyrie.DropPlayer` (Prefix):**
   ```csharp
   [HarmonyPatch(typeof(Valkyrie), nameof(Valkyrie.DropPlayer))]
   public static bool InterceptDrop(Valkyrie __instance)
   {
       var carrier = __instance.GetComponent<ValkyrieCargoCarrier>();
       if (carrier != null && carrier.CarriedMerchant != null)
       {
           // Detach merchant and trigger ground touchdown sequence
           carrier.CarriedMerchant.transform.SetParent(null);
           var controller = carrier.CarriedMerchant.GetComponent<ValkyrieMerchantController>();
           controller.OnTouchdown();
           
           // Trigger Valkyrie release animation and let her ascend into the clouds
           __instance.GetComponentInChildren<Animator>().SetBool("dropped", true);
           return false;
       }
       return true;
   }
   ```

---

## 4. Merchant Lifecycle, AI & Departure

### 4.1 Custom Model Abstraction (`MerchantVisualSlot`)
Because custom 3D models and animations are pending, the architecture decouples logic from visuals:
* **Placeholder Model:** Cloned and re-textured vanilla Humanoid/Dvergr traveler rig with travel cloak, backpack, and horned helm.
* **Pending Custom Model Slot:** Exposes a clean loader:
  ```csharp
  // If an AssetBundle exists in plugins/ValkyrieCargo/merchant.bundle, instantiate custom mesh
  AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
  GameObject customModel = bundle.LoadAsset<GameObject>("ValkyrieMerchantVisual");
  ```

### 4.2 Touchdown & Ground Approach
1. **Touchdown:** Dropped from `10m` drop height with active physics; upon ground impact, instantiates `vfx_land_dust` and plays landing impact sound.
2. **Approach Movement:** Moves toward the rested player using `BaseAI.MoveTo(dt, player.transform.position, run: false)` until within **3.5 meters**.
3. **Stance & Gaze:** Reaches target radius, triggers idle greeting animation, and locks eyes via `LookAt.SetLookAtTarget(player.GetHeadPoint())`.

### 4.3 Overhead Dialogue & Voice
Using Valheim's native NPC overhead speech system (`Chat.instance.SetNpcText`, `AV:126040`):
* *"By Odin's ravens! That valkyrie doesn't know the meaning of a soft landing... Well met, warrior!"*
* *"Ah, the sweet smoke of a well-earned hearth! What goods do you bring to trade with Asgard?"*
* *"From Yggdrasil’s high branches to your front gate. Make it quick—my escort won't wait forever!"*
* *"A sturdy roof and rested bones! A true Viking knows the value of good coin and fine steel."*

### 4.4 5-Minute Lifespan & Early Dismissal
* **5-Minute Countdown (`m_ttl = 300f`):** Directly replicates the lifetime timer from `Odin.cs` (`AV:116931`).
* **1-Minute Warning:** The merchant speaks: *"Hurry your bargaining, friend! The Valkyrie's horn sounds in the wind."*
* **Manual Dismissal:**
  Hover text displays:
  ```
  [<color=yellow><b>E</b></color>] Trade
  [<color=yellow><b>LeftShift + E</b></color>] Dismiss Merchant
  ```
* **Departure Sequence (Odin Apparition Mechanics):**
  When time expires or upon dismissal:
  1. Closes open `StoreGui` if the player is currently browsing.
  2. Merchant calls out: *"The Allfather calls me back to the mist!"*
  3. Instantiates `ZNetScene.instance.GetPrefab("vfx_odin_despawn")` at the merchant's feet.
  4. Plays spectral dissipation sound (`sfx_spirit_death`).
  5. Destroys the GameObject via `m_nview.Destroy()`.

---

## 5. Dynamic Economy & Real-Time Supply/Demand Engine

### 5.1 Real-Time Pricing Formula
Unlike vanilla Haldor/Hildir where prices are static and stocks are infinite, *Valkyrie's Cargo* implements dynamic market pricing:

$$\text{Price} = \text{BasePrice} \times \left( \frac{\text{TargetStock}}{\max(1, \text{CurrentStock})} \right)^{\alpha}$$

* $\text{TargetStock}$: Baseline inventory level (e.g., 20 Iron, 50 Wood, 5 Mead).
* $\text{CurrentStock}$: Live units held by the merchant.
* $\alpha$ (Elasticity): Sensitivity exponent (default `0.35`).
* **Price Clamps:** Clamped between `0.4x` (surplus floor) and `3.0x` (scarcity ceiling).

#### Dynamic Reactions:
* **Player Buys Item:** $\text{CurrentStock} \downarrow \implies \text{Price} \uparrow$ immediately for remaining units.
* **Player Sells Item:** $\text{CurrentStock} \uparrow \implies \text{Price} \downarrow$ immediately.
* **Stock Depletion:** When stock hits 0, item shows as **"SOLD OUT"** and purchase is disabled.

### 5.2 Multi-Currency & Bartering Matrix
The trader accepts **Gold Coins** and raw **Barter Commodities**:

| Commodity | Barter Tier / Category | Exchange Value (Coin Equiv.) |
|---|---|---|
| **Coins** | Liquid Currency | 1.0 (1:1 baseline) |
| **Amber / Ruby / Pearl** | Valuables | 5 / 20 / 50 |
| **Copper / Tin / Bronze** | Basic Metals | 6 / 6 / 15 |
| **Iron Ore / Iron** | Mid-Tier Metal | 30 |
| **Silver / Silver Ore** | Mountain Metal | 45 |
| **Black Metal Scrap** | Plains Metal | 65 |
| **Flametal / Black Core** | End-Game Barter | 120 / 350 |
| **Trophies & Meads** | Rare Supplies | Tier-scaled |

When browsing the merchant, the player can toggle between paying in **Gold Coins** or **Barter Credit** (computed automatically from valid barter items in the player's inventory).

---

## 6. UI & StoreGui Integration Architecture

In vanilla Valheim, `StoreGui` (`AV:54299`) is hardcoded to `m_coinPrefab` and only allows selling items where `m_shared.m_value > 0`. We cleanly extend it without third-party UI libraries:

```
┌─────────────────────────────────────────────────────────────┐
│                       VALKYRIE'S CARGO                      │
│                "Emissary of the Allfather"                  │
│                                                             │
│  [Time Remaining: 03:42]              [Dismiss Merchant]    │
├─────────────────────────────────────────────────────────────┤
│  Payment Mode: [● Coins (840)]  [○ Barter Credit (1,250)]   │
├─────────────────────────────────────────────────────────────┤
│  [Icon] Iron Ingot x10       Stock: [ 8 ]  Price:  38c  ▲   │
│  [Icon] Serpent Stew x2      Stock: [ 3 ]  Price:  75c  ▲   │
│  [Icon] Frost Arrow x50      Stock: [200]  Price:  12c  ▼   │
│  [Icon] Black Core x1        Stock: [ 0 ]  SOLD OUT         │
├─────────────────────────────────────────────────────────────┤
│  [ BUY SELECTED ]                     [ SELL COMMODITIES ]  │
└─────────────────────────────────────────────────────────────┘
```

### 6.1 StoreGui Harmony Patches
* **`StoreGui.Show` (Postfix):** If active trader is `ValkyrieMerchant`, injects the Barter Selector, Stock Count labels, and Time Remaining header into `StoreGui.m_rootPanel`.
* **`StoreGui.FillList` (Postfix):** Formats element price texts to include price trend indicators (`▲` in red for price surges due to scarcity, `▼` in green for high supply discounts) and remaining stock.
* **`StoreGui.BuySelectedItem` (Prefix):**
  - Checks if item is in stock (`CurrentStock > 0`).
  - Verifies affordability against selected currency (Coins or deducted Barter items).
  - Deducts stock, adds items to player inventory, updates dynamic price, and invokes `StoreGui.FillList()`.
* **`StoreGui.GetSellableItem` (Prefix Override):**
  Vanilla only permits items with `m_shared.m_value > 0`. Our patch allows selling any item defined in the merchant's trade catalogue (metals, food, trophies, raw lumber), rewarding the player with coins or barter credit.

---

## 7. Configuration Schema (`com.raveniron.valkyriescargo.cfg`)

```ini
[1 - Event Triggers]
## Minimum comfort level required for the Valkyrie drop event to roll
# Setting type: Int32
# Default value: 4
MinComfortLevel = 4

## Whether the player must have the Rested status effect active
# Setting type: Boolean
# Default value: true
RequireRested = true

## Interval in minutes between event checks
# Setting type: Single
# Default value: 25.0
EventCheckIntervalMinutes = 25.0

## Percent chance (0-100) of event triggering on check if conditions are met
# Setting type: Single
# Default value: 25.0
EventChancePercent = 25.0

[2 - Merchant Behavior]
## Total time in seconds the merchant remains before vanishing (like Odin)
# Setting type: Single
# Default value: 300.0
MerchantLifespanSeconds = 300.0

## Distance in meters the merchant stops from the player after touchdown
# Setting type: Single
# Default value: 3.5
ApproachDistance = 3.5

[3 - Dynamic Economy]
## Price elasticity factor (higher = steeper price jumps with scarcity)
# Setting type: Single
# Default value: 0.35
PriceElasticity = 0.35

## Minimum price multiplier floor (prevent extreme crashes)
# Setting type: Single
# Default value: 0.4
MinPriceMultiplier = 0.4

## Maximum price multiplier ceiling (prevent runaway prices)
# Setting type: Single
# Default value: 3.0
MaxPriceMultiplier = 3.0

## Enable barter trading using metals, gems, and materials
# Setting type: Boolean
# Default value: true
EnableBarterSystem = true
```

---

## 8. Implementation Milestones

1. **Milestone 1 — Core Valkyrie Flight & Detach Patch:**
   * Instantiate `m_valkyrie` with `ValkyrieCargoCarrier`.
   * Validate Harmony hooks on `Valkyrie.SyncPlayer` and `Valkyrie.DropPlayer`.
   * Verify carrier detachment and touchdown outside player builds.
2. **Milestone 2 — Merchant AI, Greet & Odin Despawn:**
   * Implement ground approach movement to player coordinates.
   * Connect `Chat.instance.SetNpcText` with randomized dialogue.
   * Hook 5-minute timer and early dismissal triggering `vfx_odin_despawn`.
3. **Milestone 3 — Live Inventory & Dynamic Economy Engine:**
   * Build `MarketInventory` holding item IDs, target stock, and live stock.
   * Implement real-time elasticity formula for purchases and sales.
   * Implement Barter table converting inventory commodities into trade credit.
4. **Milestone 4 — StoreGui Integration & Custom Model Slot:**
   * Patch `StoreGui` to render stock counts, price trends, and barter toggles.
   * Add asset loader fallback: ready to receive custom 3D merchant model and animations.
