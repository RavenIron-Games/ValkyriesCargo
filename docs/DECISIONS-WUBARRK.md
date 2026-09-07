# Track B's decision record — Wu'barrk's overrides

Decisions taken by **Thorium Wu'barrk** (Track B) that settle an open question, or that override
something already written down and shipped. Each one names what it overrode, why, and what it costs,
because a decision without its reason is re-litigated by the next person to read the code.

The full locked-decisions table stays in `CLAUDE.md` and `docs/DESIGN.md` section 8; this file is the
narrative behind the rows that changed, and the place to look when one of them seems arbitrary.
Track A's decisions are recorded by the owner in those two documents and in the handoffs.

---

## 1. `VCargo_` replaces `vc_` on every ZDO key and RPC name

**2026-09-07. Issue #16, PR #19. Overrides the `vc_` prefix shipped through P1-P8.**

`vc_` is two letters and it is not ours. The mechanics are worse than the aesthetics:

- **`ZRpc.Register` replaces by name.** A second mod registering `vc_deal` does not collide, does not
  warn and does not throw — it silently swaps our handler out, and the first anyone knows is a trade
  that does nothing.
- **A ZDO key collision does not throw either.** `GetInt` on another mod's `vc_state` returns that
  mod's number, plausibly shaped, and the merchant acts on it.

Found while auditing, not while debugging — which is the only cheap time to find it.

**The evidence arrived mid-job and is worth recording**: `docs/knowledge-base/IMPLEMENTATIONS/Fatty.md`
notes that **Valheim Cuisine ships around sixty prefab names under the `VC_` prefix**. Not our exact
keys, and its file was left untouched — but that is the collision this decision was taken on
speculation about, already shipping, sitting in our own documentation. `VCargo_` clears it.

Twenty-one names moved. Two of them were in files the issue's own inventory missed, and one was
*mid-string* (`"...vc_rested/vc_comfort)"`), which a prefix-anchored grep never sees — so the rename
was gated on `grep -rn '"vc_'`, unanchored, rather than on an inventory.

**Also decided, as scope:** the names are now centralised in `ValkyriesCargo/Core/Keys.cs`, and the
literals live in exactly one file. The cost is one more indirection when reading a patch; the return
is that the next rename is a single edit and that a typo is a compile error rather than a silent
miss.

## 2. The Fair Market Act

**2026-09-07. Closes "The round trip — OPEN" in the locked-decisions table.**

`MaxPriceMultiplier` 3.0 x `SpreadBuy` 0.7 = 2.1, and 2.1 > 1, so Ingvar buys his own wares back for
more than he sold them. Buy a shelf out at the rising price, sell it straight back at the empty-shelf
price, and his purse pays for the difference — 800 coins on the first visit, with the shelf left
exactly where it started, so **nothing in the saved state shows it happened.** It works on 17 of the
18 Wares (`docs/ECONOMY-SIM.md` section 9).

`docs/ECONOMY-SIM.md` offered two fixes. **Taken: the code fix, not the config fix.**

- The config-only fix is `MaxPriceMultiplier` 3.0 -> 1.4, since `1 / SpreadBuy` = 1.43 is break-even.
  It works, and it throws away most of the scarcity signal the mod exists for. Rejected on those
  grounds: supply and demand is the feature, not a side effect.
- **Taken:** one clause in what he pays. When the row is a **Ware** — something he himself sells —
  the buy-back multiplier is clamped at 1.0, so he never pays more than `base x spread` for it.

What deliberately does **not** change: what he *charges* still rises to the full 3.0x, and **`Want`
rows are untouched** — a Want's scarcity is the whole of its price and there is no round trip to
exploit, because he does not sell them. A player who empties a shelf and then repents still gets
their coins back at the ordinary rate. Only the pump dies.

It is a config knob, synced and locked, defaulting on. An owner who wants the old behaviour on a
private server can have it; nobody gets it by accident.

## 3. Newtonsoft.Json is adopted as a declared dependency

**2026-09-07. Overrides this mod's implicit "BepInEx and Harmony only" posture.**

Valheim ships no JSON library worth the name — `valheim_Data/Managed` carries only
`UnityEngine.JSONSerializeModule` (that is `JsonUtility`: no dictionaries, no top-level arrays, no
polymorphism), which cannot express a 72-row keyed market at all.

So: compile-time `<Reference>` against `libs-Tools\Newtonsoft.Json.dll`, runtime copy supplied by the
Thunderstore package `ValheimModding-JsonDotNET`, declared as a dependency. This is the pattern
Fatty, Runic, BlightedHeart, TortalPortal and BarrkUI already use in this workspace. We do **not**
ship our own copy beside the plugin, which is the other family pattern (TheEye, DvergrAllies): two
Newtonsoft versions in one `BepInEx/plugins` tree is a known way to break a server.

**This does not reopen Jotunn.** The dependency surface becomes BepInEx, Harmony and JsonDotNET, and
stops there.

## 4. The world sidecar stays `.dat`. JSON is a mirror, never the source of truth

**2026-09-07. Overrides the wider "JSON everywhere" option considered alongside decision 3.**

`Core/Sidecar.cs` is pure, line-based, mutation-proven, and headless-verified on a live dedicated
server with its `.tmp` / `.bak` / `.corrupt` rotation exercised. It has **zero third-party
dependencies on the save path**, and that is the property being protected: if `ValheimModding-JsonDotNET`
is missing, or another mod ships a conflicting Newtonsoft, a server must still be able to save its
market. A readable save is worth a lot; it is not worth that.

So the `.dat` file remains authoritative and a JSON mirror is written beside it for tooling and for
BarrkBOT. The ordering is part of the decision and is enforced in code: **the `.dat` write succeeds
first, and a throw in the mirror can never take the save or the visit down with it.**

## 5. Deals stay on the existing wire. The JSON work is export-only

**2026-09-07.**

"JSON for deals" was read two ways and only one was taken. The **deal ledger is exported** — who
bought what, when, at what price — because that is what makes Ingvar answerable in Discord. The
**deal wire is not re-encoded**: `VCargo_deal` keeps its compact tab-separated payload.

Three reasons, in order of weight: the wire format is PR #1's contract *between the two tracks* and
changing it is not one track's call; it is a hot path and JSON is fatter; and deserialising
client-supplied JSON is fresh attack surface opened in the same week we closed an unbounded-trust
hole on the drop point (decision 6). If it is wanted later it goes to Don as its own proposal.

## 6. The drop point is bounded, and the bound refuses NaN

**2026-09-07. PR #18. Implements P11's finding, with one deliberate departure from the diff Don wrote.**

`docs/TRUST-BOUNDARY.md` found the one place a client's ZDO write moved server state unbounded — the
pilot owns the bird, so `VCargo_target` is a value a client writes, and `Spawner.Tick` handed it
straight to the visit. Don wrote the fix out as a diff and marked it Track B's file. It is
implemented as written, with two changes:

- **The decision moved into `Core/FlightPlan.DropAccepted`** so the off-game harness can prove it.
  A bound that cannot be mutation-tested is a comment.
- **It is spelled `!(d <= tolerance)`, not `d > tolerance`.** Every comparison against NaN is false,
  so the natural spelling *accepts* a forged NaN and writes it into the session row, the wire and the
  sidecar. Proven: that mutation fails three checks. A float is three keystrokes to forge.

## 7. Considered and declined: committing the baked bundle

**2026-09-07. Recorded because it was nearly overridden and should not be revisited by accident.**

`Assets/valkyriescargo_kit` is gitignored, and the ignore rule carries the owner's reasoning:
WORKSPLIT section 4 allows exactly two binaries in this repo and they are the body's *source art*,
not its build output. Since only one machine could bake, that looked like a release blocker worth
overriding.

It is not. `tools/package.ps1` already prints loudly when a package is shipping the stand-in body, so
the silent-failure case the rule appears to create does not exist. And as of 2026-09-07 the bake runs
on both machines (`tools/setup-ingvar-unity.sh` is the Linux twin of the `.ps1`), so the constraint
costs one command before packaging. **The owner's rule stands. Bake, then package.**
