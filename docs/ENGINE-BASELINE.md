# The engine baseline

**What build every engine fact in `CLAUDE.md` and `docs/DESIGN.md` was read against, and how to
tell whether the build under your hands is still that one.** P10a; the brief is
`docs/P10-P11-FOR-DON.md`.

Baseline taken **2026-09-07**. The four version numbers below were read out of the assembly, not
copied from anywhere: `ilspycmd -t Version` on `assembly_valheim.dll`, whose `Version` type is
`internal` and carries `CurrentVersion` (a `GameVersion` property) plus the three `const` fields
`m_networkVersion`, `m_playerVersion` and `m_worldVersion`.

**Wu'barrk's 0.221.12 / net 36 / player 43 / world 37 is CONFIRMED**, and it is the same four
numbers on both the client and the dedicated server.

## THE baseline

**The installed client and the installed dedicated server, together.** Every engine fact this mod
records was read from one or the other of these two, and every design decision that turns on a
client/server difference (the whole authored-ZDO design, design §3.2) was read from both.

    client  C:\Program Files (x86)\Steam\steamapps\common\Valheim                  buildid 21981559
    server  C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server buildid 21981590

Different build ids on the two apps are normal: they are different Steam apps built from the same
source drop, and the four version numbers - which are what the wire and the save format actually
care about - are identical.

## Every build swept

| build | app | branch | steam buildid | game | net | player | world | fetched |
|---|---|---|---|---|---|---|---|---|
| **installed client** (THE baseline) | 892970 | public | 21981559 | 0.221.12 | 36 | 43 | 37 | already on the machine |
| **installed server** (THE baseline) | 896660 | public | 21981590 | 0.221.12 | 36 | 43 | 37 | already on the machine |
| shadow `server-live` | 896660 | public | 21981590 | 0.221.12 | 36 | 43 | 37 | 2026-09-07, steamcmd |
| shadow `server-test` | 896660 | public-test | — | — | — | — | — | **NOT FETCHED: the branch does not exist today** (see below) |
| shadow `client-live` | 892970 | public | — | — | — | — | — | not fetched: needs the owner's account |
| shadow `client-test` | 892970 | public-test | — | — | — | — | — | not fetched: the branch does not exist today |

**The live dedicated server IS the installed one.** Same build id, and the three assemblies are
byte-identical (SHA-256 below). `tools/diff-engine.js` over the two decompiled trees returns
0 differing type files out of 697 and 244 of 244 surface members unchanged - which is the answer
that was wanted from the sweep, and incidentally the tightest possible proof that the tool does not
invent differences.

## The assemblies

| build | assembly | bytes | modified | SHA-256 (first 16) |
|---|---|---|---|---|
| installed client | `assembly_valheim.dll` | 2,126,848 | 2026-07-02 07:35 | `3B26C8512778F6E0` |
| installed client | `assembly_utils.dll` | 198,144 | 2026-07-02 07:35 | `41C2D53EC0351E97` |
| installed client | `assembly_guiutils.dll` | 31,744 | 2026-07-02 07:35 | `70E391B5D1F5DC47` |
| installed server | `assembly_valheim.dll` | 2,119,680 | 2026-08-25 16:48 | `84A1B34F95774D36` |
| installed server | `assembly_utils.dll` | 197,632 | 2026-08-25 16:48 | `41F66746B80B3B30` |
| installed server | `assembly_guiutils.dll` | 31,744 | 2026-08-25 16:48 | `D46CAD5BFE2D384F` |
| shadow `server-live` | all three | identical to the installed server | 2026-09-06 22:31 | identical |

Full hashes of the two that matter:

    client assembly_valheim.dll  3B26C8512778F6E0664B5AF2A26F3C30993A00F584C1E76D9123A742B67E2004
    server assembly_valheim.dll  84A1B34F95774D36BE328390578D7B07C5CFFBC8CBB15119541900F055D486A3

`Valheim - Clean`, the untouched copy beside the client, hashes the same as the client
(`3B26C851...`): the working install has not drifted. Neither it nor any Steam folder was written
to by anything in this package.

The mod's own runtime, unchanged and re-checked here: **Unity 6000.0.61f1**
(`UnityPlayer.dll` file version 6000.0.61.7643309). A body bundle must be built with that Editor.

Decompiled type counts, which are themselves a coarse version fingerprint:

    baseline-client  695 .cs   (602 valheim + 72 utils + 21 guiutils)
    baseline-server  697 .cs   (604 valheim + 72 utils + 21 guiutils)
    server-live      697 .cs   identical to baseline-server

## There is no `public-test` branch today

The brief's `-beta public-test` could not be used, and the reason is not a failure of the machine
or the script. steamcmd's whole answer is `ERROR! Failed to set beta 'public-test'`, so
`tools/fetch-builds.ps1` now asks the app what branches it has before spending a download.
Asked anonymously on 2026-09-07, **both** Valheim apps advertise exactly six branches, and
`public-test` is not among them:

| branch | client 892970 | server 896660 | description |
|---|---|---|---|
| `public` | 21981559 | 21981590 | the live build |
| `default_old` | 20460509 | 20460518 | Previous stable |
| `default_preal` | 20221215 | 20221240 | Last stable build before Ashlands |
| `default_prebw` | 20221609 | 20221628 | Last stable build before Bog Witch |
| `default_precta` | 20221837 | 20221863 | Last stable build before Call to Arms |
| `default_preml` | 20222077 | 20222098 | Last stable build before Mistlands |

A playtest branch exists only while a playtest is running. **This is the good case for P10a**: the
baseline is captured with nothing pending, which is exactly the position the brief wanted
("a baseline captured after a change is worth much less than one captured before"). When Iron Gate
next opens a public test, `.\tools\fetch-builds.ps1 -Branch public-test` will find it and the sweep
is a command, not a re-derivation.

The five older branches are all fetchable anonymously and would each make a real sweep. **They were
not fetched**: the owner authorised the `live` and `public-test` branches and nothing else.
`default_old` (server buildid 20460518) is the obvious next one to ask about — it is the only way to
see this tooling report a real "body changed" against a real Valheim, and it costs one 1.6 GB
download.

## The client shadow builds

`892970` needs an account that owns Valheim. `tools/fetch-builds.ps1 -Which client -Account <name>`
passes only the account NAME and lets steamcmd ask for the password and Steam Guard on its own
console; the script has no `-Password` parameter and writes no credential anywhere. **Only the owner
types that login.** Without it the script refuses before doing anything, naming the reason.

Not fetching it costs less than it looks: the installed client at buildid 21981559 IS the live
client build, so a `client-live` shadow would be the same zero-difference result the server gave.

## Where the shadows live

    C:\Users\donfr\valheim-shadows\
      steamcmd\          153 MB   Valve's installer, unzipped here
      server-live\       1.52 GB  app 896660, branch public
      src\
        baseline-client\   the installed client, decompiled
        baseline-server\   the installed dedicated server, decompiled
        server-live\       the shadow, decompiled

**Nothing here is in the repo and nothing here ever should be.** The builds are Iron Gate's and the
decompiled trees are a hundred thousand lines of somebody else's source. `tools/fetch-builds.ps1`
refuses any target inside the Steam install, inside any library from `libraryfolders.vdf`, inside
the Steam client folder, or inside this repo — checked before a byte moves, and proven by pointing
it at all three.

## Redoing this

```powershell
.\tools\fetch-builds.ps1                       # server, both branches; prints build ids and versions
.\tools\decompile-builds.ps1 -Baseline -All    # ilspycmd -p over all three assemblies, idempotent
node tools\diff-engine.js --surface docs\ENGINE-SURFACE.md `
     --from C:\Users\donfr\valheim-shadows\src\baseline-server `
     --to   C:\Users\donfr\valheim-shadows\src\server-live `
     --out  docs\engine-sweeps\<date>-<what>.md --all-types
```

`fetch-builds.ps1` prints the build id and the four version numbers for every build it touches,
including the two installed ones, so re-running it with `-WhatIf` is the cheapest way to answer
"has my install moved off the baseline?".

**On Linux**, `tools/fetch-builds.sh` and `tools/decompile-builds.sh` are the same two steps, tested
end to end on Wu'barrk's box (2026-09-07 — see below):

```bash
./tools/fetch-builds.sh                         # server, both branches; prints build ids and versions
./tools/decompile-builds.sh --baseline --all    # ilspycmd -p over all three assemblies, idempotent
node tools/diff-engine.js --surface docs/ENGINE-SURFACE.md \
     --from ~/valheim-shadows/src/baseline-server \
     --to   ~/valheim-shadows/src/server-live \
     --out  docs/engine-sweeps/<date>-<what>.md --all-types
```

## Confirmed independently from Linux, 2026-09-07

**The installed client and server are the same build on both platforms**, checked directly rather
than assumed: this box's Steam client build id (`21981559`) and dedicated server build id
(`21981590`) match Don's Windows numbers exactly, `Version.CurrentVersion` / `m_networkVersion` /
`m_playerVersion` / `m_worldVersion` read the same 0.221.12 / 36 / 43 / 37 off both assemblies, and
running `tools/diff-engine.js` against a fresh Linux decompile of both installs reproduces the same
six body-changed members Don's Windows sweep found, word for word
(`docs/engine-sweeps/2026-09-07-linux-baseline-client-vs-server.md`). One thing this box's client
build carries that the Windows one does not — `Version.GetPlatform()` here resolves
`Platforms.SteamLinux`, and the client's `assembly_valheim` decompiles to one MORE type file
(603 against Don's 602) because `<PrivateImplementationDetails>`, a compiler scratch type named
nowhere in this mod, happens to land in the Linux client build and not the Windows one. Cosmetic;
see that sweep report for the full accounting.

**The `public-test` branch still does not exist**, confirmed a second way: an anonymous
`steamcmd +app_info_print 896660` from this box lists the same six branches Don's Windows run found
(`public`, `default_old`, `default_preal`, `default_prebw`, `default_precta`, `default_preml`), no
`public-test` among them. Not a fetch that quietly failed on one platform — the branch is not there
today, checked twice, on two different operating systems.

## The sweeps taken against this baseline

- `docs/engine-sweeps/2026-09-07-baseline-client-vs-server.md` — the complete list of differences
  between the two builds THE baseline is made of, and the verdict on the design's one-difference
  assertion.
- `docs/engine-sweeps/2026-09-07-server-live-vs-baseline.md` — the live server against the installed
  one. Zero differences; the tool's own null test.
- `docs/engine-sweeps/2026-09-07-linux-baseline-client-vs-server.md` — the same client-vs-server
  sweep, run through the `.sh` tools on Linux instead of the `.ps1` ones on Windows, against the
  manifest as it stood after P10a picked up P5's rows. Same six differences; zero manifest problems
  on all 257 rows.
