# 10 — Minimap and map pins

**Generated 2026-08-02.** Written out of the `TortalPortal` v1.1.2 work, where markers had to be
sized, tinted and made clickable. Every citation is a `:line` into
`libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`, verified by reading the
region rather than searching for a signature.

`public class Minimap : MonoBehaviour` — **:46574**.

---

## The three findings most likely to cost you a day

1. **`UpdatePins` reassigns `pin.m_iconElement.color` unconditionally on every run** (:47923-47924).
   Any tint you set from outside is stomped within the frame. A **postfix on `UpdatePins`** is the
   only place a per-pin colour survives. There is no per-pin colour field to hook it to.
2. **`localScale` is never read or written anywhere in `Minimap`.** It is therefore the only safe
   per-pin size lever. Size otherwise comes from `sizeDelta`, which is set at marker creation
   (:47910-47912) and re-set every run when `m_animate` or `m_worldSize > 0f` (:47936-47948).
   `m_doubleSize` is a **`bool`** — the only sizes vanilla offers are `n` and `2n`.
3. **`OnMapLeftUp` dispatches click and double-click from two INDEPENDENT branches** (:48636-48656),
   so a double-click produces `OnMapLeftClick, OnMapLeftClick, OnMapDblClick` — in that order. Any
   hook on `OnMapLeftClick` runs **twice** per double-click. The double-click window is a hardcoded
   `0.3f`; there is no `m_doubleClickTime` field.

---

## `Minimap.PinData` — :46604-46637

```csharp
public class PinData
{
    public string m_name;
    public PinType m_type;
    public Sprite m_icon;
    public Vector3 m_pos;
    public bool m_save;
    public long m_ownerID;
    public PlatformUserID m_author;
    public bool m_shouldDelete;
    public bool m_checked;
    public bool m_doubleSize;
    public bool m_animate;
    public float m_worldSize;
    public RectTransform m_uiElement;
    public GameObject m_checkedElement;
    public Image m_iconElement;
    public PinNameData m_NamePinData;
}
```

All public fields, no properties. **No colour field and no scale field** — that absence is the whole
reason findings 1 and 2 above matter.

`PinNameData` — **:46639-46674** — exposes `TMP_Text PinNameText`, `GameObject PinNameGameObject`,
`RectTransform PinNameRectTransform` (all `{ get; private set; }`) and a `readonly PinData ParentPin`.
Built in `CreateMapNamePin` (**:47317**) from `m_pinNamePrefab`.

### Add and remove

```csharp
public PinData AddPin(Vector3 pos, PinType type, string name, bool save, bool isChecked,
                      long ownerID = 0L, PlatformUserID author = default(PlatformUserID))   // :48557
public void RemovePin(PinData pin)                                                          // :48499
```

`AddPin` stamps `m_icon` from the `PinType`. Swapping `pin.m_icon` **after** `AddPin` and before the
next `UpdatePins` is enough to give a pin arbitrary art, because `m_iconElement.sprite = pin.m_icon`
happens only at marker creation (:47908). This is also the one fully patch-free way to get a
coloured pin: a pre-tinted sprite multiplied by vanilla's `Color.white` renders as the sprite's own
colour.

---

## Sizing — inside `private void UpdatePins()` :47877

Size fields: `m_pinSizeSmall = 32f` (**:46799**), `m_pinSizeLarge = 48f` (**:46801**).

```csharp
float num = ((m_mode == MapMode.Large) ? m_pinSizeLarge : m_pinSizeSmall);   // :47882

if (pin.m_uiElement == null || pin.m_uiElement.parent != rectTransform)      // :47903 — CREATION ONLY
{
    DestroyPinMarker(pin);
    GameObject gameObject = UnityEngine.Object.Instantiate(m_pinPrefab, rectTransform);
    pin.m_iconElement = gameObject.GetComponent<Image>();
    pin.m_iconElement.sprite = pin.m_icon;
    pin.m_uiElement = gameObject.transform as RectTransform;
    float size = (pin.m_doubleSize ? (num * 2f) : num);                      // :47910
    pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
    pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
    pin.m_checkedElement = gameObject.transform.Find("Checked").gameObject;
}
```

`SetSizeWithCurrentAnchors` writes `sizeDelta`. It runs **only at creation** unless `m_animate`
(:47936, a `0.8 + sin(t*5)*0.2` pulse) or `m_worldSize > 0f` (:47943, map-scale sizing for area
pins) is set — in which case it is rewritten every run and will fight you.

**To size one pin by an arbitrary factor:** set `pin.m_uiElement.localScale` from a postfix on
`UpdatePins`. Set it for *every* pin you own, not only the one you are emphasising — markers are
destroyed and re-instantiated (see lifecycle below), and a fresh element arrives at scale 1.

---

## Colour — :47891, :47923-47928

```csharp
Color color = new Color(0.7f, 0.7f, 0.7f, 0.8f * m_sharedMapDataFade);   // :47891
...
Color color2 = ((pin.m_ownerID != 0L) ? color : Color.white);            // :47923
pin.m_iconElement.color = color2;                                        // :47924
if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText.color != color2)
{
    pin.m_NamePinData.PinNameText.color = color2;                        // :47927
}
```

The icon component is a `UnityEngine.UI.Image` on the pin prefab root. Both the icon and the name
label are forced back every run — the label behind an inequality guard, the icon unconditionally.

`m_ownerID != 0L` means the pin came from a **shared map** (someone else's cartography table data);
those render grey and faded. When re-tinting from a postfix, **preserve the alpha vanilla just set**
rather than forcing `1f`, or your pin punches through map regions the player has not explored.

---

## Marker lifecycle — you cannot cache `m_uiElement`

`DestroyPinMarker` (**:47965**) is called, and the marker rebuilt from `m_pinPrefab`, whenever:

- the pin scrolls out of view (`!IsPointVisible`, :47899),
- its icon type is filtered out of `m_visibleIconTypes` (:47899),
- it is a shared-map pin and the fade has expired (:47899),
- or its parent changes — which happens on **every map-mode switch**, because large and small map
  pins live under different roots (`m_pinRootLarge` / `m_pinRootSmall`, :47895-47896).

So `pin.m_uiElement` and `pin.m_iconElement` are **new objects** after any of those. Anything you
attach or set on them must be re-applied, which is the second reason the postfix pattern wins: it
re-applies on exactly the frames the marker was rebuilt, with no bookkeeping.

---

## Update cadence — `m_pinUpdateRequired`

`private bool m_pinUpdateRequired;` (**:46863**). `UpdatePins()` runs only when it is set
(:47219-47221), then clears it.

It is set by: drag (:47516), map-centre change (:47607-47610), mode change (:47543), player-pin
movement (:47794, :47802), pings, add/remove pin, left/right click (:48684), and shared-map fade.

**Consequence:** if your own state changes — a different pin becomes "selected" — while the map sits
perfectly still, nothing redraws. Set `Minimap.instance.m_pinUpdateRequired = true` yourself
(publicized field) or the change will not appear until the player happens to drag or zoom.

---

## Mouse dispatch — :48631-48684

```csharp
private void OnMapLeftDown(UIInputHandler handler)   // :48631
{
    m_leftDownTime = Time.time;
}

private void OnMapLeftUp(UIInputHandler handler)     // :48636
{
    if (m_leftDownTime != 0f)
    {
        if (Time.time - m_leftDownTime < m_clickDuration)   // hold-duration test only
        {
            OnMapLeftClick();
        }
        m_leftDownTime = 0f;
    }
    m_dragView = false;
    if (Time.time - m_leftClickTime < 0.3f)                 // time-since-previous-click only
    {
        OnMapDblClick();
        m_leftClickTime = 0f;
    }
    else
    {
        m_leftClickTime = Time.time;
    }
}
```

Fields: `m_clickDuration = 0.25f` (**:46803**, max *hold* to count as a click rather than a drag),
`m_leftDownTime` (**:46898**), `m_leftClickTime` (**:46900**). Handler wiring is at **:47020**
(`m_onLeftUp = Delegate.Combine(...)`).

`OnMapLeftClick` (**:48666**) toggles `closestPin.m_checked` — or clears `m_ownerID` on a shared pin
— using `GetClosestPin(pos, m_removeRadius * (m_largeZoom * 2f))`. On a double-click the checkmark
is toggled twice and nets back to where it started, then `OnMapDblClick` (**:48658**) opens the
pin-name input unless `m_selectedType == PinType.Death`.

### Suppressing vanilla under your own UI

- Prefix `OnMapLeftDown` returning `false` kills the click, the double-click **and** the drag in one
  place — all three derive from `m_leftDownTime`, which then never gets set.
- If a press began on the map and was released over your window, zero `m_leftDownTime` **and**
  `m_dragView` in an `OnMapLeftUp` prefix, or the next press reads as an instant double-click.
- The scroll wheel reaches the map through `UpdateMap`'s `takeInput` parameter — a prefix setting
  `takeInput = false` takes the wheel away from the zoom while leaving map centring and drag
  continuation intact.
- `OnMapMiddleClick` (**:48684**) calls `Chat.instance.SendPing` — a stray middle-click through a UI
  window pings **every player on the server**.

---

## Recipe: act on a click that lands on your own marker

Established in `TortalPortal/MapInputPatch.cs`. Prefix `OnMapLeftClick`, test the world point against
your own marker positions using vanilla's own radius rule, and return `false` when you handled it so
vanilla does not also stamp a checkmark on the pin in passing:

```csharp
Vector3 world = __instance.ScreenToWorldPoint(ZInput.mousePosition);
float radius = __instance.m_removeRadius * (__instance.m_largeZoom * 2f);
```

Two traps, both learned the hard way:

- **Do not recentre the map as a side effect of the first click.** Moving the map drags the marker
  out from under a stationary cursor, so a second click at the same screen position resolves to a
  different world point entirely and never reaches the same marker.
- A trailing `OnMapDblClick` follows every second click. If your handler closed your UI, whatever
  guard you use to refuse that double-click no longer applies — suppress it on a short timestamp set
  when the click was acted on, checked *before* any visibility guard.

---

## NOT FOUND

Searched for and genuinely absent — do not go hunting:

- **`m_doubleClickTime`** or any configurable double-click window. The `0.3f` at :48650 is a literal.
- **Any per-pin colour, tint or scale field** on `PinData`. See the field list above.
- **Any `localScale` reference anywhere in `Minimap`.** This is an absence you can rely on, and the
  basis of the sizing recipe.
- **A per-pin "do not manage this" flag.** Every pin in `m_pins` goes through the same `UpdatePins`
  body; there is no opt-out.
