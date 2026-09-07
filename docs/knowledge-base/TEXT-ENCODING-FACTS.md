# Text Encoding Facts — the CP1252 trap

**Applies to every project in `c:\WubarrkCODING`.** Written 2026-08-06 after it nearly shipped in
VikingOS 0.8.2, and after finding it had already damaged files in this very folder.

---

## The trap

**Windows PowerShell 5.1 reads and writes CP1252 unless explicitly told otherwise.** Two independent
consequences, both silent:

1. **It reads a `.ps1` with no BOM as ANSI.** A script containing `☺` is corrupted *before it executes* —
   the literal in memory is already wrong. So a script written to fix encoding can be the thing that
   breaks it.
2. **`Set-Content`, `Out-File`, `>` and `>>` rewrite UTF-8 files as CP1252, usually adding a BOM.** One
   pass over a source tree re-encodes every non-ASCII character in it.

The corruption is: UTF-8 bytes decoded as CP1252, then re-encoded as UTF-8. `—` (`E2 80 94`) becomes
`â€"`. `·` (`C2 B7`) becomes `Â·`.

**Nothing catches it.** It compiles clean with zero warnings. It is valid UTF-8 afterwards, so no tool
complains. It is only visible if you look at the characters, and a diff full of prose is exactly where
nobody looks closely.

## What it cost in VikingOS 0.8.2

Every one of these was a live on-screen label, already built, deployed, packaged **and mirrored to the
dedicated server** before anyone noticed:

| Intended | Shipped as | Where |
|---|---|---|
| `☺ Emoji` | `â˜º Emoji` | chat strip button |
| `↩ Reply` | `â†© Reply` | chat strip button |
| `⇄ Trade` | `â‡„ Trade` | chat strip button |
| `…` | `â€¦` | text truncation |
| `TRADE — NAME` | `TRADE â€” NAME` | trade window title |
| `✔ Confirmed` | `âœ" Confirmed` | trade confirm button |
| `×` | `Ã—` | close button |
| `Day 12 · 14:30` | `Day 12 Â· 14:30` | HUD clock |

Plus 38 lines of the player-facing `CHANGELOG.md`. It was caught by reading a diff before a commit, not
by any test.

## Already damaged in `libs-Tools` (2026-08-06)

Confirmed by direct reading, not yet repaired:

- `IMPLEMENTATIONS/SharedInfrastructure.md` — em-dashes throughout are `â€"`
- `VALHEIM-DEDICATED-SERVER-FACTS.md` — `→` is `â†'`, `×` is `Ã—`, `≤` is `â‰¤`, em-dashes as above

A wider sweep of `libs-Tools` and the other project folders has **not** been done. Assume more.

---

## Rules

1. **Prefer the editing tools over any shell rewrite.** They write UTF-8 correctly. A bulk `Set-Content`
   pass over source files is almost never worth its risk.
2. **Keep `.ps1` files pure ASCII.** Spell awkward characters by codepoint — `[char]0x263A`, not `☺`.
   This is not fussiness; a non-ASCII literal in a BOM-less script is already wrong at parse time.
3. **Read and write project files only through .NET, with an explicit encoding:**
   ```powershell
   $bytes = [System.IO.File]::ReadAllBytes($path)
   $text  = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($bytes)   # strict
   # ...
   [System.IO.File]::WriteAllBytes($path, (New-Object System.Text.UTF8Encoding($false)).GetBytes($text))
   ```
   `UTF8Encoding($false)` = no BOM. `($false, $true)` also throws on invalid input instead of silently
   substituting `U+FFFD`.
4. **Match the file's existing BOM state.** Don't add or strip one as a side effect — it is pure diff
   noise and it hides the real change. Most files here have no BOM; a few `.csproj`/`Plugin.cs` do.
5. **Verify the fix reached the binary, not just the source.** .NET string literals live in the `#US`
   heap as UTF-16, so searching the built DLL for the correct codepoint *and the absence of the corrupt
   one* is a genuine end-to-end check.

---

## Detecting it

Do **not** grep for `â` and friends — you will miss cases and flag clean files.

Detect it structurally: **mojibake is text that survives a CP1252 encode followed by a UTF-8 decode.**
Genuine UTF-8 does not — `—` encodes to the single byte `0x97`, which is not valid UTF-8 and throws.

**Both codecs must use exception fallbacks.** A lenient CP1252 encoder maps anything outside its
repertoire to `?`, which then decodes as valid UTF-8 — that reports every clean file containing a real
symbol (`✦`, `→`) as corrupt. This false positive is easy to fall for.

```powershell
# ASCII-ONLY BY NECESSITY - see rule 2.
# Usage: .\scan.ps1 -Root 'c:\WubarrkCODING\libs-Tools'
param([Parameter(Mandatory=$true)][string]$Root,
      [string[]]$Include = @('*.md','*.cs','*.ps1','*.json','*.csproj','*.txt'),
      [int]$MaxSizeMB = 8)
$ErrorActionPreference = 'Stop'

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$cp1252 = [System.Text.Encoding]::GetEncoding(1252,
    (New-Object System.Text.EncoderExceptionFallback),
    (New-Object System.Text.DecoderExceptionFallback))

foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Include $Include -EA SilentlyContinue) {
    if ($file.Length -gt ($MaxSizeMB * 1MB)) { continue }
    try { $text = $utf8.GetString([System.IO.File]::ReadAllBytes($file.FullName)) } catch { continue }

    $hits = 0
    foreach ($line in ($text.TrimStart([char]0xFEFF) -split "`r?`n")) {
        if ($line.Length -eq 0) { continue }
        try { if ($utf8.GetString($cp1252.GetBytes($line)) -ne $line) { $hits++ } } catch { }
    }
    if ($hits -gt 0) { "{0,6} lines  {1}" -f $hits, $file.FullName }
}
```

## Repairing it

Reverse per **line**, not per file: a file that mixes good and corrupt text (a doc edited twice) is
common, and a whole-file transform would destroy the good half. Rewrite a line only when the reverse both
**succeeds** and **changes** something. Operating on runs of non-newline characters via
`[regex]::Replace($text, '[^\r\n]+', $evaluator)` leaves every line terminator untouched, so CRLF/LF is
preserved and the diff stays honest.

**Always dry-run first**, printing `U+XXXX` codepoints before and after — the glyphs themselves cannot be
trusted to survive a console round-trip, and the codepoints prove what actually happened.

A working scan/dry-run/apply implementation lives in the VikingOS session scratchpad; the essentials are
above and are short enough to rewrite from this page.
