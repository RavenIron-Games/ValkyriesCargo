// Merge the agents' assessed-<slice>.json and corrections-<slice>.json into the value table.
// The same formula as the workflow script, so the table on disk and the calibration pass agree.
//   node merge.js <scratch> <anchors.txt> <out.tsv> <report.json>
const fs = require('fs'); const path = require('path');
const [S, anchorsFile, outTsv, reportJson] = process.argv.slice(2);
const items = path.join(S, 'items');
const strip = s => s.replace(/^﻿/, '');   // the agents' Write tool puts a BOM on the file

const anchors = {};
for (const line of fs.readFileSync(anchorsFile, 'utf8').split(/\r?\n/)) { const p = line.trim().split(':'); if (p.length >= 5) anchors[p[0]] = { base: parseInt(p[1], 10), target: parseInt(p[2], 10), max: parseInt(p[3], 10), kind: p[4] }; }

const ints = ['tier', 'station_level', 'ingredient_types', 'ingredient_units', 'deepest_ingredient_tier', 'trader_price', 'rarity', 'complexity', 'proposed_value'];
const bools = ['player_item', 'boss_gated', 'limited'];
function coerce(field, v) {
  if (ints.includes(field)) { const n = parseInt(String(v).trim(), 10); return isNaN(n) ? null : n; }
  if (bools.includes(field)) { const s = String(v).trim().toLowerCase(); return s === 'true' || s === 'yes' || s === '1'; }
  return String(v);
}
const T = { 0: 2, 1: 3, 2: 6, 3: 12, 4: 24, 5: 45, 6: 85, 7: 160, 8: 280 };
const R = { 1: 1.0, 2: 1.4, 3: 2.2, 4: 3.5, 5: 5.5 };
const C = { 1: 1.0, 2: 1.6, 3: 2.5, 4: 3.6, 5: 5.0 };
const clampInt = (v, lo, hi) => Math.max(lo, Math.min(hi, parseInt(v, 10) || lo));
function valueOf(r) {
  const t = clampInt(r.tier, 0, 8), ra = clampInt(r.rarity, 1, 5), co = clampInt(r.complexity, 1, 5);
  const ultra = r.proposed_class === 'ultra' && ((ra >= 4 && t >= 4) || (co === 5 && t >= 6) || (r.boss_gated && r.limited && ra >= 4));
  if (ultra) return { value: Math.min(1200, Math.max(800, 800 + 60 * (t - 4) + 40 * (ra - 4) + 40 * (co - 4))), cls: 'ultra' };
  const v = Math.min(600, Math.max(2, Math.round(T[t] * R[ra] * C[co])));
  return { value: v, cls: (v <= 30 && ra <= 2) ? 'common' : 'rare' };
}

const slices = fs.readdirSync(items).filter(f => /^slice-/.test(f) && !/\.json$/.test(f)).sort();
const rows = []; const applied = []; const missing = []; const badSlices = [];
for (const s of slices) {
  const aPath = path.join(items, 'assessed-' + s + '.json');
  if (!fs.existsSync(aPath)) { badSlices.push(s + ' (no assessed file)'); continue; }
  let a; try { a = JSON.parse(strip(fs.readFileSync(aPath, 'utf8'))); } catch (e) { badSlices.push(s + ' (assessed JSON unreadable: ' + e.message + ')'); continue; }
  const byPrefab = {};
  for (const r of (a.rows || [])) { r.slice = s; byPrefab[r.prefab] = r; }
  // every input line must have a row
  for (const line of fs.readFileSync(path.join(items, s), 'utf8').split(/\r?\n/).filter(Boolean)) {
    const prefab = line.split('\t')[0];
    if (!byPrefab[prefab]) missing.push(s + '\t' + prefab);
  }
  const cPath = path.join(items, 'corrections-' + s + '.json');
  if (fs.existsSync(cPath)) {
    try {
      const c = JSON.parse(strip(fs.readFileSync(cPath, 'utf8')));
      for (const k of (c.corrections || [])) {
        const r = byPrefab[k.prefab]; if (!r || !(k.field in r)) continue;
        const nv = coerce(k.field, k.new_value); if (nv === null) continue;
        applied.push({ slice: s, prefab: k.prefab, field: k.field, old: r[k.field], new: nv, reason: k.reason });
        r[k.field] = nv;
      }
    } catch (e) { badSlices.push(s + ' (corrections JSON unreadable: ' + e.message + ')'); }
  } else badSlices.push(s + ' (no corrections file)');
  for (const r of Object.values(byPrefab)) rows.push(r);
}
for (const r of rows) {
  const v = valueOf(r);
  r.value_formula = v.value;
  r.anchor = anchors[r.prefab] ? anchors[r.prefab].base : '';
  r.value = r.anchor !== '' ? r.anchor : v.value;
  r.cls = v.cls === 'ultra' ? 'ultra' : ((r.value <= 30 && clampInt(r.rarity, 1, 5) <= 2) ? 'common' : 'rare');
}
const order = { material: 0, consumable: 1, weapons: 2, armor: 3, gear: 4, trophy: 5 };
const fam = s => s.replace(/^slice-/, '').replace(/-\d+$/, '');
rows.sort((a, b) => (order[fam(a.slice)] - order[fam(b.slice)]) || (a.player_item === b.player_item ? 0 : a.player_item ? -1 : 1) || (a.tier - b.tier) || (a.value - b.value) || a.prefab.localeCompare(b.prefab));

const cols = ['prefab', 'name', 'family', 'player_item', 'tier', 'biome', 'source', 'station', 'station_level', 'ingredients', 'ingredient_types', 'ingredient_units', 'drop_rate', 'boss_gated', 'limited', 'trader_price', 'rarity', 'complexity', 'value', 'class', 'anchor', 'value_formula', 'proposed_value', 'confidence', 'wiki_page', 'exclude_reason', 'note'];
const clean = v => String(v === undefined || v === null ? '' : v).replace(/[\t\r\n]+/g, ' ').trim();
const out = [cols.join('\t')];
for (const r of rows) out.push(cols.map(c => c === 'family' ? fam(r.slice) : c === 'class' ? r.cls : clean(r[c])).join('\t'));
fs.writeFileSync(outTsv, out.join('\n') + '\n');

// the report: anchor fit, class counts, band counts
const players = rows.filter(r => r.player_item);
const fit = [];
for (const r of players) if (r.anchor !== '') fit.push({ prefab: r.prefab, anchor: r.anchor, formula: r.value_formula, ratio: +(r.value_formula / Math.max(1, r.anchor)).toFixed(2), tier: r.tier, rarity: r.rarity, complexity: r.complexity });
const within = fit.filter(f => f.ratio >= 0.5 && f.ratio <= 2).length, within3 = fit.filter(f => f.ratio >= 0.33 && f.ratio <= 3).length;
const count = (arr, f) => arr.reduce((m, r) => { const k = f(r); m[k] = (m[k] || 0) + 1; return m; }, {});
const report = {
  slices: slices.length, rows: rows.length, players: players.length, excluded: rows.length - players.length,
  missingRows: missing, badSlices, correctionsApplied: applied.length,
  byClass: count(players, r => r.cls), byTier: count(players, r => r.tier), byFamily: count(players, r => fam(r.slice)),
  bands: { 'value<=30': players.filter(r => r.value <= 30).length, '31-100': players.filter(r => r.value > 30 && r.value <= 100).length, '101-300': players.filter(r => r.value > 100 && r.value <= 300).length, '301-600': players.filter(r => r.value > 300 && r.value <= 600).length, 'ultra 800-1200': players.filter(r => r.value >= 800).length },
  anchorFit: { anchored: fit.length, within2x: within, within3x: within3, worst: fit.sort((a, b) => Math.abs(Math.log(b.ratio)) - Math.abs(Math.log(a.ratio))).slice(0, 15) },
  lowConfidence: players.filter(r => r.confidence === 'low').length,
  noWiki: players.filter(r => !r.wiki_page || /^none$/i.test(r.wiki_page)).length,
  ultra: players.filter(r => r.cls === 'ultra').map(r => r.prefab + '=' + r.value),
  applied,
};
fs.writeFileSync(reportJson, JSON.stringify(report, null, 1));
console.log(JSON.stringify({ slices: slices.length, rows: rows.length, players: players.length, excluded: rows.length - players.length, missing: missing.length, badSlices, corrections: applied.length, byClass: report.byClass, bands: report.bands, anchorFit: { anchored: fit.length, within2x: within, within3x: within3 }, lowConfidence: report.lowConfidence, noWiki: report.noWiki, ultra: report.ultra.length }, null, 1));
