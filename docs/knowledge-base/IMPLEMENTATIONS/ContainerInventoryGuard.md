# The Container Inventory Guard — a chest's Inventory is a cache, and writing through it stale destroys other people's items

**Any mod that writes to a `Container` it does not exclusively own — automation depositing into
chests, withdrawing from them, or resizing them — WILL eventually erase a player's freshly
deposited items unless it reloads the inventory from the ZDO before every write.** The loss is
silent: no error, no log line, the items simply aren't there the next time the chest is opened.

Root-caused and fixed in LetItGrow 0.1.1 (2026-08-27) after a live field report: a player put
70 Barley into a Farm Silo, walked to the Scarecrow (whose crop picker — reading the same stale
data — didn't offer barley), walked back, and the stack was gone. Companion to fact 24 in
`JOTUNN-AND-HEADLESS-AUTOMATION-FACTS.md` and the second half of fact 20's claim-at-write-time
discipline. Verified against the live Valheim build as of **August 2026** on the publicized
`assembly_valheim`.

---

## The mechanism

`Container.GetInventory()` returns the component's **in-memory `Inventory` object — a cache** of
the ZDO's persisted `"items"` payload. Vanilla refreshes that cache only on its own paths (the
chest-open handshake, the owner's update tick), by exactly this gate:

```csharp
// vanilla Container.CheckForChanges(), paraphrased from the decompile
if (m_lastRevision != m_nview.GetZDO().DataRevision)
{
    Load();   // deserialize "items" from the ZDO into m_inventory, stamp m_lastRevision
}
```

Meanwhile, **only the ZDO owner's inventory saves replicate** (`Inventory.Changed()` →
`Container.OnContainerChanged()` → `if (IsOwner()) Save()`). Put those two facts together and
every mixed player/automation write pattern has two failure modes:

1. **The clobber (items destroyed).** A player deposits into a chest — their client owns it while
   browsing (vanilla's `RPC_RequestOpen` grant hands them the ZDO) and saves the new payload.
   The server-side automation later claims the chest (fact 20 says claim-at-write-time, and that
   part is right) and calls `AddItem`/`RemoveItem` **on its own stale cache**. `Changed()` → it
   is now the owner → `Save()` → the ZDO holds the automation's *pre-deposit* snapshot plus one
   change. The player's deposit is gone, persistently, with zero errors anywhere.
2. **The resurrection (items duplicated / debits undone).** Automation mutates a chest *without*
   claiming: the change lives only in that machine's RAM, never replicates, and the "removed"
   items come back on the next reload — while whatever the removal paid for (planted crops)
   remains. Watched-farm seed withdrawals did exactly this.

Both wear innocent faces in the field: "items disappear from the silo", "the picker doesn't see
what I just deposited", "seed counts never go down while I watch".

## The guard

One helper, called before **every** read or write of a shared container (all members publicized):

```csharp
private static Inventory FreshInventory(FarmSilo silo)
{
    Container container = silo.Container;
    ZNetView view = container.m_nview;
    if (view != null && view.IsValid()
        && container.m_lastRevision != view.GetZDO().DataRevision)
    {
        container.Load();   // vanilla's own CheckForChanges gate - free when nothing moved
    }
    return container.GetInventory();
}
```

And the full mutation ritual, every time, no exceptions:

```
reload (the guard above)
→ skip if ZDOVars.s_inUse == 1   (a player is browsing - never yank a chest from under them)
→ ClaimOwnership() if not owner  (or the write will not replicate - failure mode 2)
→ mutate (AddItem / RemoveItem / resize) - Changed() now saves the TRUE latest state
```

Port sources in `Let It Grow`: `Farming/ScarecrowController.cs` (`FreshInventory`,
`CountInSilos`, `RemoveFromSilos`, `DepositIntoSilos`), `Farming/FarmSilo.cs`
(`ApplyConfiguredSize` reloads before resizing), and the same pattern client-side in
`Farming/GridPlacementController.cs` (`Confirm`'s chest seed-source: reload → refuse an in-use
chest with a message → claim → debit).

## Points that will bite you if skipped

- **Reads need the guard too, not just writes.** Stale reads are how "the picker doesn't show my
  deposit" happens, and how a `CanAddItem` capacity check lies. Counting stock server-side for
  any UI must go through the guard.
- **Reload BEFORE the in-use check and claim, in the same tick as the write.** The revision gate
  is cheap (an int compare) — there is no reason to cache its result across ticks, and every tick
  of distance between reload and write reopens the race.
- **The residual in-flight window is vanilla's own.** A client's final save can still be on the
  wire when the automation claims. The `s_inUse` skip removes the browsing case, and a closing
  client saves at close time, so by the next automation tick (seconds later) the revision has
  landed. This guard closes the systematic clobber; the remaining window is the same one vanilla
  chest handover lives with.
- **Do not "fix" this by pinning ownership of the chest** — that breaks vanilla's chest-open
  handshake outright (players can't open it; see fact 20 / ZoneAnchor.md: never pin a Container).

## Verification recipe (the exact field repro)

1. Automation running against a chest. Deposit a distinctive stack as a player.
2. Without the guard: walk away, let the automation write once, reopen — your stack is gone
   (and any server-side stock listing never saw it).
3. With the guard: the stack survives automation writes, appears in stock listings immediately,
   and watched withdrawals actually persist (counts go down and stay down).
4. Conservation check over time: run the automation loop unattended and diff total stock —
   nothing may shrink except explicit debits. (LetItGrow's telemetry export made this a
   3-minute-interval automated check: harvest/plant/sweep totals strictly climbing, stocks
   net-positive across every farm.)
