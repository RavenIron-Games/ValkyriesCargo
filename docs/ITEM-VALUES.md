# Item values — every Valheim item assessed for the walk-in trade (2026-09-08)

The owner's ask, 2026-09-08: *"run competitive full item spectrum vs valheim wiki. Assess rarity and complexity.
Assign value from 2-600, 800-1200 for ultra complex/rare."* This is that table: the price source for the
buy-anything extension in `docs/TODO.md` §3 (Ingvar buys ANY item a player offers; an uncatalogued sale forces a
walk-in row, common or rare, a common row living two visits without a sale). The table is
`docs/data/item-values-2026-09-08.tsv`; the assessors' full output behind it, with every note and wiki page, is
`docs/data/item-assessments-2026-09-08.tsv`; this file is how it was made, what the numbers mean, and what is
still a judgement call. Nothing in the mod reads it yet.

| | |
|---|---|
| rows | **776** (the game's 1083 prefabs less hair, beards and the unmistakable monster moves) |
| player items | **677**; 99 excluded with a reason (monster wear, monster weapons, dev and cheat items, the currency) |
| how the value was set | 72 catalogue anchors · 207 raw goods by tier and rarity · 320 crafted goods by their recipe's inputs · 37 trader goods by a quarter of the shop price · 25 ultra · 15 by score alone (no resolvable recipe) · 1 by hand |
| bands | 2–30: **395** · 31–100: **124** · 101–300: **69** · 301–600: **64** · ultra 800–1200: **25** |
| class | common 273 · rare 379 · ultra 25 |
| confidence | high 568 · medium 105 · low 4; 7 rows had no wiki page at all |

## 1. Where the items and the facts came from

- **The item list** is the game's own: `docs/data/items-valheim-2026-07-31.tsv`, 1083 prefabs read off `ObjectDB`
  on 0.221.12. Hair and beards (111 `Customization` rows) and the monster attack moves whose names say so
  (`_attack`, `_bite`, `_spit`, `Fader_Meteors` and the like; 195 rows) were dropped by a name filter before anyone
  looked. The remaining **777 candidates** each got a `player_item` verdict, so monster wear (`GoblinArmband`,
  `DvergerSuitFire`, `Charred_Helmet`), dev items (`SwordCheat`, `HealthUpgrade_*`, `CapeTest`) and the currency
  are IN the table with an `exclude_reason`, never silently gone. (One candidate, a duplicate prefab across two
  slices, collapsed to one row: 776.)
- **The facts** are the Valheim wiki's (`valheim.fandom.com`), pulled once on 2026-09-08 through its MediaWiki
  API with `curl` and `node` (`tools/item-values/crawl.js`): all 1027 articles, indexed by the infobox's `id`
  field (914 prefab ids), so **580 of the 777 candidates had their own page in front of the assessor by prefab
  id**; the rest were found by English name (trophies live on their creature's page, in the `drop row` table with
  the percentage; the drop rows and the trophy links were indexed too), and a few dozen were assessed from
  knowledge with `confidence low`. The `wiki_page` column names the page each row was read from. Nothing was
  fetched from the web by an agent; the fetch proxy refuses fandom (HTTP 402), which is why the corpus was pulled
  once.
- **The catalogue's 72 entries are anchors** (`docs/CATALOGUE.md`, the live `Catalogue.DefaultLine` in
  `Core/Catalogue.cs`, not the design tables): their value in this table IS their catalogue base, and every other
  number is calibrated against them.

## 2. What was assessed — three scores per item

Every row carries the facts (biome, source, station and level, the recipe line, drop rate, boss-gated, limited,
trader price) and three scores read off them:

| score | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| **tier** (0–8) | 0 anywhere (Wood, Stone) · 1 Meadows · 2 Black Forest · 3 Swamp and Ocean · 4 Mountains · 5 Plains · 6 Mistlands · 7 Ashlands · 8 Deep North; a crafted item's tier is its progression's (bronze 2, iron 3, silver 4, black metal 5, eitr 6, flametal 7) | | | | |
| **rarity** | abundant gather or guaranteed drop | common plant, drop or ore; anything freely craftable from its tier's materials (most gear and food) | uncommon drop 10–50 %, a limited spawn, a boss-gated material, a trader purchase | rare drop 1–10 %, one per nest or altar, the top pieces of a chain | very rare under 1 %, a boss trophy, a boss-unique item |
| **complexity** | raw gather or drop | one processing step: a smelted bar, cooked meat, a mead base, a one-or-two-ingredient workbench craft | two or three ingredient types at a station: a bronze axe, a stew, leather armor | a multi-step chain with refined inputs from two tiers, a station level of 3 or more, a fermented mead | the deepest chains: four or more refined inputs, refined eitr, Yggdrasil wood, flametal, artisan, galdr or black forge level 3 and up |

The point of the split: **a Black Forest sword is rarity 2 — anyone with copper and tin makes one — and its worth
is in its complexity**, while a Dragon egg is complexity 1 and its worth is in its rarity.

## 3. The value — four rules, in this order

1. **A catalogue entry keeps its catalogue base.** 72 rows, the `anchor` column.
2. **A raw good** (complexity 1: gathered, grown, dropped, fished) is `rawBase[tier] × rarity`:

   ```
   rawBase   tier 0: 2   1: 2.5   2: 4   3: 4.5   4: 5   5: 5.5   6: 6.5   7: 10   8: 14
   rarity    1: ×0.7   2: ×1.0   3: ×2.5   4: ×4.5   5: ×8
   ```

   The curve is FLAT on purpose. The catalogue prices a raw common at 1 to 10 whatever its biome (Wood 1, Troll
   hide 6, Lox pelt 8, Carapace 10, Asksvin hide 10) and puts the steepness in the metals, which are all anchored
   (ore 5 · 5 · 22 · 36 · 50 · 90, bar 6 · 6 · 15 · 25 · 40 · 60 · 110 from tin to flametal). A first draft with a
   steep tier curve priced Sap at 119 against its anchor of 6; the anchors, not the draft, won.
3. **A crafted good** (complexity 2 and up, with a recipe) is **the cost of its inputs, priced by this same
   table, recursively**, divided by the craft's yield, times a station markup, and — for equipment only —
   compressed:

   ```
   value = (Σ input value × count) / yield  [^0.8 for weapons, armor and gear of complexity 3+]  × markup
   yield    20 for arrows, bolts and nails · 5 for bombs · 4 for fireworks · 2.5 for a mead (a brew yields six,
            but the three anchored meads at 10–12 say the brew is worth more than base/6; 2.5 fits them) · 1 else
   markup   complexity 2: ×1.15   3: ×1.3   4: ×1.5   5: ×1.8
   ```

   The recipe is the wiki infobox's `materials` field, level 1 only (136 rows; the infobox runs on into the
   upgrade levels, so the parse stops at the first repeated ingredient), else the assessor's recipe line, else
   a hand-filled one for eleven well-known rows the assessors left empty (the bronze, fire, poison, needle,
   silver, obsidian, carapace and charred arrows and bolts, the cultivator, bread and dough, two shields whose
   assessed recipe was the upgrade total). 339 of 349 recipes resolve every ingredient to a prefab in this
   table. The compression exists because **with Iron at 25 a bar, every iron-and-up piece of equipment costs
   500 or more in inputs** and the 600 ceiling would have flattened the whole top of the table (111 rows sat at
   exactly 600 before it); `cost^0.8` keeps the order and spreads the band.
4. **Ultra, 800–1200**, in the owner's words "ultra complex/rare": the assessor's call, never a consumable, and for
   a crafted thing only with a rare input (rarity 4 and up); the seven boss trophies and the boss-unique drops
   (Wishbone, Dragon egg, Torn spirit, the Majestic carapace, Fader's drop, the three Dyrnwyn fragments,
   Megingjord) are rarity 5 and ultra whatever a slice said.

   ```
   ultra = 800 + 60 × (tier − 4) + 40 × (rarity − 4) + 40 × (complexity − 4), clamped 800..1200
   ```

Two floors on top: **a trader good** (Haldor, Hildir, the Bog Witch; 37 rows) is at least a quarter of its shop
price, because a 350-coin dress is not a mushroom and Ingvar is not a resale outlet either; and the one **hand
value**, the Egg at 6 — Haldor sells it for 1500 coins to start a farm, and a quarter of that would have priced
every omelette at the cap, while an egg once hens lay is a Plains common. Fifteen rows with no resolvable recipe
(section 6) fall back to `rawBase × rarity × complexity{1, 3, 8, 15, 30}`.

**Class**, the extension's common-or-rare filter (a proposal; the owner's line to move):

| class | rule | what it means for a walk-in row |
|---|---|---|
| `common` | value ≤ 30 and rarity ≤ 2 | rotates with the shelf; lives **2 visits without a sale** (owner, 2026-09-08) |
| `rare` | everything else under 600 | stays until sold, or on the extended timer |
| `ultra` | 800–1200 | as rare, and pinned on the shelf |

The gap between 600 and 800 is deliberate: nothing lands in it, so a value of 800 or more reads as ultra on
sight.

## 4. How it was run

Forty-one Haiku agents in two workflows (`item-values`, run `wf_9cb73df8-411`; `item-values-redo`, run
`wf_fd0211a9-850`), 3.7 million tokens, about half an hour of wall clock. Fourteen assessors, one per slice of
about 60 items, each reading its slice's wiki pages and scoring every row; fourteen skeptics, one per slice,
re-reading the same pages to refute wrong tiers, sources, drop rates and scores (256 corrections applied on top;
the commonest: a mead's recipe copied from a template, a feast scored rare for being craftable, a Plains set
scored Black Forest); one calibration pass over the merged table. **Three slices came back flat** — 56 and 62
material rows and 33 armor rows at "tier 0, anywhere" or one identical triple, with the pages named but not
read — which the first quality gate missed because it only checked that pages were named; a second gate on the
score distribution caught them, and the three slices were re-run in halves with a row-by-row procedure, a
self-check the agent had to report, and an automatic retry on a flat result (none was needed). The trophy
slice's assessor wrote no file and was recovered from the workflow journal. Every step is a script under
`tools/item-values/`: `crawl.js` (the wiki pull and the prefab index), `qc.js` (the two gates), `merge.js` (the
slices plus the corrections into one table), `cost.js` (the recipe resolution and the four rules), `snippets.js`
(this document's numbers). A new formula is a re-run of `cost.js`, not a re-assessment: the three scores and
the recipes are in the assessments file.

## 5. What the table says

| family | items | 2–30 | 31–100 | 101–300 | 301–600 | ultra | lowest | highest |
|---|---|---|---|---|---|---|---|---|
| material | 212 | 168 | 26 | 8 | 5 | 5 | Stone 1 | Majestic carapace 840 |
| consumable | 98 | 69 | 20 | 8 | 1 | 0 | Mushroom 2 | Mistlands gourmet platter 536 |
| weapons | 118 | 22 | 20 | 24 | 42 | 10 | Ooze bomb 2 | Primal Slayer 1020 |
| armor | 97 | 24 | 42 | 18 | 13 | 0 | Simple tunic 3 | Flametal helmet 600 |
| gear | 94 | 64 | 12 | 11 | 3 | 4 | Flinthead arrow 2 | Torn spirit 800 |
| trophy | 58 | 48 | 4 | 0 | 0 | 6 | Neck trophy 6 | Fader trophy 900 |

**The ladders, as they come out:**

- swords: Bronze 147 · Iron 191 · Silver 508 · Black metal 452 · Mistwalker 406 · Nidhögg 582 · Nidhögg the Bleeding 1020
- axes: Flint 12 · Bronze 64 · Iron 191 · Black metal 456 · Jotun Bane 343 · Berserkir 600
- chest pieces: Rags 17 · Leather 21 · Troll leather 35 · Bronze 44 · Iron 189 · Root 46 · Wolf 328 · Padded 199 · Carapace 224 · Eitr-weave 509 · Flametal 600
- bows: Crude 39 · Fine wood 83 · Huntsman 92 · Draugr Fang 294 · Spinesnap 237 · Ash Fang 390
- food: Cooked boar meat 2 · Sausages 22 · Serpent stew 18 · Bread 40 · Lox pie 74 · Misthare supreme 13 · Mistlands gourmet platter 536
- meads: Tasty 10 · Minor healing 12 · Medium healing 10 · Major healing 14 · Lingering eitr 52 · Berserk brew 12
- trophies: Boar 8 · Greydwarf 8 · Wolf 15 · Lox 14 · Seeker 16 · Morgen 45 · Eikthyr 800 · Fader 900
- raw goods across the biomes: Wood 1 · Resin 1 · Troll hide 6 · Guck 4 · Obsidian 4 · Lox pelt 8 · Sap 6 · Asksvin hide 10 · Celestial feather 25

**The ultra band, all 25:**

| prefab | item | tier | why | value |
|---|---|---|---|---|
| `SpearSplitner_Blood` / `_Lightning` / `_Nature` | Splitnir the Bleeding / the Storming / the Primal | 7 | infused Ashlands craft, a rare gem in it | 1020 |
| `SwordNiedhoggBlood` / `Lightning` / `Nature` | Nidhögg the Bleeding / the Thundering / the Primal | 7 | infused Ashlands craft | 1020 |
| `THSwordSlayerBlood` / `Lightning` / `Nature` | Brutal / Scourging / Primal Slayer | 7 | infused Ashlands craft | 1020 |
| `SwordDyrnwyn` | Dyrnwyn | 7 | the three fragments and flametal | 1020 |
| `TrophyFader` | Fader trophy | 7 | boss trophy | 900 |
| `DyrnwynBladeFragment` / `Hilt` / `Tip` | the Dyrnwyn fragments | 6 | boss-unique drop | 840 |
| `FaderDrop`, `QueenDrop` | Fader drop, Majestic carapace | 6 | boss-unique drop | 840 |
| `TrophyDragonQueen` | Moder trophy (the wiki's "The Queen trophy") | 6 | boss trophy | 840 |
| `BeltStrength`, `DragonEgg` | Megingjord, Dragon egg | 4 | Haldor's one unique; one per nest | 800 |
| `TrophyEikthyr`, `TrophyTheElder`, `TrophyBonemass`, `TrophyGoblinKing`, `TrophySeekerQueen` | the five other boss trophies | 1–6 | boss trophy | 800 |
| `Wishbone`, `YagluthDrop` | Wishbone, Torn spirit | 3, 5 | boss-unique drop | 800 |

**At the 600 cap, eleven rows**, all flametal or black-metal-heavy: `BattleaxeSkullSplittur`, `AtgeirBlackmetal`,
`BattleaxeBlackmetal`, `ShieldFlametal`, `ShieldFlametalTower`, `AxeBerzerkr`, `THSwordSlayer`,
`ArmorFlametalChest`, `ArmorFlametalLegs`, `ArmorMageLegs_Ashlands`, `HelmetFlametal`.

**The anchors against the model** (what the rules would have said had the anchor not been there): the bars land
within 1.4× (Bronze 21 vs 15, Iron 25 vs 25, Silver 41 vs 40, Black metal 57 vs 60, Flametal 103 vs 110), the
three meads within 1.5× (8, 12, 8 vs 10, 12, 12), and two are far off: Linen thread (3 vs 10 — the catalogue
prices the thread well above its flax) and Eitr (16 vs 45 — the catalogue treats refined eitr as a valuable, not
as sap plus soft tissue). Both keep their anchor; both say the catalogue's own numbers are the authority, which is
the point of rule 1.

## 6. What is still a judgement call

- **The compression exponent (0.8) and the band edges** (30 for common, 600 and 800 for ultra) are the owner's to
  move. Without the compression a bronze sword is 196 and an iron sword 763 (capped to 600); with it 147 and 191.
  The catalogue's metal prices also produce two orderings a player may find odd: a Silver sword (40 bars at 40)
  above a Black metal sword (20 bars at 60), and Carapace armor (Carapace anchored at 10) below Wolf armor.
- **The barley chain.** Bread is 40 and a Lox pie 74 because a loaf is ten flour is ten barley at the anchor's 3
  each; a cooked lox meat is 7. Either Barley's anchor is high for a crop, or bread is a luxury; the table says
  the latter.
- **Feasts.** The Mistlands gourmet platter at 536 is the most complex food in the game and prices as such; it is
  not ultra because nothing edible is. Whether Ingvar buys consumables at all is the extension's decision.
- **Fifteen rows priced by score alone**, with no resolvable recipe, worth a look before they are trusted:
  `CeramicPlate` 6, `BarleyWineBase` 41, `BarleyWine` 83, `FeastSwamps` 135, `FeastAshlands` 300,
  `FeastMistlands_Material` 195, `FeastAshlands_Material` 375, `KnifeWood` 6, `FishingRod` 20 (Haldor 350 → 88
  by the trader floor), `TankardAnniversary` 34, `BombLava` 75, `HelmetBerserkerUndead` 180, `Feaster` 6,
  `TorchMist` 20, `Demister` 244. Their recipes name things that are not items (`Clay`, `Water`, `Milk`,
  `Gelatin`, `Sealing Wax`, the feast placeholders) or nothing at all.
- **Trader goods** carry a quarter of their shop price and no more (Hildir's dresses 88, her hats 38–75, the
  Barber kit 150, the Dvergr circlet 155). A different fraction is one number in `cost.js`.
- **Four rows are confidence low** (Larva and three Deep North items the wiki had not caught up with) and seven
  had no wiki page; they are marked in the table.
- **Hand-filled recipes** (section 3, rule 3) are from memory of the game, not the wiki; the carapace and charred
  ammo quantities in particular are a best guess and are marked `recipe_from hand` in the assessments file.
