# Hexium Publishing — "hex fold" vs "hex web"

Covers how a built mod gets from `bin\Release` to players, and the two distinct
jobs the author asks for by name.

---

## The distinction (read this first)

The author uses two short forms. They are **not** interchangeable, and the
difference is entirely about whether anything leaves the machine.

| Phrase | Means | Network? | Reversible? |
|---|---|---|---|
| **hex fold** | The local `HexiumDist` **folder**. Build, stage, repack the zip. | No | Yes — just rebuild |
| **hex web** | Hexium **the website** (`hexium.gg`). Upload and publish the package. | Yes | **No** — a published version number cannot be reused |

Said another way: **hex fold is preparing the package, hex web is releasing it.**
Every hex web is preceded by a hex fold. A hex fold on its own is the normal,
everyday operation and needs no confirmation. **Never do a hex web unless it was
asked for explicitly** — publishing is outward-facing, and Hexium (like every
package index of this kind) treats a version number as permanently spent once it
has been accepted.

If the ask is ambiguous ("push it to Hexium"), assume **fold** and say so.

---

## hex fold — the local package

Every mod project in `c:\WubarrkCODING` has a staging folder next to the source.
The name is not consistent, so check before assuming:

- `HexiumDist\` — Fatty, DeathBeforeDishonor, MistsofAvalor, Njord, TortalPortal, WingsoftheValkyrie
- `HexiumDistrib\` — BlightedHeart, DvergrAllies, ShadowsOfMidgard

Layout (this is also exactly the zip's internal layout):

```
HexiumDist/
  manifest.json
  README.md
  CHANGELOG.md
  icon.png                    <- 256x256 PNG
  plugins/
    <ModName>.dll
  <ModName>-v<version>.zip    <- the artifact; not itself part of the package
```

Most `.csproj` files already copy the DLL into `HexiumDist\plugins\` in a
`PostBuild` target, so a plain `dotnet build -c Release` handles that step.

### The full fold checklist

1. **Build** — `dotnet build <Proj>.csproj -c Release`. Must be 0 errors AND
   0 warnings before going further.
2. **Bump the version in every place it lives.** This is the step that gets
   missed; a mismatch between the assembly and the manifest is not caught by the
   compiler. For a typical mod that is *five* files:
   - `Plugin.cs` → `public const string PluginVersion`
   - `Configuration.cs` (or `ConfigManager.cs`) → `ConfigSync.CurrentVersion`
   - `Configuration.cs` → `ConfigSync.MinimumRequiredVersion` — **a judgement
     call, not a copy of the above.** Only raise it when an older client would
     genuinely misbehave against the new server (a changed network payload,
     a new synced value it does not know about). Raising it needlessly locks
     players out for no reason.
   - `<Proj>.csproj` → `<Version>`
   - `HexiumDist\manifest.json` → `version_number`
   - plus the version badge in `HexiumDist\README.md` if the project has one
3. **Write the changelog** — new section at the top of `CHANGELOG.md`. Newest
   first. Say what broke and why, not just what changed.
4. **Update the README** if behaviour or config surface changed.
5. **Copy the DLL** into `HexiumDist\plugins\` (usually automatic; verify).
6. **Repack the zip** — see the packing rules below. Delete the superseded zip.
7. **Deploy to the Gale test profile** so it can actually be played:
   `%APPDATA%\com.kesomannen.gale\valheim\profiles\<Profile>\BepInEx\plugins\<Namespace>-<ModName>\<ModName>.dll`

   Two things to get right, both easy to fumble:
   - **Gale does NOT use `r2modmanPlus-local`.** That is a different manager's
     directory. If it exists on the machine it is stale and writing there
     deploys to nothing. Gale lives under `com.kesomannen.gale`, and its
     Valheim folder is lowercase `valheim`, not `Valheim`.
   - **Gale nests each mod in its own package folder** — `plugins\Wubarrk-TortalPortal\TortalPortal.dll`,
     not a flat `plugins\TortalPortal.dll`. Drop the DLL into the existing
     package folder; do not invent a new one.

   Verify the deploy by reading the file version back off the deployed DLL
   (`[System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion`)
   rather than trusting that the copy landed where intended.

### Packing rules

**Zip entry names must use forward slashes.** `plugins/ModName.dll`, never
`plugins\ModName.dll`. This is the ZIP specification's separator and what mod
managers expect.

.NET Framework's `System.IO.Compression.ZipFile.CreateFromDirectory` writes
**backslashes** on Windows, producing an archive some tools reject. So does
PowerShell's `Compress-Archive` on Windows PowerShell 5.1, which is built on it.
Write the entries explicitly instead:

```powershell
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$files = [ordered]@{
  'manifest.json'         = "$dist\manifest.json"
  'README.md'             = "$dist\README.md"
  'CHANGELOG.md'          = "$dist\CHANGELOG.md"
  'icon.png'              = "$dist\icon.png"
  'plugins/ModName.dll'   = "$dist\plugins\ModName.dll"   # forward slash
}
$fs = [System.IO.File]::Open($zip, 'CreateNew', 'ReadWrite')
$archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
foreach ($name in $files.Keys) {
  $e = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
  $o = $e.Open(); $b = [System.IO.File]::ReadAllBytes($files[$name]); $o.Write($b,0,$b.Length); $o.Dispose()
}
$archive.Dispose(); $fs.Dispose()
```

Then **verify** rather than assuming — reopen the archive, confirm no entry name
contains `\`, and confirm the packed DLL's hash matches the one in
`bin\Release`.

### manifest.json

```json
{
    "name": "TortalPortal",
    "version_number": "1.0.2",
    "website_url": "https://discord.com/invite/...",
    "description": "One line, shown on the listing card.",
    "dependencies": ["denikson-BepInExPack_Valheim-5.4.2333"]
}
```

`name` must match the folder/zip naming. `version_number` must be strict
three-part semver. `dependencies` are full package identifiers including their
version.

### Artifacts to keep out of git

Unpacking a package in place leaves a `HexiumDist\<ModName>-v<version>\` folder
sitting next to its own zip — a duplicate of the payload. Gitignore it:

```
HexiumDist/TortalPortal-v*/
```

---

## hex web — publishing to hexium.gg

**Docs (Swagger UI):** <https://valheim.hexium.gg/api/docs/> (OpenAPI 3.0.3, `Hexium API` v1.0.0)
**Machine-readable schema:** <https://valheim.hexium.gg/api/openapi.json>
— `/api/docs/?format=openapi` serves the same thing. **`/api/schema/` 404s**;
that is the usual DRF-Spectacular path and it is not what Hexium uses.
**Valheim community:** `valheim.hexium.gg`, community identifier `valheim`.

**Correction (2026-08-24):** an earlier revision of this file claimed endpoints
also exist community-scoped under `/c/{community_identifier}/`. They do not —
the live schema declares **32 paths and not one of them is under `/c/`**. Use
the community host (`valheim.hexium.gg`) and the plain `/api/...` paths.

### Verified from the published schema — re-pulled 2026-08-24

Read API — fully documented, and **needs no authentication**. This is the whole
path list as served, so anything absent here does not exist:

| Method | Path |
|---|---|
| GET | `/api/v1/package/` |
| GET | `/api/v1/package/{uuid4}/` |
| POST | `/api/v1/package/{uuid4}/rate/` — body `{"target_state": "Rated"\|"Unrated"}` |
| GET | `/api/v1/package-listing-index/` |
| GET | `/api/v1/package-listing-chunk/` |
| GET | `/api/v1/package-metrics/{namespace}/{name}/` → `downloads`, `rating_score`, `latest_version` |
| GET | `/api/v1/package-metrics/{namespace}/{name}/{version}/` → `downloads` |
| GET | `/api/experimental/package/{namespace}/{name}/` |
| GET | `/api/experimental/package/{namespace}/{name}/{version}/` |
| GET | `/api/experimental/package/{namespace}/{name}/{version}/readme/` |
| GET | `/api/experimental/package/{namespace}/{name}/{version}/changelog/` |
| GET | `/api/experimental/package-index/` — NDJSON stream |
| GET | `/api/experimental/community/`, `/api/experimental/current-community/` |
| POST | `/api/experimental/frontend/render-markdown/` — body `{"markdown": "..."}`, max 100000 chars. Useful for previewing a README before publishing. |
| GET | `/api/experimental/frontend/frontpage/`, `/api/experimental/frontend/packages/` |
| GET | `/api/experimental/frontend/p/{namespace}/{name}/`, `…/threads/` |
| GET | `/api/experimental/community/{community}/category/` |
| GET | `/api/cyberstorm/community/{community_id}/` |
| POST | `/api/experimental/legacyprofile/create/` · GET `…/get/{key}/` |

**Response shapes confirmed by use (2026-08-24), not just by schema:**

- `GET /api/experimental/package/{ns}/{name}/` → `latest.version_number` plus
  `latest.download_url`, a direct CDN link (`https://cdn.hexium.gg/upload/{id}/{version}.zip`).
  Downloading that and hashing `plugins/<Mod>.dll` is the definitive way to tell
  **which build actually shipped** under a version number — worth doing before
  writing a changelog entry that claims what a release contained.
- `…/{version}/changelog/` and `…/{version}/readme/` both return
  **`{"markdown": "..."}`** — key is `markdown`, not `changelog`/`readme`.
- **A per-version endpoint is NOT an existence check.** `…/{version}/readme/`
  answers **HTTP 200 for any version string at all**, published or not —
  `9.9.9` included — returning `{"markdown": null}` when the version does not
  exist. Verified on Wings (2026-09-02). So a 200 proves nothing; the test is
  whether `markdown` is non-null. Getting this wrong once cost a version number:
  a 200 on `2.1.3/readme/` was read as "already published", when in fact 2.1.2
  was still latest.
- **`latest.version_number` lags a fresh upload.** Also seen 2026-09-02: it kept
  reporting 2.1.4 for some minutes after 2.1.5 was live, and the package's
  `date_updated` was staler still. Before concluding a version is unpublished,
  check `…/{version}/readme/` for non-null `markdown` as well as `latest`, and
  re-check after a minute rather than burning the next number.
- **Hexium rewrites `manifest.json` inside the zip it serves.** Verified on
  Wings 2.0.3 and 2.0.4 (2026-08-24): the served archive drops
  `denikson-BepInExPack_Valheim-*` from `dependencies`, keeping only Jotunn —
  while the API and the listing page still report **both**, so Gale still
  installs BepInEx. Expect a downloaded package to differ from the one you
  packed by exactly this, and do not "fix" it by re-uploading. Everything else
  round-trips byte-identical (README, CHANGELOG, icon, and the DLL).


Publish flow — the endpoints are named and ordered, four steps:

1. `POST /api/experimental/usermedia/initiate-upload/` → returns a usermedia UUID
2. `PUT  /api/experimental/usermedia/{uuid}/upload-chunk/` → responds with an `ETag` per chunk
3. `POST /api/experimental/usermedia/{uuid}/finish-upload/`
4. `POST /api/experimental/submission/submit/`
   (or `submit-async/` then poll `GET /api/experimental/submission/poll-async/{id}/`)

`POST /api/experimental/usermedia/{uuid}/abort-upload/` cancels a part-done upload.
`POST /api/experimental/submission/upload/` is **deprecated** — do not use.

### STILL NOT verified — re-checked against the live schema 2026-08-24

Nothing has moved since this was first written. Pulled fresh from
`/api/openapi.json` today, the schema still declares:

- **no `securitySchemes`**, and no `security` block at top level or on any operation
- **no `requestBody` on any of the four publish operations** — all four are `ABSENT`
- **no component schemas** for anything matching usermedia / submission / upload
- responses declared (`201` for `initiate-upload`, `200` for the rest) but with
  **no content schema**, so even the reply shapes are unstated

Re-probed the same day: an unauthenticated `POST` to `initiate-upload/` still
returns **`HTTP 401` with `{"detail":"Authentication required."}` and no
`WWW-Authenticate` header**, so the token format cannot be recovered by probing.
Do not burn time on it. So the following remain unknown from the docs alone:

- the authorization header format and where the token is issued from
- the JSON body of `initiate-upload` (filename / size) and its response shape
  (presigned part URLs?)
- the body of `finish-upload` (the collected ETags?)
- the body of `submit` (author/namespace, categories, communities, NSFW flag,
  and how it references the completed upload)

**Lead for resolving these:** the schema's own vocabulary — `usermedia`,
`cyberstorm`, `legacyprofile`, `package-listing-chunk` — shows Hexium is built
on the open-source Thunderstore API, so that project's published client code is
the closest reference for the missing request bodies. Treat anything taken from
there as a hypothesis to confirm against a real token, **not** as documented
Hexium behaviour.

### How to actually establish the contract

Cheapest path, in order:

1. Log into `hexium.gg` and find the API/service-account token page — that page
   will state the header format.
2. Open the browser dev tools and publish one package through the web UI. The
   network tab gives every request body verbatim, for free.
3. Record the result **in this file**, moving it from the unverified section to
   the verified one.

Until step 3 has happened, a "hex web" request should be answered by saying the
publish contract is not yet established, and offering to do it via the web UI —
not by firing speculative requests at a live index where a wrong version number
is permanent.

---

## Where this fits

- `gale-hexium-only` (memory) — Gale is the test-profile manager, Hexium is the
  distribution side. Neither is r2modman/Thunderstore as far as workflow talk
  goes; the note above about the API's ancestry is a technical lead only.
- `SharedInfrastructure.md` — `libs-Tools` as reference-assembly cache and
  vendored-source folder, which is where a mod's dependencies come from before
  any of the above happens.
