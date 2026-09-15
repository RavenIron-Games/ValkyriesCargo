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
  he matches Haldor's rate exactly at target stock. All four are Wares, so since the Fair Market Act (§5) he never
  pays MORE than that rate, whether at target or short — only ever less, and only once a flooded shelf drags the
  multiplier under 1.0. Before 2026-09-07 he outbid Haldor while short; that was also the round trip's door in
  (§5), so it is closed now. Haldor's price is the floor a player already has walking in; Ingvar matches it, never
  beats it, at target or scarcer.
- **Everything else has no vanilla value.** Base prices follow the progression ladder (tin, copper, bronze, iron, silver,
  black metal, flametal) with refined bars above their ores, and within a tier by weight and rarity.
- **Target stock** is what he "normally carries": roughly two to four stacks for commons, half a stack to a stack for
  metals, a handful for rare goods. `Max` is 3 × target; above it he refuses ("I've all the linen a man can carry").
- **Recipe demand** (how many vanilla recipes consume a material) ranked the wants list and nudged targets up for the
  materials players burn most. Top of the table: Wood (55 recipes), Deer hide (53), Flametal (42), Iron (41), Leather
  scraps (32), Fine wood (31), Eitr (27), Linen thread (25), Feathers (21), Bronze (20), Ask hide (20), Silver (20).
- **Non-teleportable metals are the point.** Ores and bars cannot go through portals; Ingvar lands at the base with the
  smelter, so selling him iron scrap is the one liquidation that never needs a boat.

Price at any moment: `base × clamp((target / max(1, stock))^0.35, 0.4, 3.0)`; he pays 0.7 × that — for a Ware, capped
so the multiplier on this side never exceeds 1.0 (the Fair Market Act, §5): he can buy back below par when flooded,
never above it when short. Between visits a Want's stock drifts back to target with a three-game-day half-life; a Ware's
does not drift at all (`WareHalfLifeGameDays` 0, the owner's 2026-09-07 call: what he sells is what players sold him and
what an admin's target says; `docs/ECONOMY-SIM.md` §10). Purse 1500 coins (`Server.PurseCoins`,
raised from 800 by the Fair Market Act decision, `docs/DECISIONS-WUBARRK.md` §2) plus half of last visit's
takings, measured on the gross.

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
| BoarJerky | Boar jerky | Consumable | 20 | 0.5 | 3 | 20 | 60 | the cauldron's road ration; a craft makes two, so base is the table's 5 halved (the review) |
| Bread | Bread | Consumable | 20 | 0.5 | 40 | 10 | 30 | the barley loaf; at par with ten barley, and target 10 so the row is not the catalogue's biggest |
| MinceMeatSauce | Minced meat sauce | Consumable | 10 | 1 | 9 | 10 | 30 | the Black Forest's cauldron dish |
| SerpentStew | Serpent stew | Consumable | 10 | 1 | 18 | 10 | 30 | the Swamp's stew; serpent meat and mushroom are both Wants below |
| CookedLoxMeat | Cooked lox meat | Consumable | 20 | 2 | 7 | 20 | 60 | lox steak off the cookfire |
| MeadHealthMedium | Medium healing mead | Consumable | 10 | 1 | 10 | 10 | 30 | the healing line's middle rung |
| MeadStaminaMedium | Medium stamina mead | Consumable | 10 | 1 | 12 | 10 | 30 | the stamina line's middle rung |
| MeadFrostResist | Frost resistance mead | Consumable | 10 | 1 | 10 | 10 | 30 | the Mountains' brew |
| MeadPoisonResist | Poison resistance mead | Consumable | 10 | 1 | 10 | 10 | 30 | the Swamp's brew |

## 3. Wants — he only buys these

**Wood and stone**

| Prefab | Display | Stack | Weight | Base | Target | Max | Recipes |
|---|---|---|---|---|---|---|---|
| Wood | Wood | 50 | 2 | 1 | 200 | 600 | 55 |
| RoundLog | Core wood | 50 | 2 | 3 | 100 | 300 | 15 |
| FineWood | Fine wood | 50 | 2 | 3 | 100 | 300 | 31 |
| ElderBark | Ancient bark | 50 | 2 | 3 | 60 | 180 | 12 |
| Blackwood | Blackwood | 50 | 2 | 4 | 60 | 180 | 11 |
| YggdrasilWood | Yggdrasil wood | 50 | 2 | 5 | 60 | 180 | 9 |
| Resin | Resin | 50 | 0.3 | 1 | 100 | 300 | 11 |
| Coal | Coal | 50 | 2 | 1 | 100 | 300 | |
| Stone | Stone | 50 | 2 | 1 | 200 | 600 | 6 |
| Flint | Flint | 30 | 2 | 1 | 60 | 180 | 4 |
| Feathers | Feathers | 50 | 0.1 | 3 | 60 | 180 | 21 |

**Hides and leather**

| Prefab | Display | Stack | Weight | Base | Target | Max | Recipes |
|---|---|---|---|---|---|---|---|
| LeatherScraps | Leather scraps | 50 | 0.5 | 3 | 60 | 180 | 32 |
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

**Food and drink he buys** (2026-09-15; section 8)

| Prefab | Display | Stack | Weight | Base | Target | Max | Why |
|---|---|---|---|---|---|---|---|
| Raspberry | Raspberries | 50 | 0.1 | 2 | 50 | 150 | Meadows forage |
| Blueberries | Blueberries | 50 | 0.1 | 3 | 50 | 150 | Black Forest forage |
| Cloudberry | Cloudberries | 50 | 0.1 | 6 | 50 | 150 | Plains forage; the third part of every Plains pie |
| Mushroom | Mushroom | 50 | 0.1 | 2 | 50 | 150 | forage from the Meadows to the Swamp; into the serpent stew |
| MushroomYellow | Yellow mushroom | 50 | 0.1 | 3 | 50 | 150 | cave forage; the value table's row is mislabelled (section 8), so this is the raw rule for a tier-2 common |
| Carrot | Carrot | 50 | 0.3 | 2 | 50 | 150 | the first farmed crop |
| Turnip | Turnip | 50 | 0.3 | 6 | 50 | 150 | the Swamp's crop |
| Onion | Onion | 50 | 0.3 | 2 | 50 | 150 | the Mountains' crop |
| CarrotSeeds | Carrot seeds | 100 | 0.1 | 2 | 50 | 150 | the farm's starts |
| TurnipSeeds | Turnip seeds | 100 | 0.1 | 5 | 50 | 150 |  |
| OnionSeeds | Onion seeds | 100 | 0.1 | 5 | 50 | 150 |  |
| RawMeat | Boar meat | 20 | 1 | 2 | 40 | 120 | the prefab is `RawMeat`; the cookfire's raw side, on the hides' numbers |
| DeerMeat | Deer meat | 20 | 1 | 2 | 40 | 120 |  |
| NeckTail | Neck tail | 50 | 0.5 | 3 | 50 | 150 | stacks 50, so it takes the produce numbers |
| WolfMeat | Wolf meat | 20 | 1 | 5 | 40 | 120 |  |
| LoxMeat | Lox meat | 20 | 2 | 6 | 40 | 120 |  |
| SerpentMeat | Serpent meat | 50 | 10 | 5 | 40 | 120 | weight 10 a unit, so the meat numbers rather than the 50-stack ones |
| ChickenMeat | Chicken meat | 20 | 1 | 2 | 40 | 120 | once a base keeps hens |
| HareMeat | Hare meat | 20 | 1 | 7 | 40 | 120 | Mistlands |
| FishRaw | Raw fish | 20 | 0.5 | 2 | 40 | 120 | the rod's common catch; `FishAnglerRaw` shares its item token and is left out (section 8) |

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
  FlametalNew:110:6:18:Ware, Eitr:45:10:30:Ware, BlackCore:300:2:6:Ware, Amber:7:30:90:Ware,
  AmberPearl:14:20:60:Ware, Ruby:29:15:45:Ware, SilverNecklace:43:8:24:Ware, ArrowIron:2:100:300:Ware,
  ArrowFrost:3:100:300:Ware, BoltIron:3:100:300:Ware, MeadHealthMinor:12:10:30:Ware, MeadStaminaMinor:12:10:30:Ware,
  MeadTasty:10:10:30:Ware, Honey:2:50:150:Ware, BoarJerky:3:20:60:Ware, Bread:40:10:30:Ware,
  MinceMeatSauce:9:10:30:Ware, SerpentStew:18:10:30:Ware, CookedLoxMeat:7:20:60:Ware, MeadHealthMedium:10:10:30:Ware,
  MeadStaminaMedium:12:10:30:Ware, MeadFrostResist:10:10:30:Ware, MeadPoisonResist:10:10:30:Ware,
  Wood:1:200:600:Want, RoundLog:3:100:300:Want, FineWood:3:100:300:Want, ElderBark:3:60:180:Want,
  Blackwood:4:60:180:Want, YggdrasilWood:5:60:180:Want, Resin:1:100:300:Want, Coal:1:100:300:Want,
  Stone:1:200:600:Want, Flint:1:60:180:Want, Feathers:3:60:180:Want, LeatherScraps:3:60:180:Want,
  DeerHide:3:60:180:Want, TrollHide:6:20:60:Want, WolfPelt:6:40:120:Want, LoxPelt:8:40:120:Want,
  ScaleHide:6:40:120:Want, AskHide:10:40:120:Want, BjornHide:10:40:120:Want, Flax:3:100:300:Want,
  LinenThread:10:50:150:Want, Barley:3:100:300:Want, JuteRed:6:40:120:Want, JuteBlue:8:40:120:Want,
  WolfHairBundle:4:40:120:Want, CopperOre:5:40:120:Want, TinOre:5:40:120:Want, IronScrap:22:30:90:Want,
  SilverOre:36:20:60:Want, BlackMetalScrap:50:20:60:Want, FlametalOreNew:90:10:30:Want, Guck:4:40:120:Want,
  Bloodbag:3:40:120:Want, Entrails:3:40:120:Want, Ooze:3:40:120:Want, Chain:12:20:60:Want, Chitin:5:40:120:Want,
  Obsidian:4:40:120:Want, Crystal:8:20:60:Want, FreezeGland:4:40:120:Want, Needle:6:40:120:Want,
  SurtlingCore:15:10:30:Want, Sap:6:40:120:Want, Softtissue:8:30:90:Want, Carapace:10:40:120:Want,
  WitheredBone:8:30:90:Want, TrophyDeer:8:10:30:Want, TrophyBoar:8:10:30:Want, TrophyNeck:6:10:30:Want,
  TrophyGreydwarf:8:10:30:Want, TrophySkeleton:8:10:30:Want, TrophyDraugr:12:10:30:Want, TrophyWolf:15:10:30:Want,
  TrophyGoblin:15:10:30:Want, Raspberry:2:50:150:Want, Blueberries:3:50:150:Want, Cloudberry:6:50:150:Want,
  Mushroom:2:50:150:Want, MushroomYellow:3:50:150:Want, Carrot:2:50:150:Want, Turnip:6:50:150:Want,
  Onion:2:50:150:Want, CarrotSeeds:2:50:150:Want, TurnipSeeds:5:50:150:Want, OnionSeeds:5:50:150:Want,
  RawMeat:2:40:120:Want, DeerMeat:2:40:120:Want, NeckTail:3:50:150:Want, WolfMeat:5:40:120:Want,
  LoxMeat:6:40:120:Want, SerpentMeat:5:40:120:Want, ChickenMeat:2:40:120:Want, HareMeat:7:40:120:Want,
  FishRaw:2:40:120:Want
```

A hundred and one entries (72 until 2026-09-15; section 8). `MarketState` at ~40 bytes a row is about 4 KB, still
under ServerSync's compression floor (10 KB), so it goes uncompressed on every change.

**Editing it on a running server (2026-09-07).** The line is the config entry `Server.Catalogue`, synced and locked,
so it can change three ways: the cfg file on the server (a restart reads it), Configuration Manager on an admin's
client (ServerSync accepts a locked value from anyone on the admin list), or the console — `cargo catalogue add
Prefab:Base:Target:Max:Kind` (add, or change an entry already there, in place), `cargo catalogue remove Prefab`,
`cargo catalogue reset`, all admin, from any console; `cargo catalogue list` prints it and needs no admin. The console
verbs edit the entry itself, so the sync, the lock, the cfg file and the BarrkBOT export all follow. However it
changed, the director applies it as soon as no visit is running: stock and drift stamps carry by prefab, a new row
starts at target, a dropped row goes, a lowered max clamps, and the purse, the visit number and the delivery sequence
carry (`Market.WithCatalogue`, in the harness). While a visit runs the change waits, once in the log and always in
`cargo status`, because the settled-deal ring does not carry. A prefab this game has no item for is refused by `add` in
words, and dropped from a hand-edited line with one log line.

---

## 5. Worked numbers, so the curve is felt before it is played

Every number here comes out of `Core/Market.cs` and is asserted by the harness (§6). The rounding rule: the charge is
`base × multiplier` rounded once; what he pays is `base × multiplier × 0.7` rounded once, never the rounded charge
times 0.7 (that squashes the spread on cheap goods); both never below 1. **The Fair Market Act** (2026-09-07,
`docs/DECISIONS-WUBARRK.md` §2): for a Ware, the multiplier on the pay side is additionally capped at 1.0 before
the 0.7 is applied, so he never pays more than `base × 0.7` — the target-stock rate — for something he also sells.
The charge side and every Want are untouched.

- A player sells Ingvar scrap iron (base 22, target 30, max 90). At target he pays 15 a unit. A deal is priced as a
  whole at the price on the screen when it is confirmed (the `UnitPriceSeen` rule): fifty in one deal is `50 × 15 = 750`,
  most of the 800 purse; sixty in one deal is 900 and comes back `purse_empty`. Sold one at a time the price walks down
  as his stock climbs, 15 → 10, and the sixty fetch about 745. Stock 30 → 90 is his max; the 91st is refused
  `over_max`. Next visit a game-day later the stock has drifted halfway back to 60, so he pays 12.
- A player buys the two Black Cores (base 300, target 2). Stock 2 → 0, SOLD OUT; the price he would ask for a third is
  `300 × (2/1)^0.35 = 382`: an empty shelf is priced as if one were left, so the 3.0 ceiling only ever binds on rows
  with a target of 24 or more (wood, stone, arrows). Two game-days later the stock is back to 2 and the price to 300;
  stock is whole units, there is no "1.5".
- Amber (base 7, target 30, a Ware): at target he pays 5, Haldor's rate. After someone dumps 60 he pays 3. When he is
  down to 10 the bare curve would ask `7 × 3^0.35 × 0.7 = 7.2 → 7`, more than Haldor — that was the round trip's door
  in, since he sells Amber too (§9 of `docs/ECONOMY-SIM.md`). The Fair Market Act holds a Ware's buy-back at par, so
  he still pays 5, not 7, however short he is. Haldor never moves. That spread is the whole reason to walk to the
  merchant instead of the trader when the shelf is flooded — not, any more, when it is short.
- One big deal beats a drip-feed in both directions (750 for fifty at once against 635 one at a time). That is the
  price of "the price you see is the price you pay"; it is bounded by his purse on one side and his stock on the other,
  and it is recorded as a decision in DESIGN §8.

---

## 6. What the pure core tests check against this file

`Core/Catalogue` parses the config line; a test loads `docs/data/items-valheim-2026-07-31.tsv` and asserts every
default prefab exists there with a positive stack size, that no Ware's base is below its vanilla value / 0.7, that
every Want with a vanilla value of 0 has a base ≥ 1, and that `Max ≥ Target > 0` throughout. A catalogue edit that
misspells a prefab fails the test on the desk, not in someone's world.

## 7. The rotating shelf (2026-09-08; issue #56)

The owner's change of 2026-09-08: **the fixed Ware list goes away.** `Server.ShelfSize` (shipped 20) entries of the
WHOLE catalogue — the 27 of section 2 and the 74 of section 3 alike — are on sale at a time, and the shelf is
re-rolled every `Server.ShelfRotationGameDays` (shipped 2, Wu'barrk's read) game days. The `Kind` in the config
line keeps its meaning only while `ShelfSize` is `0`: then the shelf is fixed and sections 2 and 3 say exactly
what he sells and what he only buys, as they did before this date.

**What "on the shelf" means.** An entry on the shelf trades exactly as a Ware did: sold at the curve
(`PriceFor`), bought back at par at most under the Fair Market Act, drifting on `WareHalfLifeGameDays`. Every
other entry trades as a Want: bought only, drifting on `WantHalfLifeGameDays`. The market decides this in ONE
place, `Market.KindOf(item)`, and every price, drift, refusal, snapshot row and export row reads it; nothing reads
the catalogue's own kind directly any more. The snapshot the clients draw carries the effective kind, so the
terminal's two panes follow the shelf with no client change. A buy of an entry that is in the catalogue but not
on the shelf is refused with its own reason, `not_on_shelf` ("Not in this load, friend. Ask me again in a few
days."), so a pane that is a period stale says why rather than `unknown_item`.

**The roll** (`Core/Shelf.cs`, pure). World time — `ZNet.GetTimeSeconds()`, the same seconds the drift counts in,
which stop while the server is empty — is cut into periods of `ShelfRotationGameDays × SecondsPerGameDay`; the
period index and the world's salt seed an xorshift64* generator (FNV-1a of `salt#period`; NOT the engine's
`GetStableHashCode`, on purpose), a partial Fisher–Yates over the catalogue's order takes `ShelfSize` positions,
and the result is returned **in catalogue order**, so the pane never reorders under a player and two machines
list the same shelf the same way. Nothing is persisted and nothing is sent for the shelf: the same salt, clock
and catalogue roll the same shelf on every boot, which is why a restart mid-period shows the same twenty.
Because the swaps are sequential, a bigger shelf for the same period is a superset of the smaller one, so
raising `ShelfSize` mid-period adds entries and never swaps one out.

**When it rolls.** The director asks the market once a second whether a roll is due (the period moved, or an
admin changed `ShelfSize` or the rotation live) and rolls on the first tick with **no visit running** — the same
busy rule as a catalogue swap: not under a running visit, not while a saved visit waits for its event, not while
a merchant is still departing — logging `shelf roll waits: …` once and `shelf rolled: shelf 20 of 72, period N,
…: <the names>` when it happens, then republishing the market. `cargo status` carries the same line and says
when a roll is due and waiting. A catalogue edit re-rolls on the new pool for the same period (a price-only edit
keeps the same shelf; a dropped entry changes it).

**What stock does at the boundary.** An entry keeps its stock when it leaves or joins the shelf; only the rule
it trades under changes. So a Want that has been flooded to its max lands on the shelf with all of it for sale at
the flooded (low) price, and a Ware that leaves the shelf emptied drifts back on the Want knob. Those are the
open economy questions for the first live shelf, listed in the PR that built this and in `docs/ECONOMY-SIM.md`
section 11, which also carries the first fifteen shelves for the simulation's salt and thirty days of trade on
the rotating rules.

**The core tests** (`tests/CoreTests`, "Shelf.Roll" through "Market.EncodeState / ApplyState carry no shelf row"):
the roll is deterministic per salt and period, exactly `ShelfSize` distinct names in catalogue order, different
for the next period and for another world, a superset when the size grows, the whole pool when the size covers
it, empty for a size of 0 or no pool, never a throw; every entry is shown over 200 periods and none is starved
or favoured; the period arithmetic at the boundaries, negative and NaN times, a zero day; the knobs' clamps; the
fixed shelf (`ShelfSize 0`) behaving exactly as the market of the day before; on the rotating shelf exactly twenty
Ware rows in the snapshot, an off-shelf Ware trading as a Want and an on-shelf Want as a Ware, the `not_on_shelf`
refusal against an `unknown_item` for a stranger, the Fair Market Act clamp following the shelf, the drift knobs
following the shelf, the roll moving with the period, the size and the rotation, the switch-off, the catalogue
swap, and the sidecar rows carrying nothing for it.

**The backpack add-on (2026-09-08; Wu'barrk's design, the owner's decision on the mod).** A server running
Smoothbrain's Backpacks (BepInEx GUID `org.bepinex.plugins.backpacks`, or whatever `Server.BackpackModGuid`
names) sells from a bigger shelf: `ShelfSize × Server.BackpackShelfMultiplier` (shipped 2, range 1–4), because
players who can carry more can buy more in one visit. The lookup (`Server/BackpackMod.cs`) is one read of
BepInEx's chainloader on the server, made at director up — before the market is sized, so the shelf is built at
its scaled size and does not re-roll a tick later — and once a second after, so a live change of either knob
lands on the next idle tick like any other shelf change. The scaling is pure (`Shelf.Scaled`): `0` stays the
fixed shelf, the product is capped at `Shelf.MaxSize` (200) and, by the roll, at the catalogue, and the scaled
shelf for a period is a superset of the unscaled one (the swaps are sequential), so the mod arriving mid-period
swaps nothing out. On the shipped 101 entries ×4 is 80 of it and ×3 is 60. The other half of
the add-on — a backpack on Ingvar's body — is a bake, and Wu'barrk's. The log says what it found at director up
(`backpack mod: org.bepinex.plugins.backpacks 1.3.8 loaded; shelf x2 (Server.BackpackShelfMultiplier)` or
`none (…); shelf x1`) and `cargo status` repeats it.

---

## 8. Food and drink (2026-09-15)

The owner's ask: *"lets also add other food and such to the list of buy and sell."* Twenty-nine rows: nine he
sells (five dishes and four meads, section 2) and twenty he buys (forage, crops, seeds, raw meat and fish, section
3). Every base is the `value` column of `docs/data/item-values-2026-09-08.tsv`; every prefab and stack is checked
against `docs/data/items-valheim-2026-07-31.tsv` by the harness (section 6); targets follow section 1 (produce
50/150 like Honey, a dish 20/60 or 10/30 by its stack, a mead 10/30, raw meat 40/120 like the hides); `Max = 3 ×
Target` throughout. With `ShelfSize` 20 the `Kind` decides nothing (section 7); it is set honestly all the same.

The set was drafted at 39 and cut to 29 by the economy review (Opus, rule 2 of the working agreement). Its findings
are the reasons for what is NOT here:

- **`FishAnglerRaw`** shares the item token `$item_fish_raw` with `FishRaw`, and the deal applier counts and removes a
  player's goods by token - with both in the catalogue a stack of raw fish would sell at the anglerfish price. One
  of the pair ships, the common one. A guard against token collisions is its own task.
- **`ChickenEgg`** is Haldor's 1,500-coin gate to a hen farm; the value table's 6 is a hand value for pricing
  omelettes, not a shop price, and forty eggs at 6 on the shelf would delete the gate. Section 1: he matches Haldor,
  never beats him.
- **`LoxPie`** is a cook-and-sell-back loop with no assumption in it: four barley and two lox meat bought from him
  make a pie he pays 52 for - +28 a pie, 840 coins to fill the row, the second-largest row in the catalogue.
- **Yield.** The value table priced every cauldron craft as one plate, but a jerky craft is two and a sausage craft
  is four. `BoarJerky` ships at 3 (the table's 5 halved); **`Sausages`** (+46 a craft on entrails and boar meat he
  sells himself, if the yield is four) stays out until the yield is confirmed on a machine.
- **`MisthareSupreme`** and **`PiquantPie`** carry bases from an unresolved recipe (the resolver fell back to the raw
  rule on the words "Misthare meat" / "Piquant pie ingredients"), which priced an Ashlands pie at a fifth of a
  Plains one.
- **The cooked twins** (`CookedMeat`, `CookedWolfMeat`, `SerpentMeatCooked`) pay exactly what their raw pays, so
  cooking for Ingvar would earn nothing; the Mistlands and Ashlands cookfire meats had no raw side in the set.
- **Four meads** (`MeadHealthMajor`, `MeadHealthLingering`, `MeadStaminaLingering`, `MeadEitrMinor`): a brew is six
  meads off ten honey and forage, and a mead row is bought back UNCLAMPED whenever it is off the shelf (the Fair
  Market Act clamps only a shelf row); the eight proposed could absorb the whole 1,500 purse in one visit. Four
  staples ship, 472 coins to fill.
- Feasts (rare crafts, 63-536), the Bog Witch's own trader goods, every mead base, Bukeperries and rotten meat:
  never proposed.

Two facts for the next pass. `MushroomYellow`'s row in the value table is mislabelled (`name` reads "Thistle",
tier 0) and its base here is the raw rule for a tier-2 common (4 × 0.7 → 3). And food is now a third of the
catalogue - 34 of 101 with Honey, Barley and the three shipped meads - about seven of a twenty-row shelf, fourteen
of forty with the backpack mod. The owner's line to move; the next four to go would be `DeerMeat`, `ChickenMeat`,
`CarrotSeeds` and one of the two Meadows dishes.
