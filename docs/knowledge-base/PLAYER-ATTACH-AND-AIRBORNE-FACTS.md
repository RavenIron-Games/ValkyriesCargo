# Player attach state — why "is this player airborne?" is harder than it looks

Verified 2026-09-01 against Wings of the Valkyrie 2.1.2, which deployed its wings every time a
player took a ship's rudder.

## The trap

The obvious airborne test is some combination of these, and **all four pass while a player is
steering a boat**:

```csharp
!player.IsOnGround() && !player.IsSwimming() && !player.InWater()
    && player.GetStandingOnShip() == null      // <- see below, decides almost nothing
```

Taking the rudder **attaches** the player. An attached helmsman is:

- **not on the ground** — the attach lifts them off it, so `IsOnGround()` is false;
- not swimming, not in water;
- **not "standing on" anything** — `Character.GetStandingOnShip()` opens with
  `if (!IsOnGround()) return null`, so it can only ever be non-null when `IsOnGround()` has
  already matched. As an independent ship test it is worthless.

So a mod that gates flight, fall damage, gliding or any "in the air" behaviour on that set will
fire it at the helm.

## The test that works

```csharp
player.IsAttachedToShip()      // Player override, 21330: m_attached && m_attachedToShip
```

`ShipControlls` calls `AttachStart(..., onShip: true, ...)` (122365), and `Chair` passes its own
`m_inShip` (102030) — so this one flag covers **both the rudder and ship chairs**, which is what
you want. `Player.GetControlledShip()` (21418) is the narrower question ("is this player steering
*right now*") and returns the `Ship` via the doodad controller; use it when you need the vessel
rather than the boolean.

`player.InNumShipVolumes > 0` is a third, looser test — true anywhere inside a ship's volume
trigger, which is what survives a swell dropping the deck out from under someone. Use it for
wave-bob false positives, not for the attach case.

## Rule

Any "is the player airborne" check needs **all** of: not grounded, not in/under water, not
attached to a ship, and — if a fast fall can trigger it — not inside a ship volume. Leaving out
the attach test is invisible on land and in singleplayer testing, and shows up the first time
somebody takes a helm.
