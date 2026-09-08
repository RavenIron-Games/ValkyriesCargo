// Markdown snippets for docs/ITEM-VALUES.md, computed from the final table so the document never drifts from it.
//   node snippets.js <final.tsv>
const fs = require('fs');
const lines = fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/).filter(Boolean);
const h = lines[0].split('\t');
const rows = lines.slice(1).map(l => { const c = l.split('\t'); const o = {}; h.forEach((k, i) => o[k] = c[i]); return o; });
const players = rows.filter(r => r.player_item === 'true');
const n = arr => arr.length;
const count = (arr, f) => arr.reduce((m, r) => { const k = f(r); m[k] = (m[k] || 0) + 1; return m; }, {});
const out = [];
out.push('## STATS');
out.push(`rows ${n(rows)}; player items ${n(players)}; excluded ${n(rows) - n(players)}`);
out.push('how: ' + JSON.stringify(count(players, r => r.how)));
out.push('class: ' + JSON.stringify(count(players, r => r.class)));
const band = v => v >= 800 ? 'ultra 800–1200' : v <= 30 ? '2–30' : v <= 100 ? '31–100' : v <= 300 ? '101–300' : '301–600';
out.push('bands: ' + JSON.stringify(count(players, r => band(+r.value))));
out.push('confidence: ' + JSON.stringify(count(players, r => r.confidence)) + '; no wiki page: ' + n(players.filter(r => /^none$/i.test(r.wiki_page || ''))));
out.push('\n## FAMILY TABLE');
out.push('| family | items | 2–30 | 31–100 | 101–300 | 301–600 | ultra | lowest | highest |');
out.push('|---|---|---|---|---|---|---|---|---|');
for (const fam of ['material', 'consumable', 'weapons', 'armor', 'gear', 'trophy']) {
  const f = players.filter(r => r.family === fam).sort((a, b) => +a.value - +b.value);
  const b = count(f, r => band(+r.value));
  out.push(`| ${fam} | ${n(f)} | ${b['2–30'] || 0} | ${b['31–100'] || 0} | ${b['101–300'] || 0} | ${b['301–600'] || 0} | ${b['ultra 800–1200'] || 0} | ${f[0].name} ${f[0].value} | ${f[f.length - 1].name} ${f[f.length - 1].value} |`);
}
out.push('\n## ULTRA TABLE');
out.push('| prefab | item | tier | why | value |');
out.push('|---|---|---|---|---|');
for (const r of players.filter(r => r.class === 'ultra').sort((a, b) => +b.value - +a.value || a.prefab.localeCompare(b.prefab)))
  out.push(`| \`${r.prefab}\` | ${r.name} | ${r.tier} | ${/^Trophy/.test(r.prefab) ? 'boss trophy' : +r.complexity >= 5 ? 'infused Ashlands craft, a rare gem in it' : 'boss-unique drop or reward'} | ${r.value} |`);
out.push('\n## CAP LIST');
out.push(players.filter(r => +r.value === 600).map(r => '`' + r.prefab + '`').join(', '));
out.push('\n## SCORE LIST (no resolvable recipe; priced by tier × rarity × complexity)');
out.push(players.filter(r => r.how === 'score').map(r => '`' + r.prefab + '` ' + r.value).join(', '));
out.push('\n## TRADER LIST (a quarter of the shop price was the floor)');
out.push(players.filter(r => r.how === 'trader').map(r => '`' + r.prefab + '` ' + r.value + ' (' + r.trader_price + ')').join(', '));
out.push('\n## LADDER (the metals)');
for (const p of ['CopperOre', 'TinOre', 'Copper', 'Tin', 'Bronze', 'IronScrap', 'Iron', 'SilverOre', 'Silver', 'BlackMetalScrap', 'BlackMetal', 'FlametalOreNew', 'FlametalNew']) { const r = rows.find(x => x.prefab === p); if (r) out.push(`${p} ${r.value} (${r.how})`); }
out.push('\n## SAMPLE LADDERS');
const lad = (label, list) => out.push(label + ': ' + list.map(p => { const r = rows.find(x => x.prefab === p); return r ? `${r.name} ${r.value}` : p + ' ?'; }).join(' · '));
lad('swords', ['SwordBronze', 'SwordIron', 'SwordSilver', 'SwordBlackmetal', 'SwordMistwalker', 'SwordNiedhogg', 'SwordNiedhoggBlood']);
lad('axes', ['AxeFlint', 'AxeBronze', 'AxeIron', 'AxeBlackMetal', 'AxeJotunBane', 'AxeBerzerkr']);
lad('chests', ['ArmorRagsChest', 'ArmorLeatherChest', 'ArmorTrollLeatherChest', 'ArmorBronzeChest', 'ArmorIronChest', 'ArmorRootChest', 'ArmorWolfChest', 'ArmorPaddedCuirass', 'ArmorCarapaceChest', 'ArmorMageChest', 'ArmorFlametalChest']);
lad('bows', ['Bow', 'BowFineWood', 'BowHuntsman', 'BowDraugrFang', 'BowSpineSnap', 'BowAshlands']);
lad('food', ['CookedMeat', 'CookedDeerMeat', 'Sausages', 'SerpentStew', 'LoxPie', 'Bread', 'MisthareSupreme', 'FeastMistlands']);
lad('meads', ['MeadTasty', 'MeadHealthMinor', 'MeadHealthMedium', 'MeadHealthMajor', 'MeadEitrLingering', 'MeadBzerker']);
lad('trophies', ['TrophyBoar', 'TrophyGreydwarf', 'TrophyTroll', 'TrophyWolf', 'TrophyLox', 'TrophySeeker', 'TrophyMorgen', 'TrophyEikthyr', 'TrophyFader']);
lad('gathers by tier', ['Wood', 'Resin', 'TrollHide', 'Guck', 'Obsidian', 'LoxPelt', 'Sap', 'AskHide', 'CelestialFeather']);
out.push('\n## EXCLUDED (not player items), by reason');
const ex = count(rows.filter(r => r.player_item !== 'true'), r => (r.exclude_reason || '').split(/[;:.(]/)[0].trim().toLowerCase().slice(0, 40));
out.push(Object.entries(ex).sort((a, b) => b[1] - a[1]).slice(0, 12).map(e => `${e[1]}× ${e[0] || '(no reason given)'}`).join('; '));
console.log(out.join('\n'));
