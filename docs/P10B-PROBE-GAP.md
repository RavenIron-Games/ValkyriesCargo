# P10b probe gap: `CharacterAi` checks `Damage`, P5 patches `RPC_Damage`

Don — found this reading `origin/a/p10b-probes` alongside P5 (`origin/b/p5-merchant`) for the
engine-surface manifest. Not merged into `b/p10a-surface`, just read. One real gap, worth catching
before it ships quiet.

## What the probe checks

`ValkyriesCargo/EngineCheck.cs:324-334`, `CheckCharacter`:

```csharp
NeedMethod(c, "InIntro", Type.EmptyTypes, bad);                // P5's
NeedMethod(c, "Damage", new[] { typeof(HitData) }, bad);       // P5's
```

`c` is `typeof(Character)`. This resolves `Character.Damage(HitData)` — public, and it exists today
and will keep existing for as long as vanilla keeps a public damage-sender API, which is a much
weaker promise than the one the merchant's immortality actually depends on.

The same wrong name is in the probe's own description, `ValkyriesCargo/Core/EngineProbes.cs:125-126`:

```csharp
Declare(CharacterAi, 6, "Character.GetAllCharacters / GetSEMan / InIntro / Damage, MonsterAI.MakeTame, BaseAI.IsEnemy",
                        "nothing today; P5's surface, audited when P5 lands (P11d)");
```

`cargo engine` prints that string verbatim, so a person reading the probe's own report is told the
same wrong thing the check itself does.

## What the code actually patches

`origin/b/p5-merchant:ValkyriesCargo/Patches/Patch_Character_Damage.cs:22`:

```csharp
[HarmonyPatch(typeof(Character), "RPC_Damage")]
public static class Patch_Character_RPC_Damage
```

A prefix on the **private** `Character.RPC_Damage(long, HitData)`, not on `Character.Damage`. The
file's own header comment says why, and cites the same correction CLAUDE.md already carries
(`CLAUDE.md:326-332`, "Two corrections", item 2): `Character.Damage` is a thin sender — it runs on
the attacker's machine, computes a weak-spot index, and calls `InvokeRPC("RPC_Damage", hit)`. No
damage math happens in it. The real gate is in `RPC_Damage`, which runs on every machine that has
the victim instanced, `m_nview.IsOwner()` partway down. Cancelling on `Damage` would only stop hits
whose attacker is running this patch; the merchant's immortality has to live on `RPC_Damage`, and
P5's code already gets this right — this file just called it `Patch_Character_Damage.cs`, kept the
old name after fixing the target.

## Why the probe cannot catch the real breakage

`Character.Damage` and `Character.RPC_Damage` are two different methods with no relationship the
probe checks for. If a future Valheim build renames, removes, or changes the signature of
`RPC_Damage` — the private method P5's patch actually targets — `Character.Damage`'s public
signature can stay exactly as it is. `CheckCharacter` would keep resolving `Damage(HitData)`
without incident, `Record(EngineProbes.CharacterAi, ...)` would report no failures, `cargo engine`
and `cargo status` would both say the probe passed. Meanwhile `[HarmonyPatch(typeof(Character),
"RPC_Damage")]` fails to find its target, Harmony either throws or silently no-ops depending on how
that failure is handled upstream of this file, and the merchant becomes damageable with nothing in
the log or the probe report saying so. That is exactly the shape of failure P10b exists to catch —
a probe that is green while the thing it stands for is broken — and it is the same class of mistake
CLAUDE.md's two corrections already paid for once on this side of the repo, so it seemed worth a
note rather than a silent fix on a branch you haven't seen yet.

## The corrected probe

Both hunks below are a mechanically generated `diff -u`, not hand-typed, against
`origin/a/p10b-probes`'s own two files:

```diff
--- a/ValkyriesCargo/Core/EngineProbes.cs
+++ b/ValkyriesCargo/Core/EngineProbes.cs
@@ -122,7 +122,7 @@
                                       "the glide on every screen but the pilot's (CargoFlight)");
             Declare(ValkyrieFields, 5, "Valkyrie.m_attachPoint / m_attachOffset / m_dropHeight / m_speed / m_turnRate",
                                        "the carry point and the drop height (CargoFlight, P5's carry)");
-            Declare(CharacterAi, 6, "Character.GetAllCharacters / GetSEMan / InIntro / Damage, MonsterAI.MakeTame, BaseAI.IsEnemy",
+            Declare(CharacterAi, 6, "Character.GetAllCharacters / GetSEMan / InIntro / RPC_Damage, MonsterAI.MakeTame, BaseAI.IsEnemy",
                                     "nothing today; P5's surface, audited when P5 lands (P11d)");
             Declare(InventoryOps, 7, "Inventory.RemoveItem(string,int,int,bool) / AddItem(GameObject,int) / CanAddItem(GameObject,int) / CountItems, ObjectDB.GetItemPrefab",
                                      "a delivery cannot be applied (DealApplier)");

--- a/ValkyriesCargo/EngineCheck.cs
+++ b/ValkyriesCargo/EngineCheck.cs
@@ -327,7 +327,9 @@
             NeedMethod(c, "GetAllCharacters", Type.EmptyTypes, bad);       // used today, by CargoFlight
             NeedMethod(c, "GetSEMan", Type.EmptyTypes, bad);               // used today, by ComfortReporter
             NeedMethod(c, "InIntro", Type.EmptyTypes, bad);                // P5's
-            NeedMethod(c, "Damage", new[] { typeof(HitData) }, bad);       // P5's
+            // P5's Patch_Character_Damage patches RPC_Damage (private), not Damage (a thin RPC
+            // sender that never runs the actual gate) - CLAUDE.md "Two corrections", item 2.
+            NeedMethod(c, "RPC_Damage", new[] { typeof(long), typeof(HitData) }, bad);
             NeedMethod(typeof(MonsterAI), "MakeTame", Type.EmptyTypes, bad);
             NeedMethod(typeof(BaseAI), "IsEnemy", new[] { typeof(Character) }, bad);
             return 6;
```

`NeedMethod`'s `Anywhere` `BindingFlags` (`EngineCheck.cs:535-536`) already include `NonPublic`, so
the only change needed is which name and signature it asks for — `RPC_Damage` takes `(long sender,
HitData hit)`, confirmed against the real assembly (`ilspycmd -t Character`, `assembly_valheim.dll`,
installed client, 2026-09-07). Not run against `origin/a/p10b-probes`'s own test suite — that branch
was read, not built, on this side.
