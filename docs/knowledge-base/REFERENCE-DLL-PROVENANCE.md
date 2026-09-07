# libs-Tools reference DLL provenance

> **2026-09-03 — REFRESHED**, from `GAME-SNAPSHOT-pre-1.0-build21981559/` (client build 21981559),
> during YggdrasilsReckoning Phase 0. `assembly_valheim.dll`, `assembly_utils.dll`, `assembly_guiutils.dll`
> in this folder now match the client md5s in that snapshot's README. Both decompiles regenerated
> (`assembly_valheim.decompiled.cs` 144,177 lines; `assembly_valheim_SERVER.decompiled.cs` 142,841 lines,
> from the dedicated-server assembly in the same snapshot). **This is still the pre-1.0 build** — Valheim
> 1.0 lands 2026-09-09 and this refresh must be repeated from a fresh Steam install that day (see
> `GAME-SNAPSHOT-pre-1.0-build21981559/README.md` for the restore procedure and TheEye 2.2.0's
> `Manifest.json` for per-dump md5 tracking).

**Game build these were taken from: 2026-07-02** (Steam client install)
Refreshed: 2026-08-01. Source: `C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed`

## Why this file exists

Valheim stamps **every** assembly `AssemblyVersion 0.0.0.0` / `FileVersion 0.0.0.0`, so you
**cannot** tell a February DLL from a July one by version. The only reliable signals are **md5 and
mtime**. Before this refresh the refs were ~4.5 months behind the installed game and nothing
surfaced it: the build succeeded, and a signature drift would only have shown up at runtime as a
`MissingMethodException`.

## How to check for staleness

```powershell
$live = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
$refs = "C:\WubarrkCODING\libs-Tools"
Get-ChildItem $refs -Filter *.dll | ForEach-Object {
  $lp = Join-Path $live $_.Name
  if ((Test-Path $lp) -and (Get-FileHash $_.FullName -Algorithm MD5).Hash -ne (Get-FileHash $lp -Algorithm MD5).Hash) {
    "STALE: " + $_.Name
  }
}
```

Run it after every Valheim update. Copy any stale file straight from `\C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed`, then regenerate the
decompiles (see below) and re-run this table.

## Rules

- Game DLLs here are **vanilla copies**, never publicized. Projects that need private members set
  `<Publicize>true</Publicize>` on the reference and let `BepInEx.AssemblyPublicizer.MSBuild` do it at build time.
- `assembly_publicizer.dll` is the one exception: a **pre-publicized** copy of `assembly_valheim`,
  kept because ShadowsOfMidgard references it directly without the MSBuild publicizer. Regenerate it
  by building Fatty Release and copying `Fatty\obj\Release\net48\publicized\assembly_valheim.dll` over it.
- `Splatform.dll` and `UnityEngine.AssetBundleModule.dll` were already current at this refresh.
- Non-game DLLs (BepInEx, 0Harmony, Jotunn, Newtonsoft.Json, ServerSync, YamlDotNet, steamworks.net,
  Wonderland, PlayerDLL) do not come from the game install and are not tracked in the table below.

## Regenerating the decompiles

Requires `DOTNET_ROLL_FORWARD=LatestMajor` and `ilspycmd`. Output is UTF-8 (PowerShell `>` would
write UTF-16LE and double the file size, which is why these files used to be 2x bigger).

```bash
export DOTNET_ROLL_FORWARD=LatestMajor
L=/c/WubarrkCODING/libs-Tools
ilspycmd "$L/assembly_valheim.dll"            > "$L/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs"
ilspycmd "$L/UnityEngine.CoreModule.dll"      > "$L/decompiled_core/UnityEngine.CoreModule.decompiled.cs"
ilspycmd -t UIInputHandler "$L/assembly_guiutils.dll" > "$L/decompiled_guiutils/UIInputHandler.decompiled.cs"
ilspycmd -t Player    "$L/assembly_valheim.dll" > "$L/CSharp/Player.cs"
ilspycmd -t SE_Rested "$L/assembly_valheim.dll" > "$L/CSharp/SE_Rested.cs"
ilspycmd -il          "$L/assembly_valheim.dll" > "$L/CSharp/Player.il"   # whole-assembly IL, misnamed
```

`Player.cs` / `SE_Rested.cs` / `Player.il` are kept as identical copies in both `CSharp\` and the
`libs-Tools` root - update both. **`Player.il` is a whole-assembly IL dump, not just Player**;
the name is a historical misnomer.

`assembly_valheim_SERVER.decompiled.cs` comes from the dedicated-server assembly under
`DEDICATED-SERVER-TESTBED\`, **not** from the client DLL. It tracks that install's own version.

## Current md5s (game-sourced DLLs only)

| DLL | md5 | size |
|:----|:----|-----:|
| `assembly_guiutils.dll` | `4dce939bd3fa90e58cd2001a79ab866d` | 31744 |
| `assembly_postprocessing.dll` | `01a94985347b9bac41a4bb8592c92f9d` | 95232 |
| `assembly_utils.dll` | `6059fce96e96a8d6e160873ba7fe998a` | 198144 |
| `assembly_valheim.dll` | `2e93fc122fb44b827a5c32a36467696b` | 2126848 |
| `gui_framework.dll` | `8831396afb540aa38caf9f53c7bb5ab9` | 15872 |
| `Splatform.dll` | `2e0391f15ba4743366eb2a91eecf11e8` | 47104 |
| `Unity.TextMeshPro.dll` | `f3ba8764e1764a77f7bfb5ee5b191579` | 445952 |
| `UnityEngine.AnimationModule.dll` | `0db0fd800dedcb4f57be67a1095883f5` | 214448 |
| `UnityEngine.AssetBundleModule.dll` | `98dad675d8c421bc1e42cec0f6c2c1bb` | 38832 |
| `UnityEngine.CoreModule.dll` | `a381ea2672fbf4c7265d1c18125f0b75` | 1797040 |
| `UnityEngine.dll` | `f3b1b72ea06220ed50a5665da0a51d58` | 170416 |
| `UnityEngine.ImageConversionModule.dll` | `f3819e212e05a7dcf97080458f929ae1` | 28080 |
| `UnityEngine.IMGUIModule.dll` | `df601f9c26d8b877769f62de39de3a62` | 206760 |
| `UnityEngine.InputLegacyModule.dll` | `5f794c005f1688bc46fcf367ba363993` | 42408 |
| `UnityEngine.InputModule.dll` | `28e882eb6077d27180c091f16099fc55` | 25000 |
| `UnityEngine.JSONSerializeModule.dll` | `7ee7f16c682cade7af7c2bde50bce5e1` | 23984 |
| `UnityEngine.ParticleSystemModule.dll` | `b6fae595cce9ee25c8e26a92f132dec3` | 166824 |
| `UnityEngine.PhysicsModule.dll` | `bdd9b1f0fb6d3bdf5a6e77b989ee88ef` | 182704 |
| `UnityEngine.TextCoreFontEngineModule.dll` | `af7ef5753314a89d601e74af2613b2b3` | 84912 |
| `UnityEngine.TextCoreTextEngineModule.dll` | `86c7ad1827333c290cc71dd23f5073ec` | 302504 |
| `UnityEngine.TextRenderingModule.dll` | `f7b7cf3afff14aeb6b5721c525f5a24d` | 46000 |
| `UnityEngine.UI.dll` | `649a4f4c7ca984cd242898f6c16be813` | 266240 |
| `UnityEngine.UIElementsModule.dll` | `154fccf41c49c8c9ec67f6df3e23d570` | 2071984 |
| `UnityEngine.UIModule.dll` | `f651a6b7a1127a19057f7963eb56bc9c` | 45480 |
| `UnityEngine.UnityWebRequestAssetBundleModule.dll` | `17120128c82ecb209e42e7b269692f9f` | 25512 |
| `UnityEngine.UnityWebRequestAudioModule.dll` | `3696f348d67c37ef4a1548d367b6cc62` | 25008 |
| `UnityEngine.UnityWebRequestModule.dll` | `ea994821f7bc3cc5de9b6d5a3a723881` | 65968 |
| `UnityEngine.UnityWebRequestTextureModule.dll` | `2ba0fc010803782724ca5a8b5b8fb3f8` | 24496 |
| `UnityEngine.UnityWebRequestWWWModule.dll` | `38579866bfe512eab957cba956ab2dda` | 33200 |
| `UnityEngine.VFXModule.dll` | `bd1c9fe410bc66c417d6070ab0eb9779` | 74664 |
| `UnityEngine.WindModule.dll` | `ae9fa18e61d135af1f1433f426f0b9cc` | 23464 |
| `assembly_publicizer.dll` (derived) | `00b3d8be239b373bc1d2f982503ec0be` | 2215936 |

