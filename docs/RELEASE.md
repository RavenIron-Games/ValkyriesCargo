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

Packaging cleanly is not the bar.

**The bar was every item in CLAUDE.md's "What to verify in-game". The owner cut it to three on
2026-09-11, and this section records that decision rather than leaving a rule nobody follows.** The
reasoning, in his words: the list was written when the mod had never had a visit, and it has now had
twenty-seven across five sessions with deals, the terminal, the flight, the vanish, a resume across a
restart and a live config tune. Re-running the rest is ceremony. **Three of the never-run items earn
their place, because each has a consequence a player would feel:**

1. **The version wall.** A store release means strangers on mismatched versions; a wrong gate hands
   them a confusing failure instead of a clear one.
2. **Redelivery.** It is the one path where a player can pay and not receive.
3. **The non-admin refusal.** It is a trust boundary on a public server.

What was dropped, and why it is safe to drop: the prefab dumps are data gathering for later work, not
a correctness test; the flight edges and ghost mode fail cosmetically at worst, because the merchant is
immortal either way; the two unseen refusal codes are the same shape as one already proven; and
BarrkBOT reading the export is a consumer integration, not this mod's correctness.

So a release is ready when:

- **Those three have been done** — on a screen, with a client, against a dedicated server — **or the
  release says plainly which was not and why.**
- **Whoever saw each one pasted the exact log lines into `CLAUDE.md` "Status"**, with the date and
  the server. Not a summary: the line.
- **`README.md`'s Status section has been rewritten to match**, and says plainly what is built, what
  is proven, and what has never been seen.
- **`CHANGELOG.md` has an entry for everything merged since the last release**, in build order, with
  its check count.

**Where the three stand (2026-09-11, Storm10, Valheim 1.0.12, `docs/proofs/2026-09-11-release-session.md`):**
the version wall and the non-admin refusal are PROVEN with their lines; redelivery is SKIPPED at the
owner's word, because the window between a deal's answer and its ack was measured and cannot be hit from
outside the client process, and it stays proven off-game only. A release note that implies otherwise is
wrong.

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
  dependency string and the icon all come out of the package. That string is ONE entry, and it is read from the
  **repo-root** `manifest.json` (`tools/package.ps1` copies that file into the zip; the copy in
  `HexiumDist/` is not read by anything): `denikson-BepInExPack_Valheim-5.4.2350`, which is denikson's
  current release. Confirmed against Thunderstore's package API on 2026-09-11 rather than from memory,
  because a dependency string naming a version that does not exist fails at the store and nowhere earlier. The BarrkBOT export (`BARRKBOT_CONTRACT.md`)
  briefly added `ValheimModding-JsonDotNET` on 2026-09-07 for two serializer calls; the owner had it
  removed the same day in favour of the pure `Core/Json.cs`, so there is no third-party DLL at build
  time or at run time, and `tools/fetch-libs.ps1` copies nothing from outside the game install.
- **The built DLL is not tracked in git** (owner, 2026-09-07; WORKSPLIT §4). `HexiumDist/plugins/` is
  gitignored; the packager writes it there for the upload and it goes nowhere else. What a tester or a
  store gets is a **release asset**: the zip from `dist\` attached to the GitHub release, beside the baked
  bundle (`Assets/valkyriescargo_kit`) that a build on another machine needs. The two DLL blobs already
  in history (PR #22 and its rebuild) stay; nothing is rewritten.
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
