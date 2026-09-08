// Quality gate over the agents' assessed-<slice>.json files: did the assessor open its wiki pages?
//   node qc.js <scratch>   -> one line per slice, FAIL when the pass looks like placeholders
const fs = require('fs'); const path = require('path');
const S = process.argv[2]; const items = path.join(S, 'items');
const strip = s => s.replace(/^﻿/, '');
const slices = fs.readdirSync(items).filter(f => /^slice-/.test(f) && !/\.json$/.test(f)).sort();
const fails = [];
for (const s of slices) {
  const f = path.join(items, 'assessed-' + s + '.json');
  const lines = fs.readFileSync(path.join(items, s), 'utf8').split(/\r?\n/).filter(Boolean).length;
  if (!fs.existsSync(f)) { console.log(s.padEnd(24) + ' MISSING (' + lines + ' rows expected)'); fails.push(s); continue; }
  let rows; try { rows = JSON.parse(strip(fs.readFileSync(f, 'utf8'))).rows || []; } catch (e) { console.log(s.padEnd(24) + ' UNREADABLE ' + e.message); fails.push(s); continue; }
  let have = 0, ignored = 0, unknown = 0, low = 0, placeholder = 0, excluded = 0;
  for (const r of rows) {
    const has = fs.existsSync(path.join(S, 'wiki', 'by-prefab', r.prefab + '.txt'));
    if (has) { have++; if (/^none$/i.test(r.wiki_page || '')) ignored++; }
    if (r.source === 'unknown' || r.source === undefined) unknown++;
    if (r.confidence === 'low') low++;
    if (!r.player_item) excluded++;
    if (r.player_item && r.tier === 0 && r.rarity === 2 && r.complexity === 1 && /^none$/i.test(r.wiki_page || '')) placeholder++;
  }
  const bad = rows.length !== lines || (have > 0 && ignored / have > 0.3) || unknown / Math.max(1, rows.length) > 0.2 || placeholder / Math.max(1, rows.length) > 0.3;
  if (bad) fails.push(s);
  console.log(s.padEnd(24) + (bad ? ' FAIL ' : ' ok   ') + 'rows ' + rows.length + '/' + lines + '  pages ' + have + ' ignored ' + ignored + '  unknown ' + unknown + '  low ' + low + '  placeholder ' + placeholder + '  excluded ' + excluded + (fs.existsSync(path.join(items, 'corrections-' + s + '.json')) ? '  [corrections]' : ''));
}
console.log('FAILS: ' + (fails.join(' ') || 'none'));
