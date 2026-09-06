# Valkyrie's Cargo — TL;DR

## The design in one screen

**What it is.** A Raven Iron Valheim mod. A Valkyrie drops a merchant, Ingvar the Far-Travelled, beside your base at a
random moment. He walks up, calls out, trades for five minutes, and vanishes the way Odin does.

**When he comes.** You are rested, your comfort is 4 or more, you are at a base, it is daytime, and no raid is running.
The server rolls every 25 minutes at 25%. Never on command; `cargo visit` is admin-only. A summon horn comes later.

**How he arrives.** The vanilla intro Valkyrie, flown by the chosen player's client from about 90 m out (the game only
loads objects near a player, so 500 m is impossible). Ingvar hangs in the talons, drops the last 10 m, walks to you,
speaks. Everyone nearby sees all of it.

**What he does.** Buys and sells from a live stock that persists per world. Prices move with his stock in real time:
buy him out and it costs more, flood him and he pays less, and it drifts back over a game day. He carries a purse of
800 coins, so nobody can dump a warehouse on him. Coins or barter; barter is valued at his live buy price.

**Who decides what.** The server owns config (ServerSync, locked), the schedule, the event, the market, the clock, and
whether the bird and merchant exist. The chosen client simulates motion. Every client renders. Your inventory stays
yours, as vanilla, and is only touched after the server answers.

**The trade window.** Our own terminal, drawn with VikingOS's gilt theme compiled in as shared source. No dependency on
VikingOS, no patch on the vanilla store. Deals ride the direct server socket with a nonce and the price you saw; if
the price moved, the line turns amber and you confirm again. Goods owed to a player who dropped mid-deal are delivered
next login, never twice.

**Departure.** Five-minute event clock, or Shift+E twice. Odin's own vanish effect, once, on every screen.

**What ships in 0.1.** All of the above with the Dverger body. **Later:** the horn, Thorium's custom body, a barter
basket, rare rotating stock, localisation.

**Settled facts.** Valheim runs on Unity 6000.0.61f1, so bundles are built with that Editor. ServerSync broadcasts on
change only. Comfort never leaves the client, so the client reports it on its own ZDO. The delivered model is a statue
and needs retopology, a rig and clips before it can walk.

**Still open.** Reconfirm versus tear-down on a price tick (reconfirm is in). Where bundles get built.

Full text: `DESIGN.md`. Decisions table: its section 8. What to verify in-game: section 10.

---

## The catalogue in one screen

**Unit.** Coins. Ingvar charges `base × (target ÷ stock)^0.35`, clamped between 0.4× and 3×, and pays 70% of that.
At target stock he pays Haldor's flat rate for the four vanilla valuables (amber 5, pearl 10, ruby 20, necklace 30);
when he is short he pays more, when flooded less. Haldor never moves; that spread is why you walk to Ingvar.

**He sells and buys back (18 wares).** Bronze 15, iron 25, silver 40, black metal 60, flametal 110, refined eitr 45,
black core 300; amber 7, amber pearl 14, ruby 29, silver necklace 43; iron and frost arrows, iron bolts; three meads;
honey.

**He only buys (54 wants).** Wood family (wood 1 → yggdrasil wood 5), resin, coal, stone, flint, feathers; hides
(leather scraps 2 → ask and bjorn hide 10); flax, linen thread 10, barley, jute; **ores and scrap** (copper and tin ore 5,
scrap iron 22, silver ore 36, black metal scrap 50, flametal ore 90), none of which teleport, which is the point; swamp
and mistlands parts; eight common trophies.

**Stock.** Targets are two to four stacks for commons, half a stack for metals, a handful for rare goods; he refuses
above three times target ("I've all the linen a man can carry"). Stock drifts back to target with a one-game-day
half-life. Purse 800 plus half of last visit's takings.

**Where the numbers came from.** Wu'barrk's TheEye dump of every item field and all 365 recipes (2026-07-31). Every
prefab name was checked against it; recipe demand ranked the wants (wood 55 recipes, deer hide 53, flametal 42, iron
41). Watch out: core wood is `RoundLog`, the live flametal is `FlametalNew`, and there is no `Charcoal` or `Silk`.

**Safety.** An unknown name at boot is dropped with one log line. The off-game tests check the defaults against the
item table, so a typo fails on the desk.

Full tables with every number's reason: `CATALOGUE.md`. Item data: `data/items-valheim-2026-07-31.tsv`.
