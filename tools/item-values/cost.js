// Second pass over item-values-raw.tsv: resolve every recipe's ingredient names to prefabs, then price
// crafted goods by their inputs. Usage: node cost.js <scratch> [--write <out.tsv> <report.json>]
const fs = require('fs');
const S = process.argv[2];
const writeIdx = process.argv.indexOf('--write');
const lines = fs.readFileSync(S + '/items/item-values-raw.tsv', 'utf8').split(/\r?\n/).filter(Boolean);
const h = lines[0].split('\t');
const rows = lines.slice(1).map(l => { const c = l.split('\t'); const o = {}; h.forEach((k, i) => o[k] = c[i]); return o; });
const byPrefab = {}; for (const r of rows) byPrefab[r.prefab] = r;

// ---- name -> prefab -------------------------------------------------------------------------------
const byId = JSON.parse(fs.readFileSync(S + '/wiki/by-id.json', 'utf8'));
const norm = s => String(s || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
const nameTo = {};
for (const [id, titles] of Object.entries(byId)) for (const t of titles) if (!nameTo[norm(t)]) nameTo[norm(t)] = id;
for (const r of rows) { const n = norm(r.name); if (n && !nameTo[n]) nameTo[n] = r.prefab; nameTo[norm(r.prefab)] = r.prefab; }
const alias = {
  'scrap iron': 'IronScrap', 'iron scrap': 'IronScrap', 'ancient bark': 'ElderBark', 'elder bark': 'ElderBark', 'fine wood': 'FineWood',
  'finewood': 'FineWood', 'core wood': 'RoundLog', 'corewood': 'RoundLog', 'round log': 'RoundLog', 'leather scraps': 'LeatherScraps',
  'deer hide': 'DeerHide', 'troll hide': 'TrollHide', 'wolf pelt': 'WolfPelt', 'lox pelt': 'LoxPelt', 'black metal': 'BlackMetal',
  'black metal scrap': 'BlackMetalScrap', 'blackmetal': 'BlackMetal', 'flametal': 'FlametalNew', 'flametal ore': 'FlametalOreNew',
  'copper ore': 'CopperOre', 'tin ore': 'TinOre', 'silver ore': 'SilverOre', 'surtling core': 'SurtlingCore', 'black core': 'BlackCore',
  'yggdrasil wood': 'YggdrasilWood', 'refined eitr': 'Eitr', 'eitr': 'Eitr', 'soft tissue': 'Softtissue', 'linen thread': 'LinenThread',
  'blue jute': 'JuteBlue', 'red jute': 'JuteRed', 'greydwarf eye': 'GreydwarfEye', 'bone fragments': 'BoneFragments', 'hard antler': 'HardAntler',
  'wolf fang': 'WolfFang', 'wolf claw': 'WolfClaw', 'freeze gland': 'FreezeGland', 'dragon tear': 'DragonTear', 'blood bag': 'Bloodbag',
  'raspberries': 'Raspberry', 'blueberries': 'Blueberries', 'cloudberries': 'Cloudberry', 'red mushroom': 'Mushroom', 'yellow mushroom': 'MushroomYellow',
  'blue mushroom': 'MushroomBlue', 'jotun puffs': 'MushroomJotunPuffs', 'magecap': 'MushroomMagecap', 'smoke puff': 'MushroomSmokePuff',
  'barley flour': 'BarleyFlour', 'bread dough': 'BreadDough', 'raw meat': 'RawMeat', 'boar meat': 'RawMeat', 'deer meat': 'DeerMeat',
  'wolf meat': 'WolfMeat', 'lox meat': 'LoxMeat', 'serpent meat': 'SerpentMeat', 'hare meat': 'HareMeat', 'chicken meat': 'ChickenMeat',
  'bug meat': 'BugMeat', 'volture meat': 'VoltureMeat', 'asksvin meat': 'AsksvinMeat', 'neck tail': 'NeckTail', 'raw fish': 'FishRaw',
  'fish': 'FishRaw', 'cooked meat': 'CookedMeat', 'cooked deer meat': 'CookedDeerMeat', 'cooked lox meat': 'CookedLoxMeat',
  'cooked wolf meat': 'CookedWolfMeat', 'egg': 'ChickenEgg', 'chicken egg': 'ChickenEgg', 'charcoal': 'Coal', 'bronze nails': 'BronzeNails',
  'iron nails': 'IronNails', 'ancient seed': 'AncientSeed', 'withered bone': 'WitheredBone', 'ymir flesh': 'YmirRemains', 'black marble': 'BlackMarble',
  'ceramic plate': 'CeramicPlate', 'charred bone': 'CharredBone', 'charred cogwheel': 'CharredCogwheel', 'molten core': 'MoltenCore',
  'proustite powder': 'ProustitePowder', 'sulfur stone': 'SulfurStone', 'sulfur': 'SulfurStone', 'ask hide': 'AskHide', 'asksvin hide': 'AskHide',
  'ask bladder': 'AskBladder', 'bjorn hide': 'BjornHide', 'celestial feather': 'CelestialFeather', 'morgen sinew': 'MorgenSinew',
  'morgen heart': 'MorgenHeart', 'bonemaw scale': 'BonemawSerpentScale', 'bonemaw tooth': 'BonemawSerpentTooth', 'bonemaw serpent meat': 'BoneMawSerpentMeat',
  'scale hide': 'ScaleHide', 'dvergr needle': 'DvergrNeedle', 'dvergr extractor': 'DvergrNeedle', 'mechanical spring': 'MechanicalSpring',
  'thunder stone': 'Thunderstone', 'vineberry cluster': 'Vineberry', 'fiddlehead fern': 'Fiddleheadfern', 'fiddlehead': 'Fiddleheadfern',
  'seeker aspic': 'SeekerAspic', 'royal jelly': 'RoyalJelly', 'fresh seaweed': 'FreshSeaweed', 'seaweed': 'FreshSeaweed', 'pungent pebbles': 'PungentPebbles',
  'fragrant bundle': 'FragrantBundle', 'candle wick': 'CandleWick', 'giant blood sack': 'GiantBloodSack', 'blob vial': 'BlobVial', 'bile bag': 'Bilebag',
  'queen bee': 'QueenBee', 'powdered dragon egg': 'PowderedDragonEgg', 'powdered dragon eggshells': 'PowderedDragonEgg', 'sharpening stone': 'SharpeningStone',
  'shield core': 'ShieldCore', 'bell fragment': 'BellFragment', 'fir cone': 'FirCone', 'pine cone': 'PineCone', 'beech seeds': 'BeechSeeds',
  'birch seeds': 'BirchSeeds', 'carrot seeds': 'CarrotSeeds', 'turnip seeds': 'TurnipSeeds', 'onion seeds': 'OnionSeeds', 'siege bomb': 'BombSiege',
  'cured squirrel hamstring': 'CuredSquirrelHamstring', 'undead bjorn ribcage': 'UndeadBjornRibcage', 'wolf hair bundle': 'WolfHairBundle',
  'asksvin carrion neck': 'AsksvinCarrionNeck', 'asksvin carrion pelvic': 'AsksvinCarrionPelvic', 'asksvin carrion ribcage': 'AsksvinCarrionRibcage',
  'asksvin carrion skull': 'AsksvinCarrionSkull', 'gemstone red': 'GemstoneRed', 'gemstone green': 'GemstoneGreen', 'gemstone blue': 'GemstoneBlue',
  'red gemstone': 'GemstoneRed', 'green gemstone': 'GemstoneGreen', 'blue gemstone': 'GemstoneBlue', 'bloodstone': 'GemstoneRed', 'iolite': 'GemstoneBlue',
  'jade': 'GemstoneGreen', 'dyrnwyn blade fragment': 'DyrnwynBladeFragment', 'dyrnwyn hilt fragment': 'DyrnwynHiltFragment', 'dyrnwyn tip fragment': 'DyrnwynTipFragment',
  'fader drop': 'FaderDrop', 'fader trophy': 'TrophyFader', 'yagluth drop': 'YagluthDrop', 'yagluth thing': 'YagluthDrop', 'torn spirit': 'YagluthDrop',
  'dvergr key fragment': 'DvergrKeyFragment', 'vegvisir': '', 'axe head': 'AxeHead1', 'scythe handle': 'ScytheHandle', 'barrel rings': 'BarrelRings',
  'mead base': '', 'tasty mead base': 'MeadBaseTasty', 'honey glazed chicken uncooked': 'HoneyGlazedChickenUncooked',
  // the twelve fish by their English names (the wiki's fish pages carry no id field)
  'perch': 'Fish1', 'pike': 'Fish2', 'tuna': 'Fish3', 'trollfish': 'Fish4_cave', 'grouper': 'Fish5', 'tetra': 'Fish6', 'giant herring': 'Fish7',
  'coral cod': 'Fish8', 'anglerfish': 'Fish9', 'northern salmon': 'Fish10', 'pufferfish': 'Fish11', 'magmafish': 'Fish12',
  'vulture meat': 'VoltureMeat', 'cooked seeker meat': 'CookedBugMeat', 'frost gland': 'FreezeGland', 'bone maw serpent meat': 'BoneMawSerpentMeat',
  'meat': 'RawMeat', 'drake trophy': 'TrophyHatchling', 'mead base berserk': 'MeadBaseBzerker', 'berserk mead base': 'MeadBaseBzerker',
  'mead base berserker': 'MeadBaseBzerker', 'cooked bonemaw serpent meat': 'CookedBoneMawSerpentMeat', 'cooked asksvin meat': 'CookedAsksvinMeat',
  'cooked bjorn meat': 'CookedBjornMeat', 'cooked hare meat': 'CookedHareMeat', 'cooked egg': 'CookedEgg', 'cooked volture meat': 'CookedVoltureMeat',
  'cooked serpent meat': 'SerpentMeatCooked', 'grilled neck tail': 'NeckTailGrilled', 'cooked fish': 'FishCooked', 'bread': 'Bread',
};
for (const [k, v] of Object.entries(alias)) if (v) nameTo[k] = v;
const prefabs = new Set(rows.map(r => r.prefab));
// Recipes the assessors left empty or wrong, filled by hand from the game (the well-known ones only; the
// rest stay on the score model and are listed as such in the report).
const handRecipes = {
  ArrowBronze: 'Wood x8, Bronze x1, Feathers x2', ArrowFire: 'Wood x8, Resin x8, Feathers x2', ArrowPoison: 'Wood x8, Obsidian x4, Feathers x2, Ooze x2',
  ArrowNeedle: 'Needle x4, Feathers x2', ArrowSilver: 'Wood x8, Silver x1, Feathers x2', ArrowObsidian: 'Wood x8, Obsidian x4, Feathers x2',
  BoltBlackmetal: 'Wood x8, BlackMetal x2, Feathers x2', Cultivator: 'RoundLog x5, Bronze x5', Bread: 'BreadDough x1', BreadDough: 'BarleyFlour x10',
  BarleyFlour: 'Barley x1',
  // the four Mistlands/Ashlands ammo rows the assessors left empty; quantities from memory, marked as such
  ArrowCarapace: 'Wood x8, Carapace x4, Feathers x2', BoltCarapace: 'Wood x8, Carapace x4, Feathers x2',
  ArrowCharred: 'Blackwood x8, CharredBone x2, Feathers x2', BoltCharred: 'Blackwood x8, CharredBone x2, Feathers x2',
  // two shields whose assessed recipe was the upgrade total (40 black metal), corrected to the level-1 recipe
  ShieldBlackmetal: 'FineWood x10, BlackMetal x8, Chain x1', ShieldBlackmetalTower: 'FineWood x15, BlackMetal x10, Chain x7',
};
// Values set by hand, with the reason, applied before anything else: Haldor sells an Egg for 1500 coins to start a
// farm, and a quarter of that would price every omelette at the cap; once hens lay, an egg is a Plains common.
const handValues = { ChickenEgg: 6 };
// The recipe straight from the wiki page's infobox (`| materials = * [[Wood]] x2 ...`), which is the LEVEL-1
// recipe; the assessors sometimes summed the upgrade table instead, which put every metal weapon at the cap.
let fromWiki = 0;
for (const r of rows) {
  const f = S + '/wiki/by-prefab/' + r.prefab + '.txt';
  if (!fs.existsSync(f) || +r.complexity < 2) continue;
  const page = fs.readFileSync(f, 'utf8');
  const m = page.match(/\|\s*materials\s*=\s*\n?((?:\s*\*[^\n]*\n?)+)/);
  if (!m) continue;
  const items = []; const seen = new Set();
  for (const line of m[1].split('\n')) {
    const mm = line.match(/\*\s*\[\[([^\]|]+)(?:\|[^\]]*)?\]\]\s*[x×]?\s*(\d+)?/);
    if (!mm) continue;
    const nm = mm[1].trim();
    if (seen.has(norm(nm))) break;   // the upgrade levels follow the base recipe in the same field; level 1 never repeats an ingredient
    seen.add(norm(nm));
    items.push(nm + ' x' + (mm[2] || 1));
  }
  if (items.length) { r.ingredients = items.join(', '); r.recipe_from = 'wiki'; fromWiki++; }
}
for (const r of rows) if (handRecipes[r.prefab]) { r.ingredients = handRecipes[r.prefab]; r.recipe_from = 'hand'; }
console.log('recipes taken from the wiki infobox: ' + fromWiki);
// Boss trophies and the boss-unique drops are rarity 5 and ultra by the owner's words, whatever a slice said.
const bossItems = ['TrophyEikthyr', 'TrophyTheElder', 'TrophyBonemass', 'TrophyDragonQueen', 'TrophyGoblinKing', 'TrophySeekerQueen', 'TrophyFader', 'Wishbone', 'DragonEgg', 'YagluthDrop', 'QueenDrop', 'FaderDrop', 'BeltStrength'];
for (const r of rows) if (bossItems.includes(r.prefab)) { r.rarity = '5'; r.class = 'ultra'; r.boss_gated = 'true'; r.limited = 'true'; }
function resolveName(name) {
  const key = norm(name);
  const tries = [key, key.replace(/s$/, ''), key + 's', key.replace(/^raw /, ''), key.replace(/ x\d+$/, '')];
  for (const t of tries) { const p = nameTo[t]; if (p && prefabs.has(p)) return p; }
  // a mead base written as "Mead base: tasty" / "Tasty mead base"
  const mb = key.match(/^(.*?)\s*mead base$/) || key.match(/^mead base:?\s*(.*)$/);
  if (mb) { const want = norm(mb[1]).replace(/ /g, ''); for (const p of prefabs) if (/^MeadBase/.test(p) && norm(p.slice(8)).replace(/ /g, '') === want) return p; }
  return null;
}
function parse(ing) {
  const out = [], bad = []; const seenNames = new Set();
  if (!ing) return { out, bad };
  for (let part of ing.split(/[,;+]|\band\b/)) {
    part = part.replace(/\(.*?\)/g, '').trim(); if (!part) continue;
    // an assessor's string that runs on into the upgrade levels repeats an ingredient; level 1 never does
    const firstName = norm(part.replace(/\s*[x×]\s*\d+\s*$/i, '').replace(/^\d+\s*[x×]?\s+/, ''));
    if (seenNames.has(firstName)) break;
    seenNames.add(firstName);
    let name, n = 1;
    let m = part.match(/^(.*?)\s*[x×]\s*(\d+)\s*$/i);
    if (m) { name = m[1]; n = +m[2]; }
    else if ((m = part.match(/^(\d+)\s*[x×]?\s+(.*)$/))) { n = +m[1]; name = m[2]; }
    else if ((m = part.match(/^(.*?)\s+(\d+)\s*$/))) { name = m[1]; n = +m[2]; }
    else name = part;
    const p = resolveName(name.trim());
    if (p) out.push({ prefab: p, n }); else bad.push(name.trim());
  }
  return { out, bad };
}

// ---- the two models ------------------------------------------------------------------------------
const rawBase = { 0: 2, 1: 2.5, 2: 4, 3: 4.5, 4: 5, 5: 5.5, 6: 6.5, 7: 10, 8: 14 };
const rarityMult = { 1: 0.7, 2: 1.0, 3: 2.5, 4: 4.5, 5: 8 };
const scoreComplexity = { 1: 1.0, 2: 3.0, 3: 8.0, 4: 15.0, 5: 30.0 };
const markup = { 1: 1.0, 2: 1.15, 3: 1.3, 4: 1.5, 5: 1.8 };
const clampInt = (v, lo, hi) => Math.max(lo, Math.min(hi, parseInt(v, 10) || lo));
function yieldOf(p, r) {
  if (/^(Arrow|Bolt|TurretBolt)/.test(p)) return 20;
  if (/Nails$/.test(p)) return 20;
  if (/^Mead[A-Z]/.test(p) && !/^MeadBase/.test(p)) return 2.5;   // a brew yields six, but the three anchored meads (10-12) say the brew is worth more than base/6: 2.5 fits them
  if (/^Bomb/.test(p)) return 5;
  if (/^Fireworks/.test(p)) return 4;
  if (p === 'Coal') return 1;
  return 1;
}
function ultraValue(r) {
  const t = clampInt(r.tier, 0, 8), ra = clampInt(r.rarity, 1, 5), co = clampInt(r.complexity, 1, 5);
  return Math.min(1200, Math.max(800, 800 + 60 * (t - 4) + 40 * (ra - 4) + 40 * (co - 4)));
}
// The ultra band, in the owner's words "ultra complex/rare": the assessor's call, and never a consumable, and
// for a crafted thing only with a rare input (rarity 4+: the infused Ashlands weapons, Dyrnwyn); the boss
// trophies and the boss-unique drops (rarity 5) as they are. merge.js's `class` carries the assessor's call.
function isUltra(r) {
  if (r.class !== 'ultra') return false;
  if (r.family === 'consumable' || /^Feast/.test(r.prefab)) return false;
  return clampInt(r.rarity, 1, 5) >= 4;
}
// Trader goods (Haldor, Hildir, the Bog Witch): a quarter of the shop price is the floor - he is no resale
// outlet, but a 350-coin dress is not a mushroom.
function traderFloor(r) { const p = parseInt(r.trader_price, 10) || 0; return p > 0 ? Math.min(600, Math.round(p / 4)) : 0; }
function scoreValue(r) {
  const t = clampInt(r.tier, 0, 8), ra = clampInt(r.rarity, 1, 5), co = clampInt(r.complexity, 1, 5);
  return Math.min(600, Math.max(2, Math.round(rawBase[t] * rarityMult[ra] * scoreComplexity[co])));
}
const memo = {}; const stack = new Set();
function valueOf(p, depth) {
  if (memo[p] !== undefined) return memo[p];
  const r = byPrefab[p]; if (!r) return null;
  if (r.anchor !== '') return (memo[p] = { value: +r.anchor, how: 'anchor' });
  if (handValues[p] !== undefined) return (memo[p] = { value: handValues[p], how: 'hand' });
  if (isUltra(r)) return (memo[p] = { value: ultraValue(r), how: 'ultra' });
  const co = clampInt(r.complexity, 1, 5);
  const floor = traderFloor(r);
  const withFloor = (value, how) => floor > value ? { value: floor, how: 'trader(' + r.trader_price + '/4; ' + how + ' said ' + value + ')' } : { value, how };
  if (co === 1 || !r.ingredients || stack.has(p) || depth > 6) return (memo[p] = withFloor(scoreValue(r), co === 1 ? 'raw' : 'score'));
  stack.add(p);
  const { out, bad } = parse(r.ingredients);
  let cost = 0, ok = out.length > 0 && bad.length === 0;
  for (const i of out) { const v = valueOf(i.prefab, depth + 1); if (!v) { ok = false; break; } cost += v.value * i.n; }
  stack.delete(p);
  if (!ok) return (memo[p] = withFloor(scoreValue(r), 'score(unresolved: ' + bad.join('/') + ')'));
  const y = yieldOf(p, r);
  // Crafted gear (complexity 3+) is compressed: with Iron at 25 a bar every iron-and-up piece costs 500+ in
  // inputs and the 600 ceiling would flatten the whole top of the table. cost^0.8 keeps the order and spreads
  // the band: leather ~13, bronze 40-75, iron ~200, silver and black metal 330-560, flametal at the cap.
  // Refining and cooking (complexity 2: bars, cooked meat, arrows, nails) stay linear, which is what the
  // anchors say.
  const per = cost / y;
  const equipment = r.family === 'weapons' || r.family === 'armor' || r.family === 'gear';
  const compress = equipment && co >= 3;
  const base = compress ? Math.pow(per, 0.8) : per;
  const v = Math.min(600, Math.max(2, Math.round(base * markup[co])));
  return (memo[p] = withFloor(v, 'cost(' + out.map(i => i.prefab + 'x' + i.n).join(' ') + ')/' + y + (compress ? '^0.8' : '') + '*' + markup[co]));
}

let crafted = 0, resolved = 0; const unresolved = {};
for (const r of rows) {
  if (r.player_item !== 'true' || +r.complexity < 2 || !r.ingredients) continue;
  crafted++;
  const { out, bad } = parse(r.ingredients);
  if (bad.length === 0 && out.length) resolved++;
  for (const b of bad) unresolved[b] = (unresolved[b] || 0) + 1;
}
console.log('crafted rows with a recipe: ' + crafted + ', fully resolved ' + resolved);
console.log('unresolved names (top 50): ' + Object.entries(unresolved).sort((a, b) => b[1] - a[1]).slice(0, 50).map(e => e[0] + 'x' + e[1]).join(' | '));

for (const r of rows) { const v = valueOf(r.prefab, 0); r.value2 = v ? v.value : ''; r.how = v ? v.how : ''; }
// the anchored crafted rows: what would the cost model have said?
console.log('\n== crafted anchors, cost model vs anchor (what the model says if the anchor were absent)');
for (const r of rows) {
  if (r.anchor === '' || +r.complexity < 2) continue;
  delete memo[r.prefab]; const save = r.anchor; r.anchor = '';
  const v = valueOf(r.prefab, 0); r.anchor = save; delete memo[r.prefab]; memo[r.prefab] = { value: +save, how: 'anchor' };
  console.log(r.prefab.padEnd(18) + ' anchor ' + String(save).padStart(4) + '  model ' + String(v.value).padStart(4) + '  ' + v.how.slice(0, 90));
}
const players = rows.filter(r => r.player_item === 'true');
console.log('\n== rows priced by score because the recipe did not resolve');
for (const r of players) if (/^score/.test(r.how)) console.log('  ' + r.prefab.padEnd(26) + ' c' + r.complexity + ' -> ' + r.value2 + '   ' + (r.ingredients || '(no recipe)').slice(0, 70) + '   ' + r.how.replace(/^score/, '').slice(0, 60));
console.log('\n== ultra rows');
for (const r of players) if (r.how === 'ultra') console.log('  ' + r.prefab.padEnd(26) + ' t' + r.tier + ' r' + r.rarity + ' c' + r.complexity + ' = ' + r.value2 + '  ' + r.name);
const hows = {}; for (const r of players) { const k = r.how.replace(/\(.*/, ''); hows[k] = (hows[k] || 0) + 1; }
console.log('\nhow the values were set: ' + JSON.stringify(hows));
const bands = { '<=30': 0, '31-100': 0, '101-300': 0, '301-600': 0, 'ultra': 0 };
for (const r of players) { const v = +r.value2; bands[v >= 800 ? 'ultra' : v <= 30 ? '<=30' : v <= 100 ? '31-100' : v <= 300 ? '101-300' : '301-600']++; }
console.log('bands: ' + JSON.stringify(bands));
console.log('\n== samples by family (prefab value how)');
for (const fam of ['weapons', 'armor', 'consumable', 'gear', 'trophy', 'material']) {
  const pick = players.filter(r => r.family === fam).sort(() => 0.5).slice(0, 8);
  console.log(fam + ': ' + pick.map(r => r.prefab + '=' + r.value2 + ' [' + r.how.replace(/\(.*/, '') + ']').join(', '));
}
if (writeIdx > 0) {
  const outTsv = process.argv[writeIdx + 1];
  const cols = ['prefab', 'name', 'family', 'player_item', 'tier', 'biome', 'source', 'station', 'station_level', 'ingredients', 'drop_rate', 'boss_gated', 'limited', 'trader_price', 'rarity', 'complexity', 'value', 'class', 'how', 'anchor', 'confidence', 'wiki_page', 'exclude_reason', 'note'];
  const cls = r => { const v = +r.value2; if (v >= 800) return 'ultra'; return (v <= 30 && clampInt(r.rarity, 1, 5) <= 2) ? 'common' : 'rare'; };
  const out = [cols.join('\t')];
  for (const r of rows) out.push(cols.map(c => c === 'value' ? r.value2 : c === 'class' ? cls(r) : c === 'how' ? r.how.replace(/\(.*/, '') : String(r[c] === undefined ? '' : r[c])).join('\t'));
  fs.writeFileSync(outTsv, out.join('\n') + '\n');
  console.log('\nwrote ' + outTsv);
}
