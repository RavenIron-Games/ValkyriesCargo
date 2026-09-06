# Ingvar's Catalogue — data-backed defaults (v1, 2026-09-06)

> One-screen summary: `TLDR.md`, second half.

**Source data:** `docs/data/items-valheim-2026-07-31.tsv`, 1,084 items extracted from Wu'barrk's TheEye
`Values_Dump.json` (2026-07-31; every `ItemDrop.ItemData.SharedData` field per prefab), plus the 365 recipes in the
same dump. Every prefab below exists in that table under exactly that name. The game has moved to 0.221.x since;
`cargo prefab <name>` at boot is the live check, and an unknown name is dropped from the catalogue with one log line,
never a crash.

**Names that trip people up** (all verified in the table): core wood is `RoundLog`; there is no `Charcoal`, only `Coal`;
there is no `Silk`; `Flametal` is the *old* flametal (`$item_flametal_old`), the live one is `FlametalNew` with ore
`FlametalOreNew`; coins are the `Coins` prefab, stack 999, value 1.

---

## 1. How the numbers were chosen

- **Anchors.** Vanilla gives four items a coin value that Haldor pays flat: Amber 5, Amber Pearl 10, Ruby 20, Silver
  Necklace 30. Ingvar pays `base × SpreadBuy` (0.7) at target stock, so their bases are set to `value / 0.7` rounded:
  he pays Haldor's rate at target stock, more when he is short, less when he is flooded. Haldor's price is the floor a
  player already has; Ingvar's job is to be better than that when it matters.
- **Everything else has no vanilla value.** Base prices follow the progression ladder (tin, copper, bronze, iron, silver,
  black metal, flametal) with refined bars above their ores, and within a tier by weight and rarity.
- **Target stock** is what he "normally carries": roughly two to four stacks for commons, half a stack to a stack for
  metals, a handful for rare goods. `Max` is 3 × target; above it he refuses ("I've all the linen a man can carry").
- **Recipe demand** (how many vanilla recipes consume a material) ranked the wants list and nudged targets up for the
  materials players burn most. Top of the table: Wood (55 recipes), Deer hide (53), Flametal (42), Iron (41), Leather
  scraps (32), Fine wood (31), Eitr (27), Linen thread (25), Feathers (21), Bronze (20), Ask hide (20), Silver (20).
- **Non-teleportable metals are the point.** Ores and bars cannot go through portals; Ingvar lands at the base with the
  smelter, so selling him iron scrap is the one liquidation that never needs a boat.

Price at any moment: `base × clamp((target / max(1, stock))^0.35, 0.4, 3.0)`; he pays 0.7 × that. Stock drifts back to
target with a one-game-day half-life between visits. Purse 800 coins plus half of last visit's takings.

---

## 2. Wares — he sells these and buys them back

| Prefab | Display | Type | Stack | Weight | Base | Target | Max | Why |
|---|---|---|---|---|---|---|---|---|
| Bronze | Bronze | Material | 30 | 12 | 15 | 20 | 60 | 2 copper + 1 tin; 20 recipes |
| Iron | Iron | Material | 30 | 12 | 25 | 20 | 60 | 41 recipes, 519 units; the workhorse metal |
| Silver | Silver | Material | 30 | 14 | 40 | 12 | 36 | 20 recipes, mountain-gated |
| BlackMetal | Black metal | Material | 30 | 12 | 60 | 10 | 30 | 15 recipes, plains-gated |
| FlametalNew | Flametal | Material | 30 | 12 | 110 | 6 | 18 | 42 recipes, ashlands-gated |
| Eitr | Refined eitr | Material | 30 | 5 | 45 | 10 | 30 | 27 recipes, 311 units |
| BlackCore | Black core | Material | 20 | 1 | 300 | 2 | 6 | dungeon-only; the rarest thing he carries |
| Amber | Amber | Material | 20 | 0.1 | 7 | 30 | 90 | vanilla value 5 → pays 5 at target |
| AmberPearl | Amber pearl | Material | 50 | 0.1 | 14 | 20 | 60 | vanilla value 10 |
| Ruby | Ruby | Material | 20 | 0.1 | 29 | 15 | 45 | vanilla value 20 |
| SilverNecklace | Silver necklace | Material | 20 | 0.1 | 43 | 8 | 24 | vanilla value 30 |
| ArrowIron | Iron arrow | Ammo | 100 | 0.1 | 2 | 100 | 300 | consumable, sells by the stack |
| ArrowFrost | Frost arrow | Ammo | 100 | 0.1 | 3 | 100 | 300 | |
| BoltIron | Iron bolt | Ammo | 100 | 0.1 | 3 | 100 | 300 | |
| MeadHealthMinor | Minor healing mead | Consumable | 10 | 1 | 12 | 10 | 30 | |
| MeadStaminaMinor | Minor stamina mead | Consumable | 10 | 1 | 12 | 10 | 30 | |
| MeadTasty | Tasty mead | Consumable | 10 | 1 | 10 | 10 | 30 | |
| Honey | Honey | Consumable | 50 | 0.2 | 2 | 50 | 150 | 19 recipes; every mead starts here |

## 3. Wants — he only buys these

**Wood and stone**

| Prefab | Display | Stack | Weight | Base | Target | Max | Recipes |
|---|---|---|---|---|---|---|---|
| Wood | Wood | 50 | 2 | 1 | 200 | 600 | 55 |
| RoundLog | Core wood | 50 | 2 | 2 | 100 | 300 | 15 |
| FineWood | Fine wood | 50 | 2 | 2 | 100 | 300 | 31 |
| ElderBark | Ancient bark | 50 | 2 | 3 | 60 | 180 | 12 |
| Blackwood | Blackwood | 50 | 2 | 4 | 60 | 180 | 11 |
| YggdrasilWood | Yggdrasil wood | 50 | 2 | 5 | 60 | 180 | 9 |
| Resin | Resin | 50 | 0.3 | 1 | 100 | 300 | 11 |
| Coal | Coal | 50 | 2 | 1 | 100 | 300 | |
| Stone | Stone | 50 | 2 | 1 | 200 | 600 | 6 |
| Flint | Flint | 30 | 2 | 1 | 60 | 180 | 4 |
| Feathers | Feathers | 50 | 0.1 | 2 | 60 | 180 | 21 |

**Hides and leather**

| Prefab | Display | Stack | Weight | Base | Target | Max | Recipes |
|---|---|---|---|---|---|---|---|
| LeatherScraps | Leather scraps | 50 | 0.5 | 2 | 60 | 180 | 32 |
| DeerHide | Deer hide | 50 | 1 | 3 | 60 | 180 | 53 |
| TrollHide | Troll hide | 20 | 2 | 6 | 20 | 60 | 6 |
| WolfPelt | Wolf pelt | 50 | 1 | 6 | 40 | 120 | 7 |
| LoxPelt | Lox pelt | 50 | 1 | 8 | 40 | 120 | 5 |
| ScaleHide | Scale hide | 50 | 0.5 | 6 | 40 | 120 | 11 |
| AskHide | Ask hide | 50 | 1 | 10 | 40 | 120 | 20 |
| BjornHide | Bjorn hide | 50 | 1 | 10 | 40 | 120 | 9 |

**Textiles and farm**

| Prefab | Display | Stack | Weight | Base | Target | Max | Recipes |
|---|---|---|---|---|---|---|---|
| Flax | Flax | 100 | 0.2 | 3 | 100 | 300 | |
| LinenThread | Linen thread | 50 | 2 | 10 | 50 | 150 | 25 |
| Barley | Barley | 100 | 0.2 | 3 | 100 | 300 | |
| JuteRed | Red jute | 50 | 2 | 6 | 40 | 120 | |
| JuteBlue | Blue jute | 50 | 2 | 8 | 40 | 120 | |
| WolfHairBundle | Wolf hair bundle | 50 | 1 | 4 | 40 | 120 | 4 |

**Ores and scrap** (none teleport; this is where he earns his keep)

| Prefab | Display | Stack | Weight | Base | Target | Max |
|---|---|---|---|---|---|---|
| CopperOre | Copper ore | 30 | 10 | 5 | 40 | 120 |
| TinOre | Tin ore | 30 | 8 | 5 | 40 | 120 |
| IronScrap | Scrap iron | 30 | 10 | 22 | 30 | 90 |
| SilverOre | Silver ore | 30 | 14 | 36 | 20 | 60 |
| BlackMetalScrap | Black metal scrap | 30 | 10 | 50 | 20 | 60 |
| FlametalOreNew | Flametal ore | 30 | 10 | 90 | 10 | 30 |

**Creature parts and oddments**

| Prefab | Display | Stack | Weight | Base | Target | Max | Recipes |
|---|---|---|---|---|---|---|---|
| Guck | Guck | 50 | 0.5 | 4 | 40 | 120 | |
| Bloodbag | Bloodbag | 50 | 0.5 | 3 | 40 | 120 | 5 |
| Entrails | Entrails | 50 | 0.5 | 3 | 40 | 120 | |
| Ooze | Ooze | 50 | 0.5 | 3 | 40 | 120 | |
| Chain | Chain | 50 | 2 | 12 | 20 | 60 | |
| Chitin | Chitin | 50 | 2 | 5 | 40 | 120 | |
| Obsidian | Obsidian | 50 | 2 | 4 | 40 | 120 | |
| Crystal | Crystal | 50 | 1 | 8 | 20 | 60 | 4 |
| FreezeGland | Freeze gland | 50 | 0.5 | 4 | 40 | 120 | 4 |
| Needle | Needle | 50 | 0.5 | 6 | 40 | 120 | |
| SurtlingCore | Surtling core | 10 | 5 | 15 | 10 | 30 | |
| Sap | Sap | 50 | 0.2 | 6 | 40 | 120 | 9 |
| Softtissue | Soft tissue | 40 | 1 | 8 | 30 | 90 | |
| Carapace | Carapace | 50 | 2 | 10 | 40 | 120 | 9 |
| WitheredBone | Withered bone | 30 | 1 | 8 | 30 | 90 | |

**Trophies** (he collects; a little coin for the wall clutter)

| Prefab | Base | Target | Max |
|---|---|---|---|
| TrophyDeer | 8 | 10 | 30 |
| TrophyBoar | 8 | 10 | 30 |
| TrophyNeck | 6 | 10 | 30 |
| TrophyGreydwarf | 8 | 10 | 30 |
| TrophySkeleton | 8 | 10 | 30 |
| TrophyDraugr | 12 | 10 | 30 |
| TrophyWolf | 15 | 10 | 30 |
| TrophyGoblin | 15 | 10 | 30 |

---

## 4. The config line (mirrors the tables; `Prefab:Base:Target:Max:Kind`)

```
Catalogue = Bronze:15:20:60:Ware, Iron:25:20:60:Ware, Silver:40:12:36:Ware, BlackMetal:60:10:30:Ware,
  FlametalNew:110:6:18:Ware, Eitr:45:10:30:Ware, BlackCore:300:2:6:Ware, Amber:7:30:90:Ware, AmberPearl:14:20:60:Ware,
  Ruby:29:15:45:Ware, SilverNecklace:43:8:24:Ware, ArrowIron:2:100:300:Ware, ArrowFrost:3:100:300:Ware,
  BoltIron:3:100:300:Ware, MeadHealthMinor:12:10:30:Ware, MeadStaminaMinor:12:10:30:Ware, MeadTasty:10:10:30:Ware,
  Honey:2:50:150:Ware,
  Wood:1:200:600:Want, RoundLog:2:100:300:Want, FineWood:2:100:300:Want, ElderBark:3:60:180:Want,
  Blackwood:4:60:180:Want, YggdrasilWood:5:60:180:Want, Resin:1:100:300:Want, Coal:1:100:300:Want,
  Stone:1:200:600:Want, Flint:1:60:180:Want, Feathers:2:60:180:Want,
  LeatherScraps:2:60:180:Want, DeerHide:3:60:180:Want, TrollHide:6:20:60:Want, WolfPelt:6:40:120:Want,
  LoxPelt:8:40:120:Want, ScaleHide:6:40:120:Want, AskHide:10:40:120:Want, BjornHide:10:40:120:Want,
  Flax:3:100:300:Want, LinenThread:10:50:150:Want, Barley:3:100:300:Want, JuteRed:6:40:120:Want,
  JuteBlue:8:40:120:Want, WolfHairBundle:4:40:120:Want,
  CopperOre:5:40:120:Want, TinOre:5:40:120:Want, IronScrap:22:30:90:Want, SilverOre:36:20:60:Want,
  BlackMetalScrap:50:20:60:Want, FlametalOreNew:90:10:30:Want,
  Guck:4:40:120:Want, Bloodbag:3:40:120:Want, Entrails:3:40:120:Want, Ooze:3:40:120:Want, Chain:12:20:60:Want,
  Chitin:5:40:120:Want, Obsidian:4:40:120:Want, Crystal:8:20:60:Want, FreezeGland:4:40:120:Want,
  Needle:6:40:120:Want, SurtlingCore:15:10:30:Want, Sap:6:40:120:Want, Softtissue:8:30:90:Want,
  Carapace:10:40:120:Want, WitheredBone:8:30:90:Want,
  TrophyDeer:8:10:30:Want, TrophyBoar:8:10:30:Want, TrophyNeck:6:10:30:Want, TrophyGreydwarf:8:10:30:Want,
  TrophySkeleton:8:10:30:Want, TrophyDraugr:12:10:30:Want, TrophyWolf:15:10:30:Want, TrophyGoblin:15:10:30:Want
```

Seventy-two entries. `MarketState` at ~40 bytes a row is under 3 KB, below ServerSync's compression floor, so it goes
uncompressed on every change.

---

## 5. Worked numbers, so the curve is felt before it is played

- A player sells Ingvar 60 scrap iron in one visit (two stacks, 600 kg hauled). Stock 30 → 90 (the max; the 91st is
  refused). Price falls from 22 to `22 × (30/90)^0.35 = 15`; he paid 15.4 → 10.8 a unit over the run, about 780 coins,
  most of the purse. Next visit a game-day later stock has drifted halfway back, so he pays about 13.
- A player buys the two Black Cores. Stock 2 → 0, SOLD OUT; the price he would charge for a third is clamped at
  `300 × 3.0 = 900`. Two game-days later stock is back near 1.5, price near 340.
- Amber, 30 in stock at target: he pays 5, Haldor's rate. After someone dumps 60, he pays 3.4; when he is down to 10, he
  pays 7.3. Haldor never moves. That spread is the whole reason to walk to the merchant instead of the trader.

---

## 6. What the pure core tests check against this file

`Core/Catalogue` parses the config line; a test loads `docs/data/items-valheim-2026-07-31.tsv` and asserts every
default prefab exists there with a positive stack size, that no Ware's base is below its vanilla value / 0.7, that
every Want with a vanilla value of 0 has a base ≥ 1, and that `Max ≥ Target > 0` throughout. A catalogue edit that
misspells a prefab fails the test on the desk, not in someone's world.
