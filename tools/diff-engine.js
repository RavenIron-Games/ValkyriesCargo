#!/usr/bin/env node
'use strict';
//
// The filter that makes a Valheim-versus-Valheim diff readable. P10a, docs/P10-P11-FOR-DON.md.
//
//   node tools/diff-engine.js --surface docs/ENGINE-SURFACE.md \
//        --from <srcA> --to <srcB> --out <report.md> [--all-types]
//
// <srcA> and <srcB> are decompiled trees written by tools/decompile-builds.ps1: a directory
// holding assembly_valheim/, assembly_utils/ and assembly_guiutils/, one .cs file per type.
//
// A raw diff of two builds is thousands of files and a hundred thousand lines. This walks
// docs/ENGINE-SURFACE.md - every game member this mod actually names - and gives each one of
// four verdicts:
//
//   unchanged          identical after whitespace normalisation          nothing to do
//   body changed       same declaration, different body                  THE DANGEROUS ONE:
//                                                                        a recorded engine fact
//                                                                        may now be false, and
//                                                                        nothing would say so
//   signature changed  found, but the declaration line differs           the build breaks, or a
//                                                                        patch stops applying
//   gone               present in <from>, absent in <to>                 as above
//
// Only the last three are printed. --all-types additionally lists every TYPE whose file differs
// at all, which is what makes the client-versus-server sweep a COMPLETE list of behavioural
// differences rather than "we found one".
//
// One thing to know when reading a report: FOR A FIELD, THE INITIALISER IS PART OF THE
// DECLARATION, so `m_dayLengthSec = 1200` becoming `= 1500` reads as "signature changed", not
// "body changed". That is the right answer - a field has no body - but it means the two most
// alarming-sounding verdicts are not always about a rename. Read the diff, not the heading.
//
// Node only, no packages: there is no Python on this machine and nothing here needs npm.
// Output is deterministic (everything sorted). Exit 0 when every surface member is unchanged,
// 1 otherwise - so this can gate a build.
//
// Why a hand-written C# member extractor rather than a real parser: ilspycmd's output is
// extremely regular (tabs, one member per block, attributes on their own lines), and the job is
// only to find one member's text inside one file. The scanner below tracks strings, chars,
// verbatim strings, both comment forms and brace depth, and knows the one construct that would
// otherwise fool it - a field whose initialiser opens a brace (`= new List<int> { ... };`).

const fs = require('fs');
const path = require('path');

// ---- arguments ---------------------------------------------------------------------------

function parseArgs(argv) {
  const out = { surface: null, from: null, to: null, output: null, allTypes: false };
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--surface') out.surface = argv[++i];
    else if (a === '--from') out.from = argv[++i];
    else if (a === '--to') out.to = argv[++i];
    else if (a === '--out') out.output = argv[++i];
    else if (a === '--all-types') out.allTypes = true;
    else if (a === '--help' || a === '-h') out.help = true;
    else { console.error('unknown argument: ' + a); process.exit(2); }
  }
  return out;
}

const USAGE =
  'node tools/diff-engine.js --surface docs/ENGINE-SURFACE.md --from <srcA> --to <srcB> --out <report.md> [--all-types]';

// ---- the surface manifest ----------------------------------------------------------------
//
// One member per line, pipe separated, anywhere in the Markdown:
//
//   Type.Member | assembly | kind | note
//
// Everything that is not such a line (prose, headings, table rules) is ignored, so the manifest
// stays a document a person reads rather than a data file with a header comment.

const MEMBER_RE = /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$/;

function readSurface(file) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  const entries = [];
  const seen = new Set();
  for (let i = 0; i < lines.length; i++) {
    const raw = lines[i];
    if (raw.indexOf('|') < 0) continue;
    const cols = raw.split('|').map(s => s.trim());
    if (cols.length < 2) continue;
    if (!MEMBER_RE.test(cols[0])) continue;
    const name = cols[0];
    if (seen.has(name)) continue;          // a duplicate row is the manifest's problem, not the diff's
    seen.add(name);
    entries.push({
      name,
      assembly: cols[1] || '',
      kind: cols[2] || '',
      note: cols.slice(3).join(' | ') || '',
      line: i + 1,
    });
  }
  return entries;
}

// ---- the C# member extractor ---------------------------------------------------------------

// Walk the file once, char by char, and record every member and nested type with its own text.
// Keys are the full nesting path: "ZDO.ObjectType", "ItemDrop.ItemData.SharedData.m_name".
function extractMembers(text) {
  const members = new Map();
  const typeStack = [];        // names of the types whose bodies we are inside
  const blockStack = [];       // {isType, name, headerStart}
  let i = 0;
  let stmtStart = 0;           // where the current declaration's text begins
  const n = text.length;

  const skipTo = (j, stop) => { while (j < n && text[j] !== stop) j++; return j; };

  while (i < n) {
    const c = text[i];

    // --- things that must not be read as code ---
    if (c === '/' && text[i + 1] === '/') { i = skipTo(i, '\n'); continue; }
    if (c === '/' && text[i + 1] === '*') {
      i += 2;
      while (i < n && !(text[i] === '*' && text[i + 1] === '/')) i++;
      i += 2; continue;
    }
    if (c === '@' && text[i + 1] === '"') {           // verbatim string: "" is an escaped quote
      i += 2;
      while (i < n) {
        if (text[i] === '"') { if (text[i + 1] === '"') { i += 2; continue; } i++; break; }
        i++;
      }
      continue;
    }
    if (c === '"' || c === '\'') {
      const q = c; i++;
      while (i < n) {
        if (text[i] === '\\') { i += 2; continue; }
        if (text[i] === q) { i++; break; }
        i++;
      }
      continue;
    }

    if (c === '{') {
      const header = text.slice(stmtStart, i);
      const decl = classifyHeader(header);
      const end = matchBrace(text, i);
      if (decl && decl.isType) {
        // A type body: descend into it so its members get qualified names.
        typeStack.push(decl.name);
        record(members, typeStack, null, text, stmtStart, end + 1, header, true);
        blockStack.push({ closeAt: end, pop: true });
        i++;
        stmtStart = i;
        continue;
      }
      if (decl && decl.name) {
        // A method, property, event or indexer: take the whole block and skip it.
        record(members, typeStack, decl.name, text, stmtStart, end + 1, header, false);
        i = end + 1;
        // A property may be followed by an initialiser: `{ get; } = new X();`. Swallow to the ;
        let j = i;
        while (j < n && /\s/.test(text[j])) j++;
        if (text[j] === '=') { i = swallowStatement(text, j); }
        stmtStart = i;
        continue;
      }
      // Not a declaration this extractor names. Two very different cases, and getting them the
      // wrong way round costs the NEXT member: an initialiser (`= new List<int> { ... };`) is
      // mid-statement and must run on to its semicolon, while an operator or an indexer is a
      // whole member whose closing brace ends the statement. Told apart by a top-level `=`,
      // which an initialiser has and an operator declaration never does. Cost one run to find:
      // ZDOID's `operator ==` swallowed the header of everything after it, and `ZDOID.IsNone`
      // came back "not found" from a file that plainly declares it.
      i = end + 1;
      if (topLevelEquals(classifiableHeader(header)) < 0) stmtStart = i;
      continue;
    }

    if (c === '}') {
      const top = blockStack[blockStack.length - 1];
      if (top && top.closeAt === i) { blockStack.pop(); if (top.pop) typeStack.pop(); }
      i++;
      stmtStart = i;
      continue;
    }

    if (c === ';') {
      // A field, a const, an expression-bodied member, an abstract method: ends here.
      const header = text.slice(stmtStart, i + 1);
      const decl = classifyHeader(header);
      if (decl && decl.name && !decl.isType) record(members, typeStack, decl.name, text, stmtStart, i + 1, header, false);
      i++;
      stmtStart = i;
      continue;
    }

    i++;
  }
  return members;
}

function swallowStatement(text, i) {
  const n = text.length;
  while (i < n) {
    const c = text[i];
    if (c === '"' || c === '\'') {
      const q = c; i++;
      while (i < n) { if (text[i] === '\\') { i += 2; continue; } if (text[i] === q) { i++; break; } i++; }
      continue;
    }
    if (c === '{') { i = matchBrace(text, i) + 1; continue; }
    if (c === ';') return i + 1;
    i++;
  }
  return i;
}

function matchBrace(text, open) {
  let depth = 0;
  let i = open;
  const n = text.length;
  while (i < n) {
    const c = text[i];
    if (c === '/' && text[i + 1] === '/') { while (i < n && text[i] !== '\n') i++; continue; }
    if (c === '/' && text[i + 1] === '*') { i += 2; while (i < n && !(text[i] === '*' && text[i + 1] === '/')) i++; i += 2; continue; }
    if (c === '@' && text[i + 1] === '"') {
      i += 2;
      while (i < n) { if (text[i] === '"') { if (text[i + 1] === '"') { i += 2; continue; } i++; break; } i++; }
      continue;
    }
    if (c === '"' || c === '\'') {
      const q = c; i++;
      while (i < n) { if (text[i] === '\\') { i += 2; continue; } if (text[i] === q) { i++; break; } i++; }
      continue;
    }
    if (c === '{') depth++;
    else if (c === '}') { depth--; if (depth === 0) return i; }
    i++;
  }
  return n - 1;
}

const TYPE_KIND_RE = /\b(class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)/;

// Attributes and comments are not part of a declaration; strip them once, in one place.
function classifiableHeader(header) {
  return header
    .replace(/\/\/[^\n]*/g, ' ')
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/^\s*\[[^\]]*\]\s*/gm, ' ')
    .trim();
}

// Turn the text before a { or ; into a member name, or null when it is not a declaration.
function classifyHeader(header) {
  const h = classifiableHeader(header);
  if (!h) return null;
  if (/^(namespace|using)\b/.test(h)) return null;

  const t = TYPE_KIND_RE.exec(h);
  if (t) return { isType: true, name: t[2] };

  // An initialiser, a lambda or an assignment is not a member declaration.
  const eq = topLevelEquals(h);
  const beforeEq = eq >= 0 ? h.slice(0, eq) : h;

  // A method or constructor: the identifier immediately before the first top-level (
  const paren = topLevelIndex(beforeEq, '(');
  if (paren > 0) {
    const before = beforeEq.slice(0, paren);
    const m = /([A-Za-z_][A-Za-z0-9_]*)\s*(<[^<>]*>)?\s*$/.exec(before);
    if (m) return { isType: false, name: m[1] };
    return null;
  }

  // this[...] indexers and operators are out of scope; they are named nowhere in this mod.
  if (/\bthis\s*\[/.test(beforeEq) || /\boperator\b/.test(beforeEq)) return null;

  // A field, a const or a property: the last identifier of the declaration part.
  const cleaned = beforeEq.replace(/=>[\s\S]*$/, '').replace(/;\s*$/, '').trim();
  const parts = cleaned.split(/[\s,]+/).filter(Boolean);
  if (parts.length < 2) return null;             // a bare identifier is a statement, not a declaration
  const last = parts[parts.length - 1];
  if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(last)) return null;
  if (RESERVED.has(last)) return null;
  return { isType: false, name: last };
}

const RESERVED = new Set([
  'return', 'break', 'continue', 'throw', 'else', 'try', 'finally', 'do', 'get', 'set', 'add', 'remove',
  'true', 'false', 'null', 'base', 'this', 'new', 'case', 'default', 'goto', 'yield', 'checked', 'unchecked',
]);

function topLevelIndex(s, ch) {
  let angle = 0, square = 0, round = 0;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (c === '<') angle++;
    else if (c === '>') { if (angle > 0) angle--; }
    else if (c === '[') square++;
    else if (c === ']') { if (square > 0) square--; }
    else if (c === '(') { if (ch === '(' && angle === 0 && square === 0 && round === 0) return i; round++; }
    else if (c === ')') { if (round > 0) round--; }
    else if (c === ch && angle === 0 && square === 0 && round === 0) return i;
  }
  return -1;
}

function topLevelEquals(s) {
  let angle = 0, square = 0, round = 0;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (c === '<') angle++;
    else if (c === '>') { if (angle > 0) angle--; }
    else if (c === '[') square++;
    else if (c === ']') { if (square > 0) square--; }
    else if (c === '(') round++;
    else if (c === ')') { if (round > 0) round--; }
    else if (c === '=' && angle === 0 && square === 0 && round === 0) {
      if (s[i + 1] === '=' || s[i + 1] === '>' || s[i - 1] === '=' || s[i - 1] === '!' ||
          s[i - 1] === '<' || s[i - 1] === '>' || s[i - 1] === '+' || s[i - 1] === '-') continue;
      return i;
    }
  }
  return -1;
}

function record(members, typeStack, memberName, text, start, end, header, isType) {
  const parts = memberName ? typeStack.concat([memberName]) : typeStack.slice();
  if (parts.length === 0) return;
  const key = parts.join('.');
  const body = text.slice(start, end);
  const decl = declarationLine(header);
  const existing = members.get(key);
  if (existing) {
    // OVERLOADS ARE ONE ENTRY. `ZDO.Set` has eight of them and the manifest names the member,
    // not a signature; folding them together means a change in ANY overload is reported, and an
    // overload appearing or disappearing shows up as a signature change rather than as silence.
    // Sorted, so the report does not move when ilspycmd reorders members.
    existing.parts.push({ decl, body });
    existing.parts.sort((a, b) => (a.decl < b.decl ? -1 : a.decl > b.decl ? 1 : 0));
    existing.text = existing.parts.map(p => p.body).join('\n');
    existing.declaration = existing.parts.map(p => p.decl).join(' ;; ');
    existing.overloads = existing.parts.length;
    return;
  }
  members.set(key, {
    key,
    isType,
    parts: [{ decl, body }],
    overloads: 1,
    text: body,
    declaration: decl,
  });
}

// The one line the "signature changed" verdict compares: everything before the body, with
// attributes and comments removed and whitespace flattened.
function declarationLine(header) {
  return classifiableHeader(header).replace(/\s+/g, ' ').trim();
}

// ---- the trees ------------------------------------------------------------------------------

const ASSEMBLIES = ['assembly_valheim', 'assembly_utils', 'assembly_guiutils'];

function walk(dir, base, out) {
  let entries;
  try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch (e) { return out; }
  entries.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) walk(full, base, out);
    else if (e.name.endsWith('.cs')) out.push(path.relative(base, full).split(path.sep).join('/'));
  }
  return out;
}

// A tree: for each assembly, the relative paths of its .cs files, indexed by the type name the
// file is named for (the last path segment without .cs). ILSpy writes namespaced types into
// subfolders, so the index is by leaf name.
function loadTree(root) {
  const tree = { root, byAssembly: {}, byType: new Map(), files: new Map() };
  for (const asm of ASSEMBLIES) {
    const dir = path.join(root, asm);
    const files = fs.existsSync(dir) ? walk(dir, dir, []) : [];
    tree.byAssembly[asm] = files;
    for (const rel of files) {
      const leaf = path.basename(rel, '.cs');
      const key = asm + '/' + rel;
      tree.files.set(key, path.join(dir, rel));
      if (!tree.byType.has(leaf)) tree.byType.set(leaf, []);
      tree.byType.get(leaf).push({ assembly: asm, rel, key });
    }
  }
  return tree;
}

const memberCache = new Map();
function membersOf(tree, fileKey) {
  const cacheKey = tree.root + ' ' + fileKey;
  if (memberCache.has(cacheKey)) return memberCache.get(cacheKey);
  const full = tree.files.get(fileKey);
  const m = full ? extractMembers(fs.readFileSync(full, 'utf8')) : new Map();
  memberCache.set(cacheKey, m);
  return m;
}

// The file that declares the OUTERMOST type of a dotted path.
function fileFor(tree, entry) {
  const outer = entry.name.split('.')[0];
  const hits = tree.byType.get(outer);
  if (!hits || hits.length === 0) return null;
  if (entry.assembly) {
    const exact = hits.find(h => h.assembly === entry.assembly);
    if (exact) return exact;
  }
  return hits[0];
}

function lookup(tree, entry) {
  const file = fileFor(tree, entry);
  if (!file) return null;
  const members = membersOf(tree, file.key);
  const found = members.get(entry.name);
  if (!found) return null;
  return { file, member: found };
}

// ---- comparison -----------------------------------------------------------------------------

// Whitespace-insensitive equality: ilspycmd can re-wrap a long line between builds without any
// change in meaning, and a report full of those hides the one that matters.
function normalise(s) {
  return s.replace(/\r\n/g, '\n').split('\n').map(l => l.replace(/\s+/g, ' ').trim()).filter(l => l.length > 0).join('\n');
}

// A plain LCS unified diff. Three lines of context, no rename detection, no heuristics.
function unifiedDiff(aText, bText, fromLabel, toLabel) {
  const a = aText.replace(/\r\n/g, '\n').split('\n');
  const b = bText.replace(/\r\n/g, '\n').split('\n');
  const ops = lcsOps(a, b);
  const hunks = [];
  let cur = null;
  const CONTEXT = 3;
  for (let k = 0; k < ops.length; k++) {
    const op = ops[k];
    if (op.t === '=') {
      if (cur) {
        cur.trailing = (cur.trailing || 0) + 1;
        if (cur.trailing > CONTEXT * 2) { hunks.push(cur); cur = null; }
        else cur.lines.push(' ' + op.v);
      }
      continue;
    }
    if (!cur) {
      cur = { lines: [], aStart: op.ai, bStart: op.bi, trailing: 0 };
      const back = [];
      for (let j = k - 1, c = 0; j >= 0 && c < CONTEXT; j--) {
        if (ops[j].t !== '=') break;
        back.unshift(' ' + ops[j].v); c++; cur.aStart = ops[j].ai; cur.bStart = ops[j].bi;
      }
      cur.lines = back;
    }
    cur.trailing = 0;
    cur.lines.push((op.t === '-' ? '-' : '+') + op.v);
  }
  if (cur) hunks.push(cur);
  if (hunks.length === 0) return '';
  const out = ['--- ' + fromLabel, '+++ ' + toLabel];
  for (const h of hunks) {
    while (h.lines.length && h.lines[h.lines.length - 1].startsWith(' ') &&
           h.lines.filter(l => l.startsWith(' ')).length > CONTEXT * 2) {
      const tail = h.lines[h.lines.length - 1];
      if (!tail.startsWith(' ')) break;
      let count = 0;
      for (let i = h.lines.length - 1; i >= 0 && h.lines[i].startsWith(' '); i--) count++;
      if (count <= CONTEXT) break;
      h.lines.pop();
    }
    const aCount = h.lines.filter(l => l[0] === ' ' || l[0] === '-').length;
    const bCount = h.lines.filter(l => l[0] === ' ' || l[0] === '+').length;
    out.push('@@ -' + (h.aStart + 1) + ',' + aCount + ' +' + (h.bStart + 1) + ',' + bCount + ' @@');
    for (const l of h.lines) out.push(l);
  }
  return out.join('\n');
}

function lcsOps(a, b) {
  // Trim the common head and tail first: two decompiled bodies of the same method are almost
  // all common, and the O(n*m) table below is only worth building for what is left.
  let head = 0;
  while (head < a.length && head < b.length && a[head] === b[head]) head++;
  let tail = 0;
  while (tail < a.length - head && tail < b.length - head &&
         a[a.length - 1 - tail] === b[b.length - 1 - tail]) tail++;
  const am = a.slice(head, a.length - tail);
  const bm = b.slice(head, b.length - tail);

  const ops = [];
  for (let i = 0; i < head; i++) ops.push({ t: '=', v: a[i], ai: i, bi: i });

  if (am.length * bm.length > 4000000) {
    // Far too large to be one member's body; fall back to a block replace.
    for (let i = 0; i < am.length; i++) ops.push({ t: '-', v: am[i], ai: head + i, bi: head });
    for (let j = 0; j < bm.length; j++) ops.push({ t: '+', v: bm[j], ai: head + am.length, bi: head + j });
  } else {
    const m = am.length, n2 = bm.length;
    const dp = new Int32Array((m + 1) * (n2 + 1));
    for (let i = m - 1; i >= 0; i--) {
      for (let j = n2 - 1; j >= 0; j--) {
        dp[i * (n2 + 1) + j] = am[i] === bm[j]
          ? dp[(i + 1) * (n2 + 1) + (j + 1)] + 1
          : Math.max(dp[(i + 1) * (n2 + 1) + j], dp[i * (n2 + 1) + (j + 1)]);
      }
    }
    let i = 0, j = 0;
    while (i < m && j < n2) {
      if (am[i] === bm[j]) { ops.push({ t: '=', v: am[i], ai: head + i, bi: head + j }); i++; j++; }
      else if (dp[(i + 1) * (n2 + 1) + j] >= dp[i * (n2 + 1) + (j + 1)]) { ops.push({ t: '-', v: am[i], ai: head + i, bi: head + j }); i++; }
      else { ops.push({ t: '+', v: bm[j], ai: head + i, bi: head + j }); j++; }
    }
    while (i < m) { ops.push({ t: '-', v: am[i], ai: head + i, bi: head + j }); i++; }
    while (j < n2) { ops.push({ t: '+', v: bm[j], ai: head + i, bi: head + j }); j++; }
  }

  for (let k = 0; k < tail; k++) {
    const ai = a.length - tail + k, bi = b.length - tail + k;
    ops.push({ t: '=', v: a[ai], ai, bi });
  }
  return ops;
}

// ---- main ------------------------------------------------------------------------------------

function main() {
  const args = parseArgs(process.argv);
  if (args.help || !args.surface || !args.from || !args.to) {
    console.log(USAGE);
    process.exit(args.help ? 0 : 2);
  }
  for (const p of [args.surface, args.from, args.to]) {
    if (!fs.existsSync(p)) { console.error('no such path: ' + p); process.exit(2); }
  }

  const surface = readSurface(args.surface);
  const from = loadTree(args.from);
  const to = loadTree(args.to);

  const verdicts = { unchanged: [], 'body changed': [], 'signature changed': [], gone: [] };
  const manifestProblems = [];

  for (const entry of surface) {
    const a = lookup(from, entry);
    const b = lookup(to, entry);
    if (!a && !b) { manifestProblems.push({ entry, why: 'not found in either tree' }); continue; }
    if (!a && b) { manifestProblems.push({ entry, why: 'absent from <from>, present in <to> (new in the newer build)' }); continue; }
    if (a && !b) { verdicts.gone.push({ entry, a }); continue; }
    const sameText = normalise(a.member.text) === normalise(b.member.text);
    if (sameText) { verdicts.unchanged.push({ entry }); continue; }
    const sameDecl = a.member.declaration === b.member.declaration;
    const row = {
      entry, a, b,
      diff: unifiedDiff(a.member.text, b.member.text, args.from + '/' + a.file.key, args.to + '/' + b.file.key),
    };
    if (sameDecl) verdicts['body changed'].push(row);
    else verdicts['signature changed'].push(row);
  }

  const byType = r => {
    const t = r.entry.name.split('.').slice(0, -1).join('.');
    return t + ' ' + r.entry.name;
  };
  for (const k of Object.keys(verdicts)) verdicts[k].sort((x, y) => (byType(x) < byType(y) ? -1 : byType(x) > byType(y) ? 1 : 0));

  // ---- --all-types: every type file that differs at all ----
  let typeReport = null;
  if (args.allTypes) {
    const keys = new Set([...from.files.keys(), ...to.files.keys()]);
    const differing = [], onlyFrom = [], onlyTo = [];
    for (const key of [...keys].sort()) {
      const fa = from.files.get(key), fb = to.files.get(key);
      if (fa && !fb) { onlyFrom.push(key); continue; }
      if (!fa && fb) { onlyTo.push(key); continue; }
      const ta = normalise(fs.readFileSync(fa, 'utf8'));
      const tb = normalise(fs.readFileSync(fb, 'utf8'));
      if (ta !== tb) differing.push(key);
    }
    typeReport = { differing, onlyFrom, onlyTo, total: keys.size };
  }

  const md = render(args, surface, verdicts, manifestProblems, typeReport);
  if (args.output) {
    fs.mkdirSync(path.dirname(path.resolve(args.output)), { recursive: true });
    fs.writeFileSync(args.output, md, 'utf8');
    console.log('wrote ' + args.output);
  } else {
    process.stdout.write(md);
  }

  const bad = verdicts['body changed'].length + verdicts['signature changed'].length +
              verdicts.gone.length + manifestProblems.length;
  console.log(
    'surface ' + surface.length + ': ' + verdicts.unchanged.length + ' unchanged, ' +
    verdicts['body changed'].length + ' body changed, ' +
    verdicts['signature changed'].length + ' signature changed, ' +
    verdicts.gone.length + ' gone, ' + manifestProblems.length + ' manifest problem(s)' +
    (typeReport ? '; types: ' + typeReport.differing.length + ' differ, ' +
      typeReport.onlyFrom.length + ' only in from, ' + typeReport.onlyTo.length + ' only in to' : ''));
  process.exit(bad === 0 ? 0 : 1);
}

function render(args, surface, verdicts, manifestProblems, typeReport) {
  const L = [];
  L.push('<!-- generated by tools/diff-engine.js; edit the prose around it, not the tables -->');
  L.push('');
  L.push('    from    : ' + args.from);
  L.push('    to      : ' + args.to);
  L.push('    surface : ' + args.surface + ' (' + surface.length + ' members)');
  L.push('');
  L.push('| verdict | count |');
  L.push('|---|---|');
  L.push('| unchanged | ' + verdicts.unchanged.length + ' |');
  L.push('| body changed | ' + verdicts['body changed'].length + ' |');
  L.push('| signature changed | ' + verdicts['signature changed'].length + ' |');
  L.push('| gone | ' + verdicts.gone.length + ' |');
  if (manifestProblems.length) L.push('| (manifest problems) | ' + manifestProblems.length + ' |');
  L.push('');

  for (const verdict of ['gone', 'signature changed', 'body changed']) {
    const rows = verdicts[verdict];
    L.push('## ' + verdict + ' (' + rows.length + ')');
    L.push('');
    if (rows.length === 0) { L.push('None.'); L.push(''); continue; }
    let lastType = null;
    for (const r of rows) {
      const type = r.entry.name.split('.').slice(0, -1).join('.');
      if (type !== lastType) { L.push('### ' + type); L.push(''); lastType = type; }
      L.push('**`' + r.entry.name + '`**' + (r.entry.note ? ' - ' + r.entry.note : ''));
      L.push('');
      if (verdict === 'gone') {
        L.push('Present in `' + args.from + '`, absent in `' + args.to + '`.');
        L.push('');
        L.push('```csharp');
        L.push(r.a.member.text.trim());
        L.push('```');
      } else {
        if (verdict === 'signature changed') {
          L.push('    - ' + r.a.member.declaration);
          L.push('    + ' + r.b.member.declaration);
          L.push('');
        }
        L.push('```diff');
        L.push(r.diff);
        L.push('```');
      }
      L.push('');
    }
  }

  if (manifestProblems.length) {
    L.push('## manifest problems (' + manifestProblems.length + ')');
    L.push('');
    L.push('These are about `' + args.surface + '`, not about the engine: a row that resolves in');
    L.push('neither tree is a name that wants correcting.');
    L.push('');
    for (const p of manifestProblems) L.push('- `' + p.entry.name + '` (' + p.entry.assembly + ', line ' + p.entry.line + '): ' + p.why);
    L.push('');
  }

  if (typeReport) {
    L.push('## every type whose file differs (--all-types)');
    L.push('');
    L.push(typeReport.differing.length + ' of ' + typeReport.total + ' type files differ; ' +
           typeReport.onlyFrom.length + ' exist only in `from`, ' + typeReport.onlyTo.length + ' only in `to`.');
    L.push('');
    if (typeReport.onlyFrom.length) {
      L.push('### only in `from`');
      L.push('');
      for (const k of typeReport.onlyFrom) L.push('- `' + k + '`');
      L.push('');
    }
    if (typeReport.onlyTo.length) {
      L.push('### only in `to`');
      L.push('');
      for (const k of typeReport.onlyTo) L.push('- `' + k + '`');
      L.push('');
    }
    L.push('### differing');
    L.push('');
    if (typeReport.differing.length === 0) L.push('None.');
    for (const k of typeReport.differing) L.push('- `' + k + '`');
    L.push('');
  }

  return L.join('\n') + '\n';
}

main();
