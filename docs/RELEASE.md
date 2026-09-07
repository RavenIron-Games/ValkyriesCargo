# Releasing Valkyrie's Cargo

The procedure, in order. Everything up to the upload is a command; the upload is the owner's,
because the store credentials are.

---

## 1. Bump the version — in one place

`ValkyriesCargo/ValkyriesCargo.csproj`, the `<Version>` property. Nowhere else.

- The C# constant `ValkyriesCargo.PluginVersion` is generated from it at build time
  (`GenerateVersionConst` in the csproj); it is not a literal and must never become one.
- `manifest.json`'s `version_number` is **written** from it by `tools/package.ps1`, not compared
  against it. A number that is written cannot drift from itself.

Bumping the version bumps the ServerSync gate with it: `MinimumRequiredVersion == CurrentVersion`,
so every client on the old build is refused at the handshake. That is the point, and it is the
thing the release note has to say (step 5).

## 2. Tests green

```powershell
.\tools\run-tests.ps1
```

Expect `1034 passed, 0 failed` at the version this file was written against; the number only goes up.
A red harness is not a release candidate. Anything new since the last release needs its own checks,
and each proven to fail without its fix (the working agreement in `CLAUDE.md`).

## 3. Package

```powershell
.\tools\package.ps1
```

It writes `dist\RavenIronStudios-ValkyriesCargo-<version>.zip` and prints the entries. On the way it
refuses to package a stale binary, refuses a missing or wrongly sized `icon.png`, and warns if the
body bundle exists but the DLL is too small to be carrying it.

**Read the comments in `tools/package.ps1` before changing anything in it.** They are the record of
what the stores actually did: why the store files sit at the zip root and the DLL under `plugins/`,
why the entries are written by hand rather than by `Compress-Archive`, why the file is ASCII-only,
and why the icon size is checked here. Do not restate those facts from memory — they were measured.

Confirm the printed layout is exactly:

```
CHANGELOG.md
icon.png
manifest.json
README.md
plugins/ValkyriesCargo.dll
```

`dist/` is gitignored. The zip is a build output; it is never committed.

## 4. What "release-ready" means

Packaging cleanly is not the bar. A release is ready when:

- **Every item in `CLAUDE.md` "What to verify in-game" has been done** — on a screen, with a client,
  against a dedicated server, not just headless. A clean build proves nothing about a game member.
- **Whoever saw each one pasted the exact log lines into `CLAUDE.md` "Status"**, with the date and
  the server. Not a summary: the line.
- **`README.md`'s Status section has been rewritten to match**, and says plainly what is built, what
  is proven, and what has never been seen. As long as anything on that list is unproven, the "Status:
  not yet playable" block stays where it is.
- **`CHANGELOG.md` has an entry for everything merged since the last release**, in build order, with
  its check count.

Until then this is a source-available work in progress, not a store release.

## 5. The Hexium upload — the owner's step

The store is [Hexium](https://hexium.gg); the Valheim community is `valheim.hexium.gg`. It consumes
the Thunderstore package format, which is what `package.ps1` builds.

- **Team: `RavenIronStudios`** — the existing team, which already carries Cairn, Undertow, FireFront,
  RagnaroksWrath, RavenEye, TheRavensCall and WhereTheCrowFlies. The package would sit at
  `valheim.hexium.gg/mods/RavenIronStudios/ValkyriesCargo`.
- **Name check, 2026-09-06:** a search of the Valheim community for `ValkyriesCargo` returned "No
  mods found", and searches for `cargo`, `valkyrie` and `ingvar` turned up nothing by this name. The
  name is free. Re-check before uploading; it costs one search.
- Upload the zip from `dist\`. Nothing is edited by hand on the store side: the description, the
  dependency string and the icon all come out of the package. As of the BarrkBOT export
  (`BARRKBOT_CONTRACT.md`) that string is two entries, both written from `manifest.json`:
  `denikson-BepInExPack_Valheim-5.4.2333` and `ValheimModding-JsonDotNET-13.0.4` (the version
  confirmed live against Fatty's own shipped manifest, not guessed). The second exists because
  `Server/BarrkBotExport.cs` compiles against `Newtonsoft.Json.dll` at build time only
  (`<Private>false</Private>` in the csproj) and needs a runtime copy on the server; we do not ship
  one ourselves (`docs/DECISIONS-WUBARRK.md` #3 — two Newtonsoft builds in one `BepInEx/plugins`
  tree is a known way to break a server). `tools/fetch-libs.ps1` copies the build-time DLL itself
  from the workspace's `libs-Tools\`, which is a fresh-clone build requirement, not a player-facing
  one: a server owner installing this mod through a manager that resolves Thunderstore dependencies
  gets JsonDotNET automatically from the manifest entry above.
- **The release note must say that every client has to update.** The ServerSync gate refuses any
  client on a different version, so a server that updates and a player who does not is a player who
  cannot connect, with a message they will read as a crash. Say it in the first line of the note,
  not the last.
- Thunderstore is not a channel this studio publishes to (see the header of `tools/package.ps1`).
  `docs/DESIGN.md` section 9 also calls for a GitHub tag on `RavenIron-Games/ValkyriesCargo` matching
  the version.

Credentials are the owner's. No agent logs into the store, creates an account, or accepts terms on
anyone's behalf.

## 6. Rollback

The previous zip. Keep the last released `dist\RavenIronStudios-ValkyriesCargo-<version>.zip` (or
re-cut it from the tag), and re-upload it as the current version on Hexium. Because of the version
gate, a rollback is the same event as an upgrade for every player: the note has to say so again.

The world sidecar (`valkyriescargo_{worldUid}.dat`, beside the world save) carries a `format`
version on its first line and quarantines a file it cannot read to `.corrupt` rather than dying on
it, so a downgrade does not eat a market. It does not merge forward: a market saved by a newer
format and read by an older build may be reset. Back up the world folder before a rollback that
crosses a format bump.
