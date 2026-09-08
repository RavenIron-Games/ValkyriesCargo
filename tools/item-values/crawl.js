// Pull every non-redirect article of the Valheim fandom wiki (ns 0) with its wikitext, index by the
// infobox's `| id = <prefab>` field, and write one file per candidate prefab for the agents to read.
const fs = require('fs'); const path = require('path');
const S = process.argv[2]; const candidates = process.argv[3];
const UA = 'ValkyriesCargo-item-audit/1.0 (RavenIron-Games; contact via GitHub)';
const base = 'https://valheim.fandom.com/api.php';
async function get(params) {
  const u = base + '?' + new URLSearchParams({ format: 'json', formatversion: '2', ...params }).toString();
  for (let attempt = 0; attempt < 5; attempt++) {
    try {
      const r = await fetch(u, { headers: { 'User-Agent': UA } });
      if (r.status === 429 || r.status >= 500) { await new Promise(x => setTimeout(x, 2000 * (attempt + 1))); continue; }
      return await r.json();
    } catch (e) { await new Promise(x => setTimeout(x, 2000 * (attempt + 1))); }
  }
  throw new Error('gave up on ' + u);
}
(async () => {
  const pages = {}; let cont = {}; let batches = 0;
  for (;;) {
    const j = await get({ action: 'query', generator: 'allpages', gapnamespace: '0', gaplimit: '50', gapfilterredir: 'nonredirects',
                          prop: 'revisions', rvprop: 'content', rvslots: 'main', ...cont });
    batches++;
    for (const p of (j.query && j.query.pages) || []) {
      const c = p.revisions && p.revisions[0] && p.revisions[0].slots && p.revisions[0].slots.main && p.revisions[0].slots.main.content;
      if (typeof c === 'string') pages[p.title] = c;
    }
    if (!j.continue) break;
    cont = j.continue;
    await new Promise(x => setTimeout(x, 300));
  }
  fs.writeFileSync(path.join(S, 'pages.json'), JSON.stringify(pages));
  // one file per page, for grep
  const safe = t => t.replace(/[\/:*?"<>|]/g, '_');
  for (const [t, c] of Object.entries(pages)) fs.writeFileSync(path.join(S, 'pages', safe(t) + '.txt'), c);
  // index by infobox id(s)
  const byId = {};
  for (const [t, c] of Object.entries(pages)) {
    const re = /\|\s*id\d*\s*=\s*([^\n|}]+)/g; let m;
    while ((m = re.exec(c))) for (const id of m[1].split(/[,;\/]|<br\s*\/?>|\s+/)) { const k = id.trim(); if (k && /^[A-Za-z0-9_]+$/.test(k)) (byId[k] = byId[k] || []).push(t); }
  }
  fs.writeFileSync(path.join(S, 'by-id.json'), JSON.stringify(byId, null, 1));
  // per candidate prefab
  const rows = fs.readFileSync(candidates, 'utf8').split(/\r?\n/).filter(Boolean).map(l => l.split('\t'));
  let hit = 0, miss = [];
  const lower = {}; for (const k of Object.keys(byId)) lower[k.toLowerCase()] = k;
  for (const r of rows) {
    const prefab = r[0]; const key = byId[prefab] ? prefab : lower[prefab.toLowerCase()];
    if (key) { hit++; const titles = byId[key]; fs.writeFileSync(path.join(S, 'by-prefab', prefab + '.txt'), titles.map(t => '==== WIKI PAGE: ' + t + ' ====\n' + pages[t]).join('\n\n')); }
    else miss.push(prefab);
  }
  fs.writeFileSync(path.join(S, 'missing.txt'), miss.join('\n'));
  console.log('batches ' + batches + ', pages ' + Object.keys(pages).length + ', ids ' + Object.keys(byId).length + ', candidates ' + rows.length + ', matched ' + hit + ', missing ' + miss.length);
})().catch(e => { console.error(e); process.exit(1); });
