# tools/item-values — how docs/data/item-values-2026-09-08.tsv was made

The scripts behind `docs/ITEM-VALUES.md`, in the order they ran. They need Node 24 and a scratch directory `S`
holding `S/items/` (the candidate slices, the agents' `assessed-<slice>.json` and `corrections-<slice>.json`)
and `S/wiki/` (the crawl).

1. `node crawl.js S/wiki S/items/candidates.tsv` — pulls every article of valheim.fandom.com through its MediaWiki
   API (about 50 requests; be polite, it sleeps 300 ms), indexes them by the infobox `id` field, writes one file
   per candidate prefab. The drop rows and trophy links were indexed from the same pages (see docs/ITEM-VALUES.md §1).
2. The assessment workflows (Haiku agents; the scripts are in the session that ran them and are described in
   docs/ITEM-VALUES.md §4) write `assessed-*.json` and `corrections-*.json`.
3. `node qc.js S` — the two quality gates: did the assessor open its pages, and is the score distribution flat.
4. `node merge.js S S/items/anchors.txt S/items/item-values-raw.tsv S/items/report-raw.json` — the slices plus the
   skeptics' corrections into one table (`docs/data/item-assessments-2026-09-08.tsv` is that file, less its first-draft `value`, `class` and `value_formula` columns, which `cost.js` supersedes).
5. `node cost.js S --write S/items/item-values-final.tsv` — the recipe resolution and the four rules of
   docs/ITEM-VALUES.md §3; the table (`docs/data/item-values-2026-09-08.tsv`). A new formula is an edit here and
   a re-run, not a re-assessment.
6. `node snippets.js S/items/item-values-final.tsv` — the document's numbers.
