# Vanilla API — coordinator-verified, 2026-08-01

Checked directly against `libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`
(144,161 lines, client build). **These line numbers are this file's and supersede the ones quoted in
`PLAN-v2-implementation.md` / `REVIEW-verified-facts.md`, which run ~3 lines earlier** — the code
bodies are identical, only the offsets drifted. Where this file and a design document disagree about
a *signature*, this file wins.

## Confirmed exactly as the facts file describes

| Member | Line | Shape |
|---|---|---|
| `BaseAI.BaseAIInstances` | 4013 | `public static List<BaseAI> BaseAIInstances { get; }` — add L4067 (Awake), remove L4073 (OnDestroy) |
| `ParticleMist.IsMistBlocked` | 32579 | `public static bool IsMistBlocked(Vector3 p0, Vector3 p1)` |
| `Player.GetAllPlayers` | 20432 | `public static List<Player> GetAllPlayers()` |
| `Player.GetPlayerID` | 15975 | `public long GetPlayerID()` |
| `BaseAI.m_mistVision` | 3851 | `public bool m_mistVision` (instance) |
| `BaseAI.Alert()` | 5330 | `public void Alert()` |
| `BaseAI.SetAlerted(bool)` | 5353 | `protected virtual void SetAlerted(bool alert)` |
| `BaseAI.IsAlerted()` | 5451 | `public bool IsAlerted()` |
| `Character.m_onDamaged` | 6878 | `public Action<float, Character> m_onDamaged` |
| `MonsterAI.m_targetCreature` | 5730 | `private Character m_targetCreature` — **declared on MonsterAI, not BaseAI** (confirms facts §1.5) |
| `ZDOVars` | 66218 | `public static class`, fields are `public static readonly int s_*` — the reflection sweep in `SoMKeys.AssertNoVanillaCollision` is valid |

## Corrections — the plan or a design document gets these wrong

### 1. `BaseAI.m_viewBlockMask` is `private **static**`, not an instance field

```
L3954:  private static int m_viewBlockMask = 0;
L4027:  m_viewBlockMask = LayerMask.GetMask("Default", "static_solid", "Default_small",
                                            "piece", "terrain", "viewblock", "vehicle");
```

Consequences:
- Under the publicized assembly it reads directly as `BaseAI.m_viewBlockMask` — **no live instance
  is needed**, contrary to how the bind package was briefed. Lazy resolution is still required, but
  for the *timing* reason only: it is 0 until the first `BaseAI.Awake` runs (L4027).
- **v1's hardcoded fallback mask in `RaycastUtils` is byte-identical to vanilla's own initializer.**
  So the two derivations agree by construction, and the "refuse on disagreement" check will only
  ever fire if a future Valheim build changes the layer set — which is exactly the Valheim-v1.0
  early-warning this layer exists to provide. Keep the check; note in the comment that agreement is
  expected today and disagreement is the signal.

### 2. `ZNet.GetAllCharacterZDOS()` is an **instance** method

```
L68491: public List<ZDO> GetAllCharacterZDOS()
```
Call it as `ZNet.instance.GetAllCharacterZDOS()`. It is not static. It also allocates and returns a
`List<ZDO>` — never call it per-tick.

### 3. `Character.GetStealthFactor()` is `virtual` and the base returns `1f`

```
L10092: public virtual float GetStealthFactor() { return 1f; }
```
This is the *method*, and it is not what cross-cutting rule 8 is about. Rule 8 concerns reading a
stealth value off a ZDO, where the vanilla default is 0. The direction of "safe" is confirmed by how
vanilla consumes the factor:

```
L4605-4611:  float stealthFactor = target.GetStealthFactor();
             float num2 = viewRange * stealthFactor;
             if (num > num2) return false;
```
It **scales view range**. So `0f` collapses the range to zero — invisible, i.e. *less* perception —
and `1f` is full range, i.e. *more*. Rule 8's "default to 0f, never 1f" is correct; keep it.

## The one that matters most — vanilla `CanSeeTarget`, verbatim

Static overload, L4596-4625. This is the method the whole sensing package must agree with:

```csharp
float num = Vector3.Distance(target.transform.position, me.position);
if (num > viewRange) return false;
_ = num / viewRange;                                   // dead store in vanilla
float stealthFactor = target.GetStealthFactor();
float num2 = viewRange * stealthFactor;
if (num > num2) return false;
if (!alerted && Vector3.Angle(target.transform.position - me.position, me.forward) > viewAngle)
    return false;                                       // <-- the ONLY use of `alerted`
Vector3 vector = (target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position);
Vector3 vector2 = vector - eyePoint;
if (Physics.Raycast(eyePoint, vector2.normalized, vector2.magnitude, m_viewBlockMask))
    return false;                                       // <-- UNCONDITIONAL
if (!mistVision && ParticleMist.IsMistBlocked(eyePoint, vector))
    return false;                                       // <-- the mist gate, also unconditional
return true;
```

Three cross-cutting rules read straight off this body:
- **Rule 1** — `alerted` gates the FOV cone and nothing else. The raycast is unconditional.
- **Rule 2** — the mist gate runs after the raycast, gated only on `m_mistVision`.
- **WP-4 endpoints** — `target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position`,
  cast *from* `eyePoint`, and the mist check uses the *same* two points as the raycast.

## `BaseAI.Alert()` routes itself — WP-7 leans on this

```csharp
public void Alert() {
    if (m_nview.IsValid() && !IsAlerted()) {
        if (m_nview.IsOwner()) SetAlerted(alert: true);
        else                   m_nview.InvokeRPC("Alert");
    }
}
```
Calling `Alert()` on a creature this peer does **not** own is safe and correct — vanilla forwards it
to the owner by RPC. That removes the main objection to WP-7's central decision (delete the
`IsAlerted` patch, drive the real `Alert()`): the call site does not have to own the creature, and
the alert replicates the way vanilla intends rather than being invented per-client.
