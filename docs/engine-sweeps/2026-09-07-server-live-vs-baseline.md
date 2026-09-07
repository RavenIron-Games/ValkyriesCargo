# Sweep: the live dedicated server against the baseline dedicated server

**2026-09-07.** Shadow build `server-live`, app 896660, branch `public`, fetched with steamcmd into
`C:\Users\donfr\valheim-shadows\server-live`, against the installed dedicated server that
`docs/ENGINE-BASELINE.md` names as half of THE baseline.

```
node tools/diff-engine.js --surface docs/ENGINE-SURFACE.md ^
     --from C:\Users\donfr\valheim-shadows\src\baseline-server ^
     --to   C:\Users\donfr\valheim-shadows\src\server-live ^
     --out  %TEMP%\server-live-vs-baseline.md --all-types
```

**Point `--out` somewhere else, not at this file** — the tool's report is generated and this one is
that report plus the reading of it.

```
surface 244: 244 unchanged, 0 body changed, 0 signature changed, 0 gone, 0 manifest problem(s)
types: 0 of 697 differ, 0 only in from, 0 only in to
exit 0
```

## Nothing moved, and that was expected

| | build id | game | net | player | world |
|---|---|---|---|---|---|
| installed dedicated server | 21981590 | 0.221.12 | 36 | 43 | 37 |
| shadow `server-live` | 21981590 | 0.221.12 | 36 | 43 | 37 |

**Same Steam build id, and the three assemblies are byte-identical:**

    assembly_valheim.dll    SHA-256 84A1B34F95774D36...  identical
    assembly_utils.dll      SHA-256 41F66746B80B3B30...  identical
    assembly_guiutils.dll   SHA-256 D46CAD5BFE2D384F...  identical

The installed server had not drifted from the live branch, so **no `body changed` member was found
and there is no engine fact to re-check.** Every claim in `CLAUDE.md` "Engine facts the code relies
on today" and `docs/DESIGN.md` §0 that was true against the installed server is true against the
live branch, because they are the same bytes.

## What this sweep is actually worth

Two things, neither of them nothing.

**It dates the baseline against the live branch.** `docs/ENGINE-BASELINE.md` can now say the mod was
written against the build Iron Gate is shipping *today*, rather than against whatever happened to be
on this machine. The installed server was written on 2026-08-25 and the live branch was last pushed
on the same build id; the baseline is current.

**It is the tool's null test.** `tools/diff-engine.js` reports 0 differing files out of 697 and 244
of 244 surface members unchanged, on two trees produced by two separate `ilspycmd` runs over two
separate copies of the same DLL. A diff tool that invents differences would have shown them here —
whitespace, member ordering, generated names — and it showed none. The same run against the client
(`2026-09-07-baseline-client-vs-server.md`) then finds 40 differing files and six surface members,
so the filter is not simply blind either.

## Why there is no `server-test` companion to this file

There is no `public-test` branch on either Valheim app today. Both advertise exactly six branches
(`public`, `default_old`, `default_preal`, `default_prebw`, `default_precta`, `default_preml`) and
`public-test` is not among them; steamcmd's answer to `-beta public-test` is
`ERROR! Failed to set beta 'public-test'` and nothing else. `tools/fetch-builds.ps1` now asks the app
what it has before spending a download, and skips with the list rather than failing with that line.
`docs/ENGINE-BASELINE.md` has the full branch table.

A playtest branch exists only while a playtest is running. **This is the good case:** the baseline is
captured with nothing pending, which is exactly the position the brief asked for. When Iron Gate
opens the next public test, `.\tools\fetch-builds.ps1 -Branch public-test` finds it, and the sweep
after it is a command rather than a re-derivation.

## The client half

Not fetched: app 892970 needs an account that owns Valheim, and only the owner types that login
(`.\tools\fetch-builds.ps1 -Which client -Account <name>`; steamcmd asks for the password itself and
the script has no parameter to put one in). It would cost little: the installed client is at build id
21981559, which **is** the live client build, so a `client-live` shadow would return the same zero.
