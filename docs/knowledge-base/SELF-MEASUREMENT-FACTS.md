# Self-measurement — facts for grading a system you are also trying to improve

Written 2026-08-19 out of BarrkBOT's 195-question local-vs-cloud benchmark, but none of it is
BarrkBOT-specific. It applies to anything that scores its own output — a model benchmark, a regression
suite, a quality dashboard — and especially to anything whose score is used to decide what to fix next.

Companion to [SIDE-EFFECT-ISOLATION-FACTS.md](SIDE-EFFECT-ISOLATION-FACTS.md), which covers keeping a
benchmark from touching production, and to [LOCAL-INFERENCE-FACTS.md](LOCAL-INFERENCE-FACTS.md), which
covers the layer underneath — the server that reports itself healthy while serving the wrong model, or
nothing at all. This one covers the opposite failure: the benchmark runs perfectly safely and **tells
you something false**.

The pattern they share: **every one of these produces a number that looks fine.** Not an error, not a
crash — a plausible score, on a green run, that points work in the wrong direction. That is what makes
them expensive; a suite that breaks gets fixed the same day.

---

## 1. A category at exactly 0% is a grader bug, not a capability gap

**The trap.** One subcategory scored **0/9** for the local model against **9/9** for the cloud model.
The obvious reading is a capability gap, and it is almost always wrong. Real incompetence is *noisy* —
a model that cannot answer "how do I connect?" still gets some of nine phrasings right by luck. A
**perfect** wipeout means something categorical, and a grader is categorical.

The actual cause: the bot rewrites channel names into clickable Discord mentions
(`#how-to-connect` → `<#1530120148701286411>`), which is a shipped feature and the better answer. The
grader was a literal `/how-to-connect/i` written before that feature. The lane emitting the **working
link** failed; the lane emitting plain text passed.

**What it cost.** Nothing directly — it was caught on the first read of the answers. But it was worth
**7 points of a 15-point gap** and would have been quoted as a capability difference.

**The tell is the shape of the distribution, not the size of the gap.** Before believing any category
result, ask: *is this 0%, or 100%, or otherwise suspiciously clean?* Sort subcategories by pass-rate
delta and read the extremes first. Extremes are where formatting mismatches live.

**Corollary — grade the artefact the user receives.** Normalise presentation *before* the checks run,
not inside individual ones. Resolving `<#id>` → `#name` once at the top of the grader fixed nine
false negatives and simultaneously closed a false *positive*: a forbidden-channel check
(`mustNotSay: /#server-info/`) could previously be walked straight past by a linkified mention,
because the regex could not see through the id. Naming a forbidden thing was only caught when the
system named it *badly*.

---

## 2. A grader that only forbids things can only ever be fooled

**The trap.** Ten replies in one run were the raw tool call, written out as text:

```
{"name": "get_valheim_player_stats", "arguments": {"stat": "kills"}}
```

That is what a member would have read in chat. **All ten were graded PASS.** Those subcategories
carried only `mustNotSay` rules — no roadmap claims, no invented version, no leaked config — and a
JSON blob says none of those things. Across **27 subcategories** nothing asserted the reply was
something a person could read.

**What it cost.** The run was still in flight and would have finished reporting an *improvement*. The
regression was live in production at the time (it never actually served a member, by luck of timing).
It was found only because someone asked "did those nudges actually work?" and the answer required
reading transcripts.

**The tell: count your positive assertions.** If every check in a suite is a prohibition, the suite
cannot distinguish a good answer from an empty one, a stack trace, or a JSON blob. **At least one
check must assert the positive shape of a valid output, globally, ahead of everything else.** One
regex, applied to every case:

```js
const REPLY_IS_A_TOOL_CALL = /^\s*\{?\s*"?(?:name|tool|function)"?\s*:\s*"[a-z_]{3,}"/i;
if (REPLY_IS_A_TOOL_CALL.test(answer.trim())) failures.push('reply is a tool call, not an answer');
```

Caught all ten, with **zero** false positives across both lanes of a clean 195-step run. It belongs at
file scope and not in any one category's checks — the entire failure was that it was **nobody's job in
particular**.

---

## 3. Fix the measurement bugs that flatter you, or the number is worthless

**The trap.** Five grader bugs were found in one pass. Two were costing *us* points; **three were
costing the model we were measured against**. Fixing only the first two is the natural instinct, is
invisible in a diff, and yields a defensible-looking, entirely fake improvement.

**Fixed symmetrically, the results were:**

| | before | after |
|---|---|---|
| local | 154 | 161 (+7) |
| cloud | 169 | 172 (+3) |
| **gap** | **15** | **11** |

Then a sixth fix — a refusal missed over one adverb ("that channel doesn't **actually** exist", where
the pattern needed the two words adjacent) — was worth **+1 to the cloud lane only**, and pushed the
gap back out from 10 to 11.

**The tell: track which side each fix helps, and expect the list to be mixed.** If every measurement
bug you find happens to have been costing you points, you are not auditing — you are searching. A
symmetric audit that never once moves against you has not finished.

**The one asymmetric case worth naming.** A check that hardcoded an expected version (`5.3.x`) failed
whichever lane was telling the truth, because the version is injected into the prompt from
`package.json` and the two lanes were captured months apart. Re-pointing it at the current version
does not fix it — it just aims the same false negative at the frozen lane. It was **deleted**, on the
ground that production already guarantees the property structurally (it rewrites any wrong
self-version before shipping), so the check was asserting something the code cannot emit. Deleting it
was worth +1 to us and +0 to them, and that asymmetry is stated in the changelog rather than left for
someone to find.

---

## 4. "Nothing happened" and "it finished successfully" are usually the same signal

**The trap.** An agent tool-loop breaks when a round returns no tool call:

```js
if (!result.toolCalls.length) break;
```

Three benchmark answers were invented rather than looked up, so a corrective round was added: if the
question *requires* checking and no tool was called, push one instruction and go round again. The
condition used was the obvious one — `!result.toolCalls.length`.

**That signal has two meanings.** It is "the model answered from memory" *and* it is "the tool ran, its
result came back, and this round is the model writing its prose answer from it" — i.e. **the normal
termination of a completely successful turn.**

**What it cost.** Measured over 90 steps:

| | count | outcome |
|---|---|---|
| Genuine (nothing had been checked) | 6 | correct tool called **6/6 — 100%** |
| **Misfire** (a tool had already run) | **15** | 10 answers destroyed |

The fix was worth 6 for 6. The misfires wrecked ten *correct* answers — and produced the JSON blobs in
§2, because the loop's final round withholds tools by design, so a model told to go and call something
with no channel left to call it through **writes the call out as prose**.

The one-line fix:

```js
if (!toolNudged && mustCheck && useTools && !anyToolRan) {   // <-- !anyToolRan is the whole thing
```

**The tell: before acting on an absence, ask what else produces that absence.** "No tool call", "no
output", "no rows", "no diff", "exit 0" — each is generated by both the failure you are hunting and by
ordinary success. Gate on a *positive* record that the work has not happened yet, never on the absence
alone.

**And guard the shape as well as the cause.** The gating fix removes the only known route to that
output; a separate check now refuses to ship a reply that *is* a tool-call blob, because the known
route is rarely the only one.

---

## 5. Resampling only your failures is a ratchet, not a measurement

**The trap.** After fixing bugs, re-running only the cases that failed is the obvious economy — the
passes already passed, and re-running 195 steps costs an hour against 11.

**It cannot produce a lower score.** Any stochastic system re-rolls both ways: some previously-failing
cases pass by luck, and some previously-*passing* cases fail. Re-rolling only the failures banks every
upward flip and hides every downward one. This is not a small effect — the first re-run of the full
lane flipped a previously-passing case to a failure within the first 16 steps.

**The tell: if a protocol can only move the number one way, it is not measuring.** Re-run the whole
lane. Where the expensive half is a frozen replay (a paid API lane recorded once), re-running the
*cheap* lane in full against it costs nothing extra and is the honest comparison — the frozen side is a
fixed benchmark and does not need to move.

---

## 6. A dashboard that computes the result is a second source of truth

**The trap.** A live progress tile recomputed the head-to-head score itself, with a header comment
claiming it "mirrors the bench line for line". It did not: the bench excluded two classes of
non-comparable step, the tile excluded one, and the tile **hardcoded the assumption** that a particular
env flag was set rather than reading it.

They happened to agree on this run because the flag genuinely was set. On any run without it, the tile
would have published a number the bench would not recognise — **and the bench itself printed no
head-to-head at all in that mode**, so the tile's number was the only one anybody ever saw.

**The tell: if two things can print the same number, one of them must be reading it.** A dashboard
should render results, never derive them. Where deriving is unavoidable, assert the two agree and fail
loudly when they do not.

**Same failure, smaller: hardcoded version strings in anything published.** The tile's header carried
the literal `v5.6.1`, typed once and then outlived by four releases — one launch away from labelling a
run of 5.6.5 as 5.6.1, in the artefact people screenshot. Read it from `package.json`. *A number nobody
re-types is a number nobody can forget to re-type.* The stale grader in §3 is the identical bug wearing
a different hat.

---

## 7. Two smaller traps that each cost a working system

**A setting validated for a floor and not a ceiling.** `historyLimit` was checked for `>= 1` and
nothing else. An admin set it to `1000`; the platform's message-fetch API caps that parameter at 100,
so **every reply thereafter died before the model was ever called**. The process stayed up, stayed
logged in, and kept sending its typing indicator — six hours, three channels, 24 failed replies and
zero successful ones.

Two things made it hide. The branch it shared rejected non-numbers and negatives, which is the input
nobody types, while a *plausible large integer* — "give it more memory" is a reasonable thing to want —
was the one shape it waved through. And the error named a **form field**, not a setting, so the log
read as a platform outage rather than as something a person had just changed. Validate both ends of
every range, and **clamp on read as well as on write**: the setter guard only covers the code path, and
config files get hand-edited. A bad value on disk should cost one degraded response, not all of them.

**Temporal dead zone: a top-level block calling a function that closes over a `const` below it.**
Node/JS hoists `function` declarations but not `const`. A file with top-level logic partway down —
here, a `--regrade` mode that runs before the main body — calls `grade()`, which references a `const`
declared just above `grade()` but *below* that block. Result: `ReferenceError: Cannot access 'X' before
initialization`, at runtime, only on that code path.

**This was walked into twice in the same file in one session**, for two different constants, because
the natural place to declare a helper is next to the function that uses it. In a file with top-level
execution, declare shared constants **at the top with the other constants**, not beside their consumer
— and run the mode that failed before relaunching anything long.
