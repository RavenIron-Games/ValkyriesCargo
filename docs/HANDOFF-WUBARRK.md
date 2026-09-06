# Handoff for Wu'barrk — Valkyrie's Cargo, 2026-09-06

The repo is scaffolded, builds clean, tests pass, and boots headless on a dedicated server. This is
what it is, what of yours is already in it, what we need from you, and exactly where each thing goes.

## 1. Where things are

| Read first | What it is |
|---|---|
| `docs/TLDR.md` | one screen for the design, one for the catalogue |
| `docs/DESIGN.md` | v3, the design of record: your v5 merged with our v2 after the review |
| `docs/REVIEW-v5-2026-09-06.md` | what changed from your v5 and the engine fact behind each change |
| `docs/CATALOGUE.md` | the 72 catalogue entries with every number's reason |
| `CLAUDE.md` | build commands, layout, house rules, the "not ours" list, what is verified |

Layout: `ValkyriesCargo/` is the plugin (net472), `tests/CoreTests/` the off-game harness,
`tools/` the scripts, `libs/` the game DLLs (gitignored; see §2), `docs/` everything above.

## 2. Building on your machine

The csproj expects the game and BepInEx assemblies in `libs\` at the repo root. On Windows,
`tools\fetch-libs.ps1` copies them from the Steam install. On Linux, point it at your `libs-Tools`:
the files it needs are `assembly_valheim_publicized.dll`, `assembly_utils_publicized.dll`,
`assembly_guiutils_publicized.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`,
`UnityEngine.IMGUIModule.dll`, `UnityEngine.TextRenderingModule.dll`, `UnityEngine.InputLegacyModule.dll`,
`UnityEngine.UIModule.dll`, `UnityEngine.UI.dll`, `Unity.TextMeshPro.dll`, `UnityEngine.PhysicsModule.dll`,
`UnityEngine.AnimationModule.dll`, `BepInEx.dll`, `0Harmony.dll`. A symlink `libs -> ../libs-Tools`
works if the names match; the publicized ones must carry the `_publicized` suffix.

```
dotnet build ValkyriesCargo/ValkyriesCargo.csproj          # the mod
dotnet run --project tests/CoreTests/CoreTests.csproj       # 27 tests, no game needed
```

ServerSync is already in as `ValkyriesCargo/Libs/ServerSync.cs` (your shared-source pattern, not
ILRepack). Its header says where it came from; do not edit it, replace it from upstream.

## 3. What of yours is already in

- **The design.** v5's escrow ideas (nonce ring, the price the player saw in every deal, no inventory
  mutation before the server answers), the asset-pipeline lessons (§6.3–6.6), the `AttachPoint` anchor.
- **The item data.** `docs/data/items-valheim-2026-07-31.tsv` was extracted from your TheEye
  `Values_Dump.json` (2026-07-31): prefab, display name, type, stack, weight, vanilla value,
  teleportable, token. The catalogue and its tests were checked against it.
- **Your JOTUNN/headless facts** §7–§8 are cited for the Unity pipeline. There is no §9 in the copy
  here; the "windows-mono not needed" claim is marked unverified.

## 4. What we need from you, and where it goes

### 4.1 The two shared-source files (blocks the terminal)

From your `libs-Tools/SharedUI/`:

```
ValkyriesCargo/Libs/SharedUI/GiltFrameTheme.cs
ValkyriesCargo/Libs/SharedUI/UIFocus.cs
```

Add a header to each, same shape as `Libs/ServerSync.cs`:

```csharp
// NOT OURS. VikingOS SharedUI by Wubarrk - libs-Tools/SharedUI/GiltFrameTheme.cs at VikingOS 0.9.8,
// 2026-09-06. License MIT. Vendored as shared source; update from libs-Tools, never edit here.
```

Keep the namespaces as they are (`SharedUI`). The csproj globs every `.cs` under `ValkyriesCargo/`,
so dropping the files in is the whole integration; build once to confirm no missing reference.
`UIFocus` carries two Harmony patches of its own (`GameCamera.UpdateMouseCapture`, `Chat.HasFocus`);
they are expected and listed in the design as shared-source patches.

We will **not** reference `VikingOS.dll` at runtime: every class in it is internal, it is a beta,
and Cargo players on other servers may not run it. The terminal is drawn with your theme so it
looks native beside VikingOS when both are installed.

If you would rather publish `SharedUI` as a git submodule or a tagged tarball, say so and we will
consume it that way instead of vendoring.

### 4.2 The trade code, as reference only

`BarrkUI/Trading/TradeEscrow.cs`, `TradeInbox.cs`, `TradeRpc.cs`, `TradeItem.cs`, `TradeWindow.cs`
would help as **reading material** (the delivery-id inbox and redelivery rule are being ported into
our merchant deals, without Newtonsoft). Put them under `docs/reference/vikingos-trading/` if you
share them; nothing compiles from there.

### 4.3 The body (not on the 0.1 path)

The resized GLB (1.37 m) is the source of truth. Before it can be an NPC:
1. retopology or decimation to ~30k triangles, maps re-baked at 2048²;
2. a Humanoid rig with skin weights, plus an `AttachPoint` empty between the shoulder blades;
3. clips: idle, walk, talk gesture, hang;
4. an Animator Controller that declares what vanilla drives: floats `forward_speed`, `sideway_speed`,
   `turn_speed`, `tilt`, `statef`; bools `onGround`, `falling`, `inWater`, `encumbered`, `flying`,
   `sleeping`, `sitting`, `freeze`, `blocking`; int `statei`; triggers `attack`, `stagger`, `alert`,
   `interact`, `consume`, `eat`, `jump`, `equip_hip`, `fly_takeoff`, `fly_land`. At minimum the
   locomotion set, or he slides;
5. the bundle built with **Unity 6000.0.61f1** (the installed game's runtime; a newer Editor will not
   load), `SkinnedMeshRenderer` + `CapsuleCollider`, material baked at build time, target 5–10 MB.
Full plan: `docs/DESIGN.md` §11 and `docs/REVIEW-v5-2026-09-06.md` §2. Deliver the bundle plus the
prefab name it contains; the loader swaps it under the same `Humanoid`/`MonsterAI` prefab clone.

### 4.4 Data refreshes after a game update

Re-run TheEye's `dump_eye`, then regenerate the table (Node, from the dump folder):

```
node -e "const fs=require('fs');let s=fs.readFileSync('Values_Dump.json','utf8');if(s.charCodeAt(0)===0xFEFF)s=s.slice(1);const a=JSON.parse(s).filter(r=>r._Source==='Items');const g=(r,k)=>r[k]==null?'':String(r[k]);fs.writeFileSync('items.tsv','prefab\tdisplay\ttype\tstack\tweight\tvalue\tteleportable\ttoken\n'+a.map(r=>[r._Key,g(r,'_displayName'),g(r,'m_shared.m_itemType'),+g(r,'m_shared.m_maxStackSize')||0,+g(r,'m_shared.m_weight')||0,+g(r,'m_shared.m_value')||0,g(r,'m_shared.m_teleportable'),g(r,'m_shared.m_name')].join('\t')).join('\n'))"
```

Drop the result in `docs/data/items-valheim-<date>.tsv`, point the test harness at it
(`tests/CoreTests/Program.cs`, one constant), run the tests: a renamed prefab fails on the desk.

## 5. Decisions you should know were made

- **Custom terminal, locked** (owner, 2026-09-06), drawn in IMGUI with your theme, opened from our
  own `Interactable`; the vanilla store is never patched.
- **Price change on a staged deal: reconfirm, provisional.** The line turns amber and the player
  confirms once more; `Teardown` (dissolve every open tray) sits behind config. The owner is not sure
  yet; your view is wanted.
- **Odin vanish is the departure**, as the original brief said; v5's mist-and-horn is gone.
- **ServerSync as source, not ILRepack.** Same result, one fewer tool.
- **Dverger is the 0.1 body.** Yours replaces it behind the same contract when it is rigged.
- **The flight starts ~90 m out, not 500 m**: the game only instantiates objects inside a player's
  active zone block.

## 6. Do not

- Edit `Libs/ServerSync.cs` or, later, `Libs/SharedUI/*` in place; replace from upstream.
- Add a runtime dependency on VikingOS, Jotunn or any other plugin.
- Put the GLB, a bundle or any binary over a few hundred KB in the repo; bundles go beside the
  DLL in the package, sources live in the Unity project directory, which is a sibling folder, not
  inside the repo.
- Commit without `dotnet run --project tests/CoreTests/CoreTests.csproj` green.

## 7. Open questions for you

1. Shared-source files: vendored copies with headers, or a submodule?
2. Tear-down versus reconfirm on a price tick (§5).
3. Where bundles get built: your Linux box (has 6000.0.61f1) or Don's (has 6000.5.4f1 only)?
4. Will the rigged body come with its own clips, or should we plan on Mixamo?
