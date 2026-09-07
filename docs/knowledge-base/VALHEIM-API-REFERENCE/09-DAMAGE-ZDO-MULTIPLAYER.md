All citations: `C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` unless stated.
Class anchors: `BaseAI`:3809, `Character`:6814, `Humanoid`:12806, `Player`:15320, `SEMan`:24219, `StatusEffect`:26393, `ZDO`:62124, `ZDOVars`:66396, `ZNetView`:70199, `HitData`:112132.

---

## 1. `Character.Damage(HitData)` — it is a *thin RPC sender*

```csharp
// :8692
public void Damage(HitData hit)
{
    if (m_nview.IsValid())
    {
        hit.m_weakSpot = FindWeakSpotIndex(hit.m_hitCollider);
        m_nview.InvokeRPC("RPC_Damage", hit);
    }
}
```
`public`, instance, non-virtual. It is the `IDestructible.Damage` implementation (`Character : MonoBehaviour, IDestructible, Hoverable, IWaterInteractable, IMonoUpdater`, :6814). **No damage math happens here.** `InvokeRPC(string, params object[])` (:70518) routes to the ZDO **owner only** — so this method runs on the *attacker's* client, and the payload is serialized (`HitData.Serialize` :112826).

### `Character.RPC_Damage` — where everything actually happens
`private void RPC_Damage(long sender, HitData hit)` — **:8701**. Registered in `Character.Awake`: `m_nview.Register<HitData>("RPC_Damage", RPC_Damage);` (**:7347**).

**The ":8707" local-player gate the plan cites is inside `RPC_Damage`, not `Damage`:**

```csharp
// :8701
private void RPC_Damage(long sender, HitData hit)
{
    if (IsDebugFlying())                                   // :8703
    {
        return;
    }
    if (hit.GetAttacker() == Player.m_localPlayer)         // :8707  <-- LOCAL-PLAYER GATE
    {
        Game.instance.IncrementPlayerStat(IsPlayer() ? PlayerStatType.PlayerHits : PlayerStatType.EnemyHits);
        m_localPlayerHasHit = true;                        // :8710
    }
    if (!m_nview.IsOwner())                                // :8712  <-- OWNERSHIP GATE
    {
        return;
    }
    Character attacker = hit.GetAttacker();                // :8716
    ...
}
```

Copy-paste ownership check for a Harmony postfix on `RPC_Damage` (or on `ApplyDamage`):
```csharp
// victim-side, owner-only:
if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
// attacker-is-me check:
if (hit.GetAttacker() == Player.m_localPlayer) { /* my hit */ }
```
`Character.m_nview` is `protected ZNetView m_nview;` (**:7047**) — reachable only because you publicize. There is **no** `Character.GetZNetView()` (NOT FOUND); there *is* `Character.IsOwner()` :9698 and `Character.GetZDOID()` :9689.

### Body outline of `RPC_Damage` (:8701–8802), in order
| Line | Step |
|---|---|
| 8703 | `IsDebugFlying()` early-out |
| **8707** | local-player attacker stat / `m_localPlayerHasHit` (runs on **every** client that sees the RPC — before the owner gate) |
| **8712** | `if (!m_nview.IsOwner()) return;` — everything below is **owner-only** |
| 8717–8720 | `Stagger(hit.m_dir)` if `hit.m_staggerMultiplier >= 100f` |
| 8721–8724 | Bail-out: dead / teleporting / cutscene / `hit.m_dodgeable && IsDodgeInvincible()` / `hit.HaveAttacker() && attacker == null` / PVP-off player-vs-player unless `hit.m_ignorePVP` |
| 8725–8730 | If attacker is non-player: `hit.ApplyModifier(Game.instance.GetDifficultyDamageScalePlayer(pos))`, then `hit.ApplyModifier(Game.m_enemyDamageRate)` |
| 8731 | `m_seman.OnDamaged(hit, attacker)` (SEMan :24745) |
| 8732–8735 | `BaseAI.AggravateAllInArea(pos, 20f, BaseAI.AggravatedReason.Damage)` |
| 8736–8741 | Backstab: `hit.ApplyModifier(hit.m_backstabBonus)` + `m_backstabHitEffects` |
| 8742–8746 | Crit on staggering non-player: `hit.ApplyModifier(2f)` |
| 8747–8754 | `BlockAttack(hit, attacker)` if `hit.m_blockable && IsBlocking()`, else `AddAdrenaline(player.m_nonBlockDamageAdrenaline)` |
| 8755 | `ApplyPushback(hit)` |
| 8756–8772 | `hit.m_statusEffectHash != 0` → `m_seman.GetStatusEffect(hash)` / `m_seman.AddStatusEffect(hash, false, hit.m_itemLevel, hit.m_skillLevel)`, `statusEffect.SetAttacker(attacker)` |
| 8773–8779 | weak spot + `GetDamageModifiers(weakSpot)` + `hit.ApplyResistance(mods, out significantModifier)` |
| 8780–8789 | `if (IsPlayer()) { hit.ApplyArmor(GetBodyArmor()); DamageArmorDurability(hit); }` else `hit.ApplyArmor(Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC)` |
| 8790–8795 | poison/fire/spirit stripped off `hit.m_damage` into locals |
| 8796 | `ApplyDamage(hit, showDamageText: true, triggerEffects: true, significantModifier)` |
| 8797–8801 | `AddFireDamage / AddSpiritDamage / AddPoisonDamage / AddFrostDamage / AddLightningDamage` |

Related public entry points:
```csharp
public HitData.DamageModifiers GetDamageModifiers(WeakSpot weakspot = null)   // :8809
public void ApplyDamage(HitData hit, bool showDamageText, bool triggerEffects,
                        HitData.DamageModifier mod = HitData.DamageModifier.Normal) // :8817
protected virtual void OnDamaged(HitData hit)                                  // :9103  (empty in Character)
public Action<float, Character> m_onDamaged;                                   // :6875  (public delegate, best non-Harmony hook)
protected virtual void DoDamageCameraShake(HitData hit)                        // :8881
protected virtual void DamageArmorDurability(HitData hit)                      // :8885
```
Inside `ApplyDamage`: `hit.ApplyModifier(Game.m_localDamgeTakenRate)` for players (:8832), `hit.ApplyModifier(Game.m_playerDamageRate)` for non-players (:8828); `if (totalDamage2 <= 0.1f) return;` (:8836); `SetHealth(health)` (:8855); `m_onDamaged(totalDamage2, hit.GetAttacker())` (:8873).

---

## 2. `class HitData` (:112132) — plain class, `public`, **not** a MonoBehaviour, **not** `[Serializable]`-marked at class level

### Fields (all `public`, all instance) — :112764–112810
| Line | Declaration |
|---|---|
| 112764 | `private static StringBuilder m_sb = new StringBuilder();` |
| **112766** | `public DamageTypes m_damage;` (struct `HitData.DamageTypes`) |
| **112768** | `public bool m_dodgeable;` |
| **112770** | `public bool m_blockable;` |
| **112772** | `public bool m_ranged;` |
| 112774 | `public bool m_ignorePVP;` |
| **112776** | `public short m_toolTier;`  ← **short, not int** |
| **112778** | `public float m_pushForce;` |
| **112780** | `public float m_backstabBonus = 1f;` |
| **112782** | `public float m_staggerMultiplier = 1f;` |
| **112784** | `public Vector3 m_point = Vector3.zero;` |
| **112786** | `public Vector3 m_dir = Vector3.zero;` |
| **112788** | `public int m_statusEffectHash;` |
| **112790** | `public ZDOID m_attacker = ZDOID.None;`  ← **ZDOID, not Character** |
| **112792** | `public Skills.SkillType m_skill;` |
| 112794 | `public float m_skillRaiseAmount = 1f;` |
| 112796 | `public float m_skillLevel;` |
| **112798** | `public short m_itemLevel;` ← **short** |
| **112800** | `public byte m_itemWorldLevel;` ← **byte** |
| 112802 | `public HitType m_hitType;` (`HitData.HitType : byte`, :112203) |
| 112804 | `public float m_healthReturn;` |
| 112806 | `public float m_radius;` |
| 112808 | `public short m_weakSpot = -1;` |
| **112810** | `public Collider m_hitCollider;` ← **NOT serialized** (see gotchas) |

Ctors: `public HitData()` :112812, `public HitData(float damage)` :112816. `public HitData Clone()` :112821 (MemberwiseClone → **shallow**, `m_damage` is a struct so it copies fine).

### Methods
```csharp
public void Serialize(ref ZPackage pkg)                                   // :112826
public void Deserialize(ref ZPackage pkg)                                 // :112942
public float GetTotalPhysicalDamage()                                     // :112981
public float GetTotalElementalDamage()                                    // :112986
public float GetTotalDamage()                                             // :112991
public void ApplyResistance(DamageModifiers modifiers,
                            out DamageModifier significantModifier)       // :113045
public void ApplyArmor(float ac)                                          // :113080
public void ApplyModifier(float multiplier)                               // :113085
public float GetTotalBlockableDamage()                                    // :113099
public void BlockDamage(float damage)                                     // :113104
public bool HaveAttacker()                                                // :113122
public Character GetAttacker()                                            // :113127
public void SetAttacker(Character attacker)                               // :113145
public bool CheckToolTier(int minToolTier, bool alwaysAllowTierZero=false) // :113157
public override string ToString()                                         // :113170
private float ApplyModifier(float baseDamage, DamageModifier mod, ref float normalDmg,
        ref float resistantDmg, ref float weakDmg, ref float immuneDmg)   // :113001 (private overload)
```

```csharp
// :112991 — NOTE the world-level surcharge, it is NOT in m_damage
public float GetTotalDamage()
{
    Character attacker = GetAttacker();
    if (attacker != null && Game.m_worldLevel > 0 && !attacker.IsPlayer())
    {
        return m_damage.GetTotalDamage() + (float)(Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseDamage);
    }
    return m_damage.GetTotalDamage();
}

// :113085 — does NOT touch m_damage.m_damage (the generic "Damage" channel)
public void ApplyModifier(float multiplier)
{
    m_damage.m_blunt *= multiplier; m_damage.m_slash *= multiplier; m_damage.m_pierce *= multiplier;
    m_damage.m_chop *= multiplier;  m_damage.m_pickaxe *= multiplier;
    m_damage.m_fire *= multiplier;  m_damage.m_frost *= multiplier; m_damage.m_lightning *= multiplier;
    m_damage.m_poison *= multiplier; m_damage.m_spirit *= multiplier;
}

// :113080
public void ApplyArmor(float ac) { m_damage.ApplyArmor(ac); }

// :113127
public Character GetAttacker()
{
    if (m_attacker.IsNone()) return null;
    if (ZNetScene.instance == null) return null;
    GameObject gameObject = ZNetScene.instance.FindInstance(m_attacker);
    if (gameObject == null) return null;
    return gameObject.GetComponent<Character>();
}

// :113145
public void SetAttacker(Character attacker)
{
    if ((bool)attacker) m_attacker = attacker.GetZDOID();
    else m_attacker = ZDOID.None;
}
```

`HitData.DamageTypes.ApplyArmor` (the real formula) — :112522/:112532:
```csharp
public static float ApplyArmor(float dmg, float ac)
{
    float result = Mathf.Clamp01(dmg / (ac * 4f)) * dmg;
    if (ac < dmg / 2f) result = dmg - ac;
    return result;
}
```
`DamageTypes` fields (all `public float`, :112382–112402): `m_damage, m_blunt, m_slash, m_pierce, m_chop, m_pickaxe, m_fire, m_frost, m_lightning, m_poison, m_spirit`. Helpers: `HaveDamage()` :112406, `GetTotalPhysicalDamage()` :112415 (blunt+slash+pierce), `GetTotalStaggerDamage()` :112420, `GetTotalBlockableDamage()` :112425, `GetTotalElementalDamage()` :112430, `GetTotalDamage()` :112435, `Clone()` :112440, `Add(DamageTypes,int mult=1)` :112445, `Modify(float)` :112460, `Modify(DamageTypes)` :112475 (multiplies by `1f + mult.x`), `IncreaseEqually(float,bool)` :112490, `GetMajorityDamageType()` :112552/:112558.

`ApplyResistance` multipliers (:113010–113037): SlightlyResistant ×0.75, Resistant ×0.5, VeryResistant ×0.25, SlightlyWeak ×1.25, Weak ×1.5, VeryWeak ×2.0, Immune ×0, Ignore → 0 (returns early).

---

## 3. Heal

```csharp
// Character :8592 — public, instance, NOT virtual
public void Heal(float hp, bool showText = true)
{
    if (!(hp <= 0f))
    {
        if (m_nview.IsOwner()) { RPC_Heal(0L, hp, showText); return; }
        m_nview.InvokeRPC("RPC_Heal", hp, showText);
    }
}

// Character :8605
private void RPC_Heal(long sender, float hp, bool showText)
{
    if (!m_nview.IsOwner()) return;
    float health = GetHealth();
    if (health <= 0f || IsDead()) return;
    float num = Mathf.Min(health + hp, GetMaxHealth());
    if (num > health) { SetHealth(num); if (showText) { ... DamageText.instance.ShowText(DamageText.TextType.Heal, ...); } }
}
```
Registered `m_nview.Register<float, bool>("RPC_Heal", RPC_Heal);` (:7349).

**`Player.Heal` — NOT FOUND.** `Player` does not declare or override `Heal`; `Character.Heal(float, bool)` at :8592 is the only `Heal(float…)` in the assembly. Patch `Character.Heal` (and filter with `__instance is Player`).

Health accessors:
```csharp
public float GetHealth()          // :9325  -> m_nview.GetZDO()?.GetFloat(ZDOVars.s_health, GetMaxHealth()) ?? GetMaxHealth()
public void  SetHealth(float)     // :9330  -> writes ZDO only if (zDO != null && m_nview.IsOwner())
public void  UseHealth(float hp)  // :9347
public float GetMaxHealth()       // :9384  -> ZDO GetFloat(ZDOVars.s_maxHealth, m_health)
public void  SetMaxHealth(float)  // :9372  -> NO owner check on the ZDO write (see gotchas)
public float GetMaxHealthBase()   // :9393
```

---

## 4. `ZDO` (:62124) — Set/Get. **Both `string` and `int` overloads exist; the `int hash` form is the modern/canonical one.**

Nothing is `[Obsolete]` — the `string` overloads simply forward: `Set(name.GetStableHashCode(), value)`. **Prefer the `int` overload with a cached `"key".GetStableHashCode()`** (this is what all vanilla code does via `ZDOVars`).

### Setters (all `public void`, instance)
| Line | Signature |
|---|---|
| 62417 | `public void Set(string name, float value)` |
| **62422** | `public void Set(int hash, float value)` |
| 62430 | `public void Set(string name, Vector3 value)` |
| **62435** | `public void Set(int hash, Vector3 value)` |
| 62443 | `public void Update(int hash, Vector3 value)` |
| 62451 | `public void Set(string name, Quaternion value)` |
| 62456 | `public void Set(int hash, Quaternion value)` |
| 62464 | `public void Set(string name, int value)` |
| **62469** | `public void Set(int hash, int value, bool okForNotOwner = false)` ← the extra param is **ignored in the body** |
| 62493 | `public void Set(string name, bool value)` |
| **62498** | `public void Set(int hash, bool value)`  → `Set(hash, value ? 1 : 0)` (stored as int) |
| 62503 | `public void Set(string name, long value)` |
| **62508** | `public void Set(int hash, long value)` |
| 62516 | `public void Set(string name, byte[] bytes)` |
| 62521 | `public void Set(int hash, byte[] bytes)` |
| 62529 | `public void Set(string name, string value)` |
| **62534** | `public void Set(int hash, string value)` |
| 62385 | `public void Set(string name, ZDOID id)` |
| 62390 | `public void Set(KeyValuePair<int,int> hashPair, ZDOID id)` |
| 62396 | `public static KeyValuePair<int,int> GetHashZDOID(string name)` → `(name+"_u").GetStableHashCode(), (name+"_i").GetStableHashCode()` |

Canonical body:
```csharp
// :62422
public void Set(int hash, float value)
{
    if (ZDOExtraData.Set(m_uid, hash, value)) { IncreaseDataRevision(); }
}
// :62635
private void IncreaseDataRevision()
{
    DataRevision++;
    if (!ZNet.instance.IsServer()) { ZDOMan.instance.ClientChanged(m_uid); }
}
```
**There is no ownership check anywhere in `ZDO.Set`.**

### Getters (all `public`, instance) — value form + `out bool` form
| Line | Signature |
|---|---|
| 62653 / **62658** | `public float GetFloat(string name, float defaultValue = 0f)` / `public float GetFloat(int hash, float defaultValue = 0f)` |
| 62663 / 62668 | `public bool GetFloat(string name, out float value)` / `public bool GetFloat(int hash, out float value)` |
| 62673 / **62678** | `public Vector3 GetVec3(string name, Vector3 defaultValue)` / `public Vector3 GetVec3(int hash, Vector3 defaultValue)` ← **no default arg; you must pass one** |
| 62683 / 62688 | `public bool GetVec3(string name, out Vector3 value)` / `(int hash, out Vector3)` |
| 62693 / 62698 | `public Quaternion GetQuaternion(string name, Quaternion defaultValue)` / `(int hash, Quaternion)` |
| 62703 / 62708 | `public bool GetQuaternion(string name, out Quaternion value)` / `(int, out)` |
| 62713 / **62718** | `public int GetInt(string name, int defaultValue = 0)` / `public int GetInt(int hash, int defaultValue = 0)` |
| 62723 / 62728 | `public bool GetInt(string name, out int value)` / `(int, out)` |
| 62733 / **62738** | `public bool GetBool(string name, bool defaultValue = false)` / `public bool GetBool(int hash, bool defaultValue = false)` → `ZDOExtraData.GetInt(m_uid, hash, defaultValue?1:0) != 0` |
| 62743 / 62748 | `public bool GetBool(string name, out bool value)` / `(int, out)` |
| 62753 / **62758** | `public long GetLong(string name, long defaultValue = 0L)` / `public long GetLong(int hash, long defaultValue = 0L)` |
| 62763 / **62768** | `public string GetString(string name, string defaultValue = "")` / `public string GetString(int hash, string defaultValue = "")` |
| 62773 / 62778 | `public bool GetString(string name, out string value)` / `(int, out)` |
| 62783 / 62788 | `public byte[] GetByteArray(string name, byte[] defaultValue = null)` / `(int, byte[])` |
| 62401 / 62406 | `public ZDOID GetZDOID(string name)` / `(KeyValuePair<int,int>)` |

**There is no `GetLong(string/int, out long)`** — NOT FOUND (only the default-value form).

Ownership / identity:
```csharp
public ZDOID m_uid = ZDOID.None;   // :62147  public field
public bool  IsValid()             // :62346
public long  GetOwner()            // :63608
public bool  IsOwner()             // :63617  -> return Owner;
public bool  HasOwner()            // :63622
public void  SetOwner(long uid)    // :63627
public ushort OwnerRevision { get; set; }  // :62320
public uint   DataRevision  { get; set; }  // :62322
```

`ZDOVars` (`public static class ZDOVars`, :66396) — all `public static readonly int`:
`s_health` :66504 (`"health"`), `s_maxHealth` :66570 (`"max_health"`), `s_stamina` :66664, `s_eitr` :66474, `s_noise` :66576 (`"noise"`), `s_stealth` :66670 (`"Stealth"` — **capital S**).

---

## 5. `ZNetView` (:70199)

```csharp
private ZDO m_zdo;                              // :70219  (private — publicize needed for direct access)

public bool IsOwner()      // :70424 -> if (!IsValid()) return false; return m_zdo.IsOwner();
public bool HasOwner()     // :70433
public void ClaimOwnership()// :70442 -> if (!IsOwner()) m_zdo.SetOwner(ZDOMan.GetSessionID());
public ZDO  GetZDO()       // :70450 -> return m_zdo;   (CAN return null — no null guard)
public bool IsValid()      // :70455 -> if (m_zdo != null) return m_zdo.IsValid(); return false;
public void ResetZDO()     // :70464
public void Destroy()      // :70419
public void InvokeRPC(long targetID, string method, params object[] parameters)  // :70513
public void InvokeRPC(string method, params object[] parameters)                 // :70518  (→ owner)
public void Register(string name, Action<long> f)                                // :70470
public void Register<T>(string name, Action<long,T> f)                           // :70475
public void Register<T,U>(...)  :70480   Register<T,U,V>(...) :70485
public void Unregister(string name)                                              // :70495
public static long Everybody = 0L;                                               // :70203
```

### Safe way to reach a `Character`'s ZDO
```csharp
// m_nview is `protected ZNetView m_nview;` at :7047 — requires the publicized assembly.
var nv = character.m_nview;
if (nv == null || !nv.IsValid()) return;      // IsValid() covers m_zdo == null AND m_zdo.IsValid()
ZDO zdo = nv.GetZDO();                        // now guaranteed non-null
```
Vanilla's own idiom (`Character.GetHealth` :9327) uses the null-conditional because `GetZDO()` may be null:
```csharp
return m_nview.GetZDO()?.GetFloat(ZDOVars.s_health, GetMaxHealth()) ?? GetMaxHealth();
```
Public alternatives that avoid touching `m_nview`: `Character.IsOwner()` :9698, `Character.GetOwner()` :9707, `Character.GetZDOID()` :9689. There is **no public `Character.GetZNetView()`** (NOT FOUND).

---

## 6. `string.GetStableHashCode()`

**NOT in assembly_valheim.** The only `ExtensionMethods` class in the decompile (:32138) contains only `Swap<T>`.

It lives in **`assembly_utils.dll`**, verified by reflection:
`StringExtensionMethods` — `public abstract sealed class` (i.e. `static class`), **global namespace**, `[Extension]`:
```csharp
public static int GetStableHashCode(this string str)
```
IL-decoded body (`MaxStack=4`, 3 int locals) — djb2-variant, two interleaved accumulators:
```csharp
int num = 5381;
int num2 = num;
for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
{
    num = ((num << 5) + num) ^ str[i];
    if (i == str.Length - 1 || str[i + 1] == '\0') break;
    num2 = ((num2 << 5) + num2) ^ str[i + 1];
}
return num + num2 * 1566083941;
```
(constants confirmed from IL: `ldc.i4 0x1505` = 5381, `ldc.i4 0x5D588B65` = 1566083941). Case-sensitive, no culture dependence, deterministic across machines — safe as a network key. Reference `assembly_utils.dll` in the csproj to call it; it is used everywhere in vanilla (`ZDOVars` :66396+, `ZNetView.Register` :70472, `ZDO.Set(string,…)` :62419).

---

## 7. Noise / stealth → AI

### Character noise
```csharp
private float m_noiseRange;        // :7149  PRIVATE (publicize to read directly)
private float m_syncNoiseTimer;    // :7151

private void UpdateNoise(float dt)                       // :10099  private, owner-only (called from :7453)
{
    m_noiseRange = Mathf.Max(0f, m_noiseRange - dt * 4f);      // decays 4/sec
    m_syncNoiseTimer += dt;
    if (m_syncNoiseTimer > 0.5f) { m_syncNoiseTimer = 0f; m_nview.GetZDO().Set(ZDOVars.s_noise, m_noiseRange); }
}

public void AddNoise(float range)                        // :10110
{
    if (m_nview.IsValid())
    {
        if (m_nview.IsOwner()) { RPC_AddNoise(0L, range); return; }
        m_nview.InvokeRPC("RPC_AddNoise", range);
    }
}

private void RPC_AddNoise(long sender, float range)      // :10123  registered :7350
{
    if (m_nview.IsOwner() && range > m_noiseRange)
    {
        m_noiseRange = range;
        m_seman.ModifyNoise(m_noiseRange, ref m_noiseRange);   // <-- status effects apply HERE, owner-side
    }
}

public float GetNoiseRange()                             // :10132
{
    if (!m_nview.IsValid()) return 0f;
    if (m_nview.IsOwner()) return m_noiseRange;
    return m_nview.GetZDO().GetFloat(ZDOVars.s_noise);
}
```
`AddNoise` is a **max**, not an accumulate (`range > m_noiseRange`). Modifier feed:
```csharp
// SEMan :24553
public void ModifyNoise(float baseNoise, ref float noise)
{ foreach (StatusEffect se in m_statusEffects) se.ModifyNoise(baseNoise, ref noise); }

// StatusEffect base :26670  -> public virtual void ModifyNoise(float baseNoise, ref float noise) {}
// SE_Stats  :25916
public override void ModifyNoise(float baseNoise, ref float noise) { noise += baseNoise * m_noiseModifier; }
// SE_Stats field :25691  public float m_noiseModifier;   (additive-percentage of base)
```

### Player stealth
```csharp
private float m_stealthFactorUpdateTimer;  // :15798
private float m_stealthFactor;             // :15800
private float m_stealthFactorTarget;       // :15802

private void UpdateStealth(float dt)       // :21812  PRIVATE, called only at :16072 (owner+alive branch of FixedUpdate)
{
    m_stealthFactorUpdateTimer += dt;
    if (m_stealthFactorUpdateTimer > 0.5f)
    {
        m_stealthFactorUpdateTimer = 0f;
        m_stealthFactorTarget = 0f;
        if (IsCrouching())
        {
            m_lastStealthPosition = base.transform.position;
            float skillFactor = m_skills.GetSkillFactor(Skills.SkillType.Sneak);
            float lightFactor = StealthSystem.instance.GetLightFactor(GetCenterPoint());
            m_stealthFactorTarget = Mathf.Lerp(0.5f + lightFactor * 0.5f, 0.2f + lightFactor * 0.4f, skillFactor);
            m_stealthFactorTarget = Mathf.Clamp01(m_stealthFactorTarget);
            m_seman.ModifyStealth(m_stealthFactorTarget, ref m_stealthFactorTarget);   // :21826
            m_stealthFactorTarget = Mathf.Clamp01(m_stealthFactorTarget);              // clamped AFTER modifiers
        }
        else { m_stealthFactorTarget = 1f; }
    }
    float num = Mathf.MoveTowards(m_stealthFactor, m_stealthFactorTarget, dt / 4f);    // 4s to travel 0→1
    if (!m_stealthFactor.Equals(num)) { m_nview.GetZDO().Set(ZDOVars.s_stealth, num); }
    m_stealthFactor = num;
}

public virtual float GetStealthFactor()  // Character :10094 -> return 1f;
public override float GetStealthFactor() // Player :21842
{
    if (!m_nview.IsValid()) return 0f;
    if (m_nview.IsOwner()) return m_stealthFactor;
    return m_nview.GetZDO().GetFloat(ZDOVars.s_stealth);
}

// SEMan :24609
public void ModifyStealth(float baseStealth, ref float stealth)
{ foreach (StatusEffect se in m_statusEffects) se.ModifyStealth(baseStealth, ref stealth); }
// StatusEffect base :26674 (empty virtual);  SE_Stats :25921 -> stealth += baseStealth * m_stealthModifier;
// SE_Stats field :25693  public float m_stealthModifier;
```
**Semantics: lower `m_stealthModifier` value = stealthier.** `stealthFactor` scales the AI's effective view range (see below), so a *positive* `m_stealthModifier` makes you MORE visible. Clamp01 means you cannot go below 0 or above 1.

### BaseAI consumption
```csharp
// BaseAI fields
public float m_viewRange = 50f;       // :3844
public float m_viewAngle = 90f;       // :3846
public float m_hearRange = 9999f;     // :3848
public bool  m_mistVision;            // :3850
private static int m_viewBlockMask = 0; // :3953

public bool CanHearTarget(Character target)                                  // :4546
    => CanHearTarget(base.transform, m_hearRange, target);

public static bool CanHearTarget(Transform me, float hearRange, Character target)   // :4551
{
    if (target.IsPlayer()) { Player p = target as Player; if (p.InDebugFlyMode() || p.InGhostMode()) return false; }
    float num = Vector3.Distance(target.transform.position, me.position);
    if (Character.InInterior(me)) hearRange = Mathf.Min(12f, hearRange);
    if (num > hearRange) return false;
    if (num < target.GetNoiseRange()) return true;      // :4570  <-- NOISE FEEDS AI HERE
    return false;
}

public bool CanSeeTarget(Character target)                                    // :4577
    => CanSeeTarget(base.transform, m_character.m_eye.position, m_viewRange, m_viewAngle, IsAlerted(), m_mistVision, target);

public static bool CanSeeTarget(Transform me, Vector3 eyePoint, float viewRange,
                                float viewAngle, bool alerted, bool mistVision, Character target) // :4582
{
    ...
    float num = Vector3.Distance(target.transform.position, me.position);
    if (num > viewRange) return false;
    _ = num / viewRange;
    float stealthFactor = target.GetStealthFactor();     // :4602  <-- STEALTH FEEDS AI HERE
    float num2 = viewRange * stealthFactor;              // :4603  effective view range
    if (num > num2) return false;
    if (!alerted && Vector3.Angle(target.transform.position - me.position, me.forward) > viewAngle) return false;
    Vector3 vector = (target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position);
    Vector3 vector2 = vector - eyePoint;
    if (Physics.Raycast(eyePoint, vector2.normalized, vector2.magnitude, m_viewBlockMask)) return false;
    if (!mistVision && ParticleMist.IsMistBlocked(eyePoint, vector)) return false;
    return true;
}

protected bool CanSeeTarget(StaticTarget target)   // :4625  (different overload, no stealth)
public bool CanSenseTarget(Character target)       // :4520
public bool CanSenseTarget(Character target, bool passiveAggresive)  // :4524
public static bool CanSenseTarget(Transform me, Vector3 eyePoint, float hearRange, float viewRange,
        float viewAngle, bool alerted, bool mistVision, Character target,
        bool passiveAggresive, bool isTamed)       // :4529  (hear first, then see)
```
Second stealth consumer — alert radius in `MonsterAI` (:6123): `float num2 = m_alertRange * m_targetCreature.GetStealthFactor();` then `if (canSeeTarget && num < num2) SetAlerted(true);`
Noise consumers outside AI: `Player.IsPlayerInRange(Vector3, float, float minNoise)` :20411, `Player.GetPlayerNoiseRange(Vector3, float maxNoiseRange = 100f)` :20427.

Vanilla `AddNoise` call sites for calibration: land on ground 15f (:8089), jump/land 30f/15f (:8319/:8323), `Stagger` 30f (:9615), Player chop/hit 50f (:16423, :18224), pickup 5f (:20678), destructible hit `m_hitNoise`, explosions 100f.

---

## 8. `Humanoid` equipment (:12806, `public class Humanoid : Character`)

```csharp
protected ItemDrop.ItemData m_rightItem;     // :12868   ** protected — publicize required **
protected ItemDrop.ItemData m_leftItem;      // :12870
protected ItemDrop.ItemData m_chestItem;     // :12872
protected ItemDrop.ItemData m_legItem;       // :12874
protected ItemDrop.ItemData m_ammoItem;      // :12876
protected ItemDrop.ItemData m_helmetItem;    // :12878
protected ItemDrop.ItemData m_shoulderItem;  // :12880
protected ItemDrop.ItemData m_utilityItem;   // :12882
protected ItemDrop.ItemData m_trinketItem;   // :12884
private   ItemDrop.ItemData m_hiddenLeftItem;  // :12902
private   ItemDrop.ItemData m_hiddenRightItem; // :12904

public ItemDrop.ItemData RightItem => m_rightItem;   // :12942  <-- PUBLIC read-only property, use this
public ItemDrop.ItemData LeftItem  => m_leftItem;    // :12944
```
No public property exists for chest/legs/helmet/shoulder/utility/trinket — NOT FOUND; you must use the publicized protected fields.

```csharp
// :13254
public ItemDrop.ItemData GetCurrentWeapon()
{
    if (m_rightItem != null && m_rightItem.IsWeapon()) return m_rightItem;
    if (m_leftItem != null && m_leftItem.IsWeapon() && m_leftItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
        return m_leftItem;
    if ((bool)m_unarmedWeapon) return m_unarmedWeapon.m_itemData;
    return null;                       // only null if m_unarmedWeapon is null
}

private ItemDrop.ItemData GetCurrentBlocker()  // :13271

public bool EquipItem(ItemDrop.ItemData item, bool triggerEquipEffects = true)   // :13806
public void UnequipItem(ItemDrop.ItemData item, bool triggerEquipEffects = true) // :14021  returns void
```
`EquipItem` returns `false` early (no state change) when: already equipped (`IsItemEquiped`), `!m_inventory.ContainsItem(item)`, `InAttack() || InDodge()`, player swimming and not on ground, `m_shared.m_useDurability && m_durability <= 0f`, missing DLC, or `Game.m_worldLevel > 0 && item.m_worldLevel < Game.m_worldLevel` for Utility/Trinket (:13808–13837). `UnequipItem(null, …)` is a safe no-op (:14023).

---

## 9. `Player.UpdateModifiers` / `GetEquipmentEitrRegenModifier`

```csharp
// Player :17662  PRIVATE — only called from UpdateStats :17221, which runs owner-only + not-dead (FixedUpdate :16046/:16055/:16067)
private void UpdateModifiers()
{
    if (s_equipmentModifierSourceFields == null) return;
    for (int i = 0; i < m_equipmentModifierValues.Length; i++)
    {
        float num = 0f;
        if (m_rightItem    != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_rightItem.m_shared);
        if (m_leftItem     != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_leftItem.m_shared);
        if (m_chestItem    != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_chestItem.m_shared);
        if (m_legItem      != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_legItem.m_shared);
        if (m_helmetItem   != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_helmetItem.m_shared);
        if (m_shoulderItem != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_shoulderItem.m_shared);
        if (m_utilityItem  != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_utilityItem.m_shared);
        if (m_trinketItem  != null) num += (float)s_equipmentModifierSourceFields[i].GetValue(m_trinketItem.m_shared);
        m_equipmentModifierValues[i] = num;
    }
}
```
Backing state (all `private`, publicize to touch):
```csharp
private float[] m_equipmentModifierValues;                                  // :15431
private static FieldInfo[] s_equipmentModifierSourceFields;                 // :15433
private static readonly string[] s_equipmentModifierSources = new string[11]// :15435
{ "m_movementModifier","m_homeItemsStaminaModifier","m_heatResistanceModifier","m_jumpStaminaModifier",
  "m_attackStaminaModifier","m_blockStaminaModifier","m_dodgeStaminaModifier","m_swimStaminaModifier",
  "m_sneakStaminaModifier","m_runStaminaModifier","m_maxAdrenaline" };
private static readonly string[] s_equipmentModifierTooltips = new string[11]; // :15441
```
Reflection cache built in `Player.Awake` (:15885–15892) via `typeof(ItemDrop.ItemData.SharedData).GetField(name, BindingFlags.Instance | BindingFlags.Public)`. Read back with `GetEquipmentModifier(int index)` (:21879, returns 0 if array null), `GetEquipmentModifierPlusSE(int)` (:21943), `public override float GetEquipmentMovementModifier()` (:21888, index 0), `public override float GetEquipmentHomeItemModifier()` (:21893, index 1). Base `Character` versions are :10358 / :10363.

**Note: `m_eitrRegenModifier` is NOT in that reflection table.** It has its own hardcoded, hand-rolled sum:
```csharp
// Player :17299  PUBLIC, instance
public float GetEquipmentEitrRegenModifier()
{
    float num = 0f;
    if (m_chestItem    != null) num += m_chestItem.m_shared.m_eitrRegenModifier;     // :17304
    if (m_legItem      != null) num += m_legItem.m_shared.m_eitrRegenModifier;       // :17308
    if (m_helmetItem   != null) num += m_helmetItem.m_shared.m_eitrRegenModifier;    // :17312
    if (m_shoulderItem != null) num += m_shoulderItem.m_shared.m_eitrRegenModifier;  // :17316
    if (m_leftItem     != null) num += m_leftItem.m_shared.m_eitrRegenModifier;      // :17320
    if (m_rightItem    != null) num += m_rightItem.m_shared.m_eitrRegenModifier;     // :17324
    if (m_utilityItem  != null) num += m_utilityItem.m_shared.m_eitrRegenModifier;   // :17328
    return num;
}
```
**`m_trinketItem` is deliberately absent from this sum** (7 slots, not 8). Field: `public float m_eitrRegenModifier;` in `ItemDrop.ItemData.SharedData` at **:58059**.

Consumption in `Player.UpdateStats` (:17258–17278):
```csharp
float maxEitr = GetMaxEitr();
float num4 = 1f;
if (IsBlocking()) num4 *= 0.8f;
if (InAttack() || InDodge()) num4 = 0f;
float num5 = (m_eiterRegen + (1f - m_eitr / maxEitr) * m_eiterRegen) * num4;
float eitrMultiplier = 1f;
m_seman.ModifyEitrRegen(ref eitrMultiplier);
eitrMultiplier += GetEquipmentEitrRegenModifier();     // :17271  ADDITIVE onto the SE multiplier
num5 *= eitrMultiplier;
m_eitrRegenTimer -= dt;
if (m_eitr < maxEitr && m_eitrRegenTimer <= 0f) m_eitr = Mathf.Min(maxEitr, m_eitr + num5 * dt);
m_nview.GetZDO().Set(ZDOVars.s_eitr, m_eitr);          // :17278
```
A postfix on `GetEquipmentEitrRegenModifier` is the clean injection point (public, pure, no side effects, called once per stats tick).

---

## 10. `SEMan` (:24219) — attach point for stat/damage mods
```csharp
public SEMan(Character character, ZNetView nview)                                            // :24267
public void Update(ZDO zdo, float dt)                                                        // :24318  (called owner-only, :7454)
public StatusEffect AddStatusEffect(int nameHash, bool resetTime = false, int itemLevel = 0, float skillLevel = 0f)      // :24352
public StatusEffect AddStatusEffect(StatusEffect statusEffect, bool resetTime = false, int itemLevel = 0, float skillLevel = 0f) // :24394
public bool RemoveStatusEffect(StatusEffect se, bool quiet = false)                           // :24422
public bool RemoveStatusEffect(int nameHash, bool quiet = false)                              // :24427
public bool HaveStatusEffect(int nameHash)                                                    // :24496
public StatusEffect GetStatusEffect(int nameHash)                                             // :24506
public List<StatusEffect> GetStatusEffects()                                                  // :24501
public void ApplyDamageMods(ref HitData.DamageModifiers mods)                                 // :24302
public void ModifyNoise(float baseNoise, ref float noise)                                     // :24553
public void ModifyStealth(float baseStealth, ref float stealth)                               // :24609
public void ModifyAttack(Skills.SkillType skill, ref HitData hitData)                         // :24617
public void OnDamaged(HitData hit, Character attacker)                                        // :24745
public static readonly int s_statusEffectBurning/-Spirit/-Poison/-Frost/...                   // :24235–24263
public SEMan GetSEMan()  // on Character, :10654 — public accessor for `protected SEMan m_seman;` :7147
```
`m_seman.ModifyAttack(...)` is called **attacker-side** while building the HitData: `Attack` :1757 (projectile spawn), :1907 (melee sweep), :2163 (secondary/AOE path). That is the correct place to buff *outgoing* damage — it happens before serialization, so the modified numbers travel to the victim.

---

## 11. MULTIPLAYER: who runs what

| Member | Runs on | Naive-postfix hazard |
|---|---|---|
| `Character.Damage` :8692 | **Attacker's client only** (the one doing the raycast). Just serializes + sends. | Reading `GetHealth()` here is meaningless — nothing has been applied yet. Mutating `hit` here **does** propagate (Serialize runs after). |
| `Character.RPC_Damage` :8701, lines 8703–8711 | **Every client that has the victim instanced** (routed RPC is delivered broadcast-style; the owner check is only at :8712) | A postfix on `RPC_Damage` that applies an effect **runs N times on N clients**. Anything visual/audio-only is fine; anything that mutates state must be behind `m_nview.IsOwner()`. |
| `Character.RPC_Damage` :8712→8802 | **Victim's ZDO owner only** | Safe for state mutation, but a *prefix* here can silently cancel damage for everyone. |
| `Character.ApplyDamage` :8817 | Owner only in practice (only called from `RPC_Damage` after the gate) — but it is **`public`**, so any mod/prefab can call it from a non-owner. **Do not assume ownership; re-check `m_nview.IsOwner()`.** | `SetHealth` :9337 silently no-ops on non-owners → your postfix "applies" damage that never lands, then the owner's next ZDO sync snaps health back. Classic desync. |
| `Character.Heal` :8592 | Callable anywhere; forwards to owner. `RPC_Heal` :8607 re-checks `IsOwner()`. | A postfix on `Heal` fires on the caller's machine, **not** where healing happens. Postfix `RPC_Heal` (after its own owner check) if you need the actual heal event. |
| `Character.SetHealth` :9330 | Writes only if owner | — |
| `Character.SetMaxHealth` :9372 | **Writes the ZDO with NO owner check** (`if (m_nview.GetZDO() != null)`) | Calling this from a non-owner bumps `DataRevision` on a ZDO you don't own → the value is fought over / reverted. Gate it yourself. |
| `Character.UpdateNoise` :10099 / `RPC_AddNoise` :10123 | Owner only (`:7450` gate + explicit `IsOwner()`) | `AddNoise` from a non-owner sends an RPC; the *modifier* (`SEMan.ModifyNoise`) runs on the owner. If your status effect only exists client-side on the attacker, the noise modifier never applies. |
| `Player.UpdateStealth` :21812 | **Local player only** (FixedUpdate :16046 `IsOwner()` + :16055 `!IsDead()`) | `m_stealthFactor` is authoritative on the owner; remotes read `ZDOVars.s_stealth`. A postfix that writes `m_stealthFactor` on a remote Player object does nothing observable and will be overwritten by the ZDO. |
| `Player.GetStealthFactor` :21842 / `Character.GetNoiseRange` :10132 | Owner returns local field; everyone else reads ZDO | **Patch these getters, not the fields**, if you want remote clients (i.e. the AI-owning machine) to see your modified value. AI runs on the *creature's* owner, which is usually NOT the sneaking player's machine — so a mod that only changes `m_stealthFactor` locally will have zero effect on enemies owned by another peer. The only value that crosses the wire is `ZDOVars.s_stealth` / `s_noise`. |
| `BaseAI.CanSeeTarget` / `CanHearTarget` :4551/:4582 | **Creature ZDO owner** (server or whichever client owns the mob) | If your patch is client-only (not on the dedicated server / other clients), stealth/noise changes are invisible to the AI. This is a hard ServerSync requirement. |
| `Player.UpdateModifiers` :17662, `UpdateStats` :17214 | **Local player only** | Never runs for remote players; do not use it as a "for every player" tick. |
| `Player.GetEquipmentEitrRegenModifier` :17299 | Called from `UpdateStats` → local player only | Safe to postfix. Eitr itself is written to `ZDOVars.s_eitr` by the owner (:17278). |
| `Humanoid.EquipItem` :13806 / `UnequipItem` :14021 | Runs wherever called; visual sync goes through `VisEquipment`/`SetupVisEquipment` | A postfix that adds/removes a `StatusEffect` on equip will run on the owner only in practice (equip is driven by local input), but ensure `__instance.m_nview.IsOwner()` before touching `SEMan`, or a remote-driven `SetupVisEquipment` path can double-add. |
| `SEMan.AddStatusEffect` :24352/:24394 | Local to whoever calls it | Status effects are **not** ZDO-synced as objects. Adding an SE on a remote character has no authority; add it on the owner. |
| `SEMan.ModifyAttack` :24617 (called :1757/:1907/:2163) | **Attacker's client, pre-serialization** | Correct place for outgoing-damage mods. `SEMan.OnDamaged` :24745 is called from `RPC_Damage` :8731 → **victim owner only**. |


## GOTCHAS
- HitData.m_hitCollider (public Collider, :112810) is NOT written by HitData.Serialize (:112826-112940) and NOT read by Deserialize (:112942-112979). It is ALWAYS null on the receiving side. Character.Damage (:8696) converts it to hit.m_weakSpot (short) BEFORE sending precisely because of this. Any postfix on RPC_Damage/ApplyDamage that dereferences hit.m_hitCollider will NullReferenceException on the victim's machine.
- The ':8707' gate the plan cites is inside Character.RPC_Damage (:8701), NOT inside Character.Damage (:8692). Character.Damage has no local-player logic at all - it only calls FindWeakSpotIndex + InvokeRPC. Patching Character.Damage to read/modify health does nothing.
- Lines :8703-:8711 of RPC_Damage execute on EVERY client that has the victim instanced; the `if (!m_nview.IsOwner()) return;` gate is at :8712. A Harmony postfix on RPC_Damage runs once per peer -> any state mutation double-applies. Always re-gate with `__instance.m_nview.IsValid() && __instance.m_nview.IsOwner()`.
- HitData.m_toolTier is `short` (:112776), NOT int. ItemDrop.ItemData.SharedData.m_toolTier is `int` (:58153). HitData.m_itemLevel is `short` (:112798) and HitData.m_itemWorldLevel is `byte` (:112800). Passing int literals to a reflection/Traverse setter on these will throw at runtime.
- HitData.m_attacker is a `ZDOID` (:112790), not a Character reference. Assigning `hit.m_attacker = someCharacter` will not compile - use SetAttacker(Character) (:113145). GetAttacker() (:113127) does a ZNetScene.instance.FindInstance lookup and returns null if the attacker is not instanced on that peer, which is COMMON on a dedicated server / far-away client. RPC_Damage :8721 explicitly bails when `hit.HaveAttacker() && attacker == null`.
- HitData.ApplyModifier(float) (:113085) does NOT scale m_damage.m_damage (the generic 'Damage' channel). A x2 damage mod written as ApplyModifier(2f) will silently miss the generic channel used by many creature attacks. HitData.DamageTypes.Modify(float) (:112460) DOES scale it.
- HitData.GetTotalDamage() (:112991) adds `Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseDamage` for non-player attackers, on top of m_damage.GetTotalDamage(). It is not a pure sum of the struct - do not assume GetTotalDamage() == m_damage.GetTotalDamage().
- ZDO.Set has NO ownership check on any overload (see :62422, :62469). IncreaseDataRevision (:62635) bumps DataRevision and calls ZDOMan.instance.ClientChanged unconditionally on clients. Writing a ZDO you don't own produces a value that the real owner's next sync overwrites -> silent desync. Always check m_nview.IsOwner() yourself.
- ZDO.Set(int hash, int value, bool okForNotOwner = false) (:62469) - the okForNotOwner parameter is completely IGNORED in the method body. It grants nothing.
- ZDO.Set(int hash, bool value) (:62498) stores an INT (`Set(hash, value ? 1 : 0)`). It is not a distinct bool channel - GetInt on the same hash will read it, and RemoveInt removes it.
- ZDO.GetVec3(name/hash, Vector3 defaultValue) (:62673/:62678) and GetQuaternion (:62693/:62698) have NO default argument. Calling GetVec3(hash) with one argument will not compile.
- ZDOVars.s_stealth is "Stealth".GetStableHashCode() with a CAPITAL S (:66670), while s_noise is "noise" lowercase (:66576) and s_health is "health" lowercase (:66504). Hashing the wrong case yields a different key and reads garbage/defaults.
- ZNetView.GetZDO() (:70450) returns the raw m_zdo field with no null guard - it CAN return null (Character.GetHealth :9327 uses `?.` for exactly this reason). Always call nview.IsValid() (:70455) first, which checks both null and ZDO.IsValid().
- string.GetStableHashCode() is NOT in assembly_valheim. It is `public static int GetStableHashCode(this string str)` on the static class `StringExtensionMethods` in assembly_utils.dll (global namespace). You must add an assembly reference to assembly_utils.dll or the mod will not compile.
- Player does NOT override Heal. Character.Heal(float hp, bool showText = true) (:8592) is the only Heal in the assembly - patching a nonexistent Player.Heal will make HarmonyX throw at plugin load.
- Character.m_nview is `protected` (:7047), m_seman is `protected` (:7147), m_noiseRange is `private` (:7149), Player.m_stealthFactor / m_equipmentModifierValues / s_equipmentModifierSourceFields are `private` (:15800/:15431/:15433), and Humanoid.m_rightItem..m_trinketItem are `protected` (:12868-12884). All of these only compile against the PUBLICIZED assembly. There is no public Character.GetZNetView().
- Player.GetEquipmentEitrRegenModifier (:17299) sums SEVEN slots and deliberately OMITS m_trinketItem. Player.UpdateModifiers (:17662) sums EIGHT slots (includes m_trinketItem) but does NOT include m_eitrRegenModifier - it is not in s_equipmentModifierSources (:15435). The two systems are separate; adding a field to one does not affect the other.
- Player.UpdateModifiers (:17662) and UpdateStealth (:21812) are both PRIVATE and are only reached from FixedUpdate (:16037) after `if (!m_nview.IsOwner()) return;` (:16046) and `else if (!IsDead())` (:16055). They never run for remote players or dead players - do not use them as a per-player tick.
- Stealth/noise reach the AI only through the ZDO (ZDOVars.s_stealth / s_noise). BaseAI.CanSeeTarget (:4582) / CanHearTarget (:4551) execute on the CREATURE's owner, which on a dedicated server or another client is a different machine than the sneaking player. Modifying Player.m_stealthFactor client-side does nothing to enemies owned elsewhere - you must patch the getters (Player.GetStealthFactor :21842, Character.GetNoiseRange :10132) AND ship the patch to every peer / the server via ServerSync.
- SE_Stats.ModifyStealth (:25921) does `stealth += baseStealth * m_stealthModifier`, and Player.UpdateStealth Clamp01s the result AFTER (:21827). A POSITIVE m_stealthModifier makes the player MORE visible (higher factor = larger effective AI view range at :4603). The sign is counter-intuitive.
- Character.RPC_AddNoise (:10125) uses `range > m_noiseRange` - AddNoise is a MAX, not an accumulator. Calling AddNoise(5f) repeatedly never stacks above 5.
- Character.SetMaxHealth (:9372) writes ZDOVars.s_maxHealth with only a `GetZDO() != null` check - NO IsOwner() check, unlike SetHealth (:9337). Calling it from a non-owner corrupts the shared value.
- Status effects are not ZDO-synced objects. SEMan.AddStatusEffect (:24352/:24394) applies locally; RPC_Damage applies hit.m_statusEffectHash only on the victim owner (:8756-8772). Adding an SE from a non-owner postfix produces a client-local effect that other peers never see.

## NOT FOUND
- HitData.m_statusEffect (string) - NOT FOUND. HitData has only `public int m_statusEffectHash;` (:112788). Sources set it via `m_statusEffect.NameHash()` on the ITEM/attack side (e.g. Attack :1744), not on HitData.
- Player.Heal / Player.RPC_Heal - NOT FOUND. Player does not override Character.Heal (:8592). Only one `Heal(float` exists in the whole assembly.
- Character.GetZNetView() / public Character.m_nview - NOT FOUND. m_nview is `protected ZNetView m_nview;` (:7047). Public alternatives: Character.IsOwner() :9698, Character.GetOwner() :9707, Character.GetZDOID() :9689.
- ZDO.GetLong(string, out long) / ZDO.GetLong(int, out long) - NOT FOUND. Only the default-value forms exist (:62753, :62758). (GetFloat/GetVec3/GetQuaternion/GetInt/GetBool/GetString/GetByteArray all DO have out-forms.)
- ZDO.RemoveString / ZDO.RemoveByteArray / ZDO.RemoveBool - NOT FOUND. Only RemoveInt(string/int) :62823/:62828, RemoveLong(int) :62833, RemoveFloat(string/int) :62838/:62843, RemoveVec3 :62848/:62853, RemoveQuaternion :62858/:62863, RemoveZDOID :62868/:62875 exist.
- Any [Obsolete]-marked ZDO Set/Get overload - NOT FOUND. Neither the string-key nor the int-hash overloads carry an obsolete attribute; the string forms simply forward via name.GetStableHashCode().
- GetStableHashCode inside assembly_valheim - NOT FOUND. The only `ExtensionMethods` class in the decompile (:32138) contains solely `Swap<T>(this List<T>, int, int)`. The extension lives in assembly_utils.dll / StringExtensionMethods.
- Public properties for Humanoid chest/legs/helmet/shoulder/utility/trinket slots - NOT FOUND. Only `RightItem` (:12942) and `LeftItem` (:12944) have public property accessors; the rest are protected fields (:12872-12884).
- Character.m_noiseRange as a public field - NOT FOUND. It is `private float m_noiseRange;` (:7149). Use public GetNoiseRange() (:10132) / AddNoise(float) (:10110).
- Player.SetStealthFactor / Character.SetNoiseRange - NOT FOUND. No public setters exist for either; the only write paths are UpdateStealth (:21812, private) and RPC_AddNoise (:10123, private).
- A `Character.Damage` virtual/override in Player or Humanoid - NOT FOUND. `public void Damage(HitData hit)` (:8692) is non-virtual on Character; the other 8 `Damage(HitData hit)` hits in the file (105003, 116161, 116614, 120528, 127124, 127357, 130831) belong to unrelated IDestructible classes (Destructible, TreeBase, WearNTear-likes), not to Character subclasses.
- HitData.ApplyArmor(float, out something) or an armor overload taking DamageModifiers - NOT FOUND. Only `public void ApplyArmor(float ac)` (:113080), which delegates to DamageTypes.ApplyArmor(float) (:112532).