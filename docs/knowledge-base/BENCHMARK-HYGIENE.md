# Benchmark hygiene — facts for measuring an LLM system without fooling yourself

Written 2026-08-22 out of a run of BarrkBOT grader rewrites and context-depth probes, but none of it is
BarrkBOT-specific. It applies to anything that compares two versions of an LLM system — a model
bake-off, a prompt A/B, a context-budget experiment, a nightly regression lane — and especially to
anything whose output is a single number somebody will act on.

Companion to [SELF-MEASUREMENT-FACTS.md](SELF-MEASUREMENT-FACTS.md), which covers a benchmark that
runs safely and tells you something false, and to
[LOCAL-INFERENCE-FACTS.md](LOCAL-INFERENCE-FACTS.md), which covers the server underneath. This one
covers the instrument itself: **the grader, the control, the fixture, the statistic and the gate —
each of which can be built so that it could never have detected the thing it was built to detect.**

The pattern they share: **a broken instrument returns a clean result, not an error.** A tidy
head-to-head, two arms that tie, a null hypothesis, a zero count. Every one of them reads as a finding,
gets written down as a finding, and closes the question.

---

## 1. A regex allow-list of accepted phrasings measures house style, not capability

**The trap.** A refusal grader — the check deciding whether "I don't have that" counts as a correct
answer — had grown by hand, one literal at a time, every time somebody noticed a phrasing it missed:

```js
/no record|nothing|hasn't|didn't|only see|no mention|.../i   // ~20 alternatives, added one at a time
```

Each alternative was added by reading an answer that should have passed and didn't. **That process has
a direction**, and the direction is whichever model's answers you were reading.

**What it cost.** Across **46 archived runs**, replacing the list with a shape test rescued **40**
local-model answers and **exactly 0** control-model answers. Not because the control refused less often
— because every phrasing the control used was already in the list, and none of the challenger's were.
The list was a description of one model's prose style. The head-to-head standing on it — **162 vs 167**
— was reporting how closely a model writes like the control.

**The fix: test the SHAPE of the claim, not the wording.** A refusal of this kind is a negation
governing a record-word. Two properties carry the whole test:

- **Within one clause.** `[^.!?]` between the halves, so a negation in one sentence cannot vouch for an
  assertion in the next.
- **In either order.** "no record of X speaking" and "X has said nothing of it" are one claim, and a
  one-directional pattern silently grades them differently.

```js
const NEG    = /\b(?:no|not|never|n't|nothing)\b/.source;
const RECORD = /\b(?:record|mention|log|said|say|spoke|speak)\b/.source;
const REFUSAL = new RegExp(`(?:${NEG}[^.!?]*${RECORD})|(?:${RECORD}[^.!?]*${NEG})`, 'i');
```

**Then prove the widening is not score inflation, using runs nobody can re-roll.** Regraded across
**20 archived arms**:

| lane | before | after |
|---|---|---|
| control | 187/195 | **187/195 — bit-identical on every single arm** |
| challenger | 185 | 188 |

A widening that moves only your side is indistinguishable from a widening that is wrong. The claim
"this fixes a grader bug, it does not flatter us" is only worth anything when the control lane is
re-scored too and reported as not having moved.

---

## 2. Widening a grader without an exemption list launders wrong answers into passes

**The trap.** The shape test in §1 is strictly more permissive than the list it replaced, and
permissiveness is not free. On its own it would have passed **11** answers that recited a live
world-feed payload — coordinates, relative timestamps, session durations, roster counts — at questions
asking what somebody **said**. Those are wrong in the most expensive way available: confident,
specific, and about a different subject entirely.

**What it would have cost.** Eleven passes, all of them in the lane the widening was built to help —
which is exactly the lane whose number was under scrutiny.

**The fix: bolt the widening to a narrow substance veto, and get the veto's signal right.** The signal
is a **VALUE that was never asked about** — a coordinate, a duration, a count — **not the name of the
tool it came from.** That distinction is the entire design:

- vetoed: an answer reciting session durations and roster counts at "what did X say?"
- passes untouched: *"the Chronicle shows no record of X speaking"* — that cites a source, which is
  what a good refusal does.

A veto written against tool *names* kills the second one. The second one is the answer you want.

**Calibrate before shipping, against the lane the veto must never touch.** Over **1,196** control
answers in the affected categories, the requirement was that the veto fire **ZERO** times. Not
"rarely" — zero. A veto with a nonzero false-positive rate on known-good answers is a second grader bug
arriving disguised as the fix for the first.

---

## 3. A control that the experiment cannot make fail is not a control

**The trap.** A depth probe placed one fact at the **head**, **middle** and **tail** of a conversation
and asked about it after trimming. Head and tail were the controls: the fact should survive in those
positions whatever the trimmer does, so a failure there means the probe is broken rather than the
system.

It had two independent faults, and both wore a control's name.

**Fault one — the question was answerable without the transcript.** It was phrased so the model
satisfied it from a wiki lookup instead of the conversation. **The probe was measuring the router.**
Every arm could have "passed" with the transcript empty.

**Fault two — the fixture was sized from an ASSUMED tokens-per-message figure.** At the size that
assumption produced, even the **10%-deep "head"** was already inside the region being cut. The head
control was not outside the experiment at all; it was a second measurement of the treated condition,
labelled as a baseline.

**What it cost — and what it bought.** Fault one was exposed in **three steps**: the head control
failed three times in a row, which is not a thing a working probe does. That is the entire argument
for keeping controls even when they feel like wasted steps. The control's job is to fail when the
instrument is wrong, and this one did it immediately.

**The rule.** Before believing any arm, ask what would have to be true for the control to fail, and
check the experiment can actually make it happen. A control that sits inside the treated region, or
that can be satisfied from a source the experiment never touches, is decoration.

---

## 4. Two arms scoring identically is a broken instrument at least as often as a real null

**The trap.** The over-correction to §3 was a smaller fixture — **45 lines**. At both context budgets
under test it trimmed **nothing**. Both arms therefore sent **byte-identical prompts, 11,529 tokens**,
and returned **5/5 vs 5/5**.

That is a clean result. It reads as "the budget does not matter here", which is a publishable finding,
and it is in fact the strongest available evidence that the experiment never ran.

**The tell was in the system's own instrumentation, not the score.** The trimmer already printed what
it had done, once per step:

```
transcript trimmed to N tok: X/Y messages dropped
```

Two arms whose scores tie and whose trim lines are identical have not been compared.

**What it cost.** A full run of both arms, and a null that would have been written down as a result.

**Three rules fall out, and the third is the cheap one:**

1. **Before believing a null, verify the arms actually differ in the way intended** — from the system's
   own instrumentation, never from the score.
2. **Size fixtures from a MEASURED ratio**, never an assumed one. This is the same assumption that
   broke §3, reappearing one section later in a new costume.
3. **Run ONE step and read the instrumentation before committing to a full run.**

The corrected **220-line** fixture separated cleanly: **0/5 vs 5/5**, with the controls pinned at **5/5
on both sides** — which is what a real effect looks like standing next to a working control.

---

## 5. The mean cannot see an effect that lives on a subset of steps

**The trap.** Raising a context budget dropped a 195-step score from **189 to 183**. The hypothesis was
that tool-schema shedding — dropping tool definitions when the prompt got tight — had been acting as an
accidental precision filter, and that relieving the pressure had removed the filter.

It was tested with mean tools-per-turn:

| arm | mean tools/turn |
|---|---|
| before | 24.5 |
| after | 24.0 |

Indistinguishable. The hypothesis was declared dead, in writing.

**It was correct.** Only about **13 of 195** turns ever shed anything. A real effect on 13 turns was
then averaged over **323 calls**, where it disappeared. The mean was not evidence against the
hypothesis — it was incapable of being evidence in either direction.

**The statistic that showed it was the extreme:**

| arm | turns above 47 tools | peak |
|---|---|---|
| before | 0 | ≤47 |
| after | **26** | **59** |

**What it cost.** A correct explanation for a six-point regression, closed and recorded as dead.

**The rule.** When an effect is expected on a subset, **measure the subset or the extreme** — a max, or
a count over a threshold — **never the mean over everything.** And before accepting a null, ask the
prior question: *could the statistic I used have shown this effect at all?* A null from a statistic
with no power is not a null. It is a missing measurement wearing one.

---

## 6. A gate that passes on an empty file has not passed

**The trap.** A probe was launched with the working directory set wrong. It died immediately with
`ERR_MODULE_NOT_FOUND`, before issuing a single request. The wrapper then reported:

```
context overflows / HTTP 400s: 0
```

Which was **true**. The wrapper counted occurrences of `HTTP 400` in the error log, and a log holding
one module-resolution failure holds none of those.

```
# what the gate did
grep -c 'HTTP 400' "$LOG"      # -> 0  ->  "0 overflows"  ->  green

# what the log actually contained
ERR_MODULE_NOT_FOUND
```

**What it cost.** The gate's entire purpose on that run: it reported the safe number for a probe that
had not executed one step.

**The rule: every gate must assert a POSITIVE artefact before it is allowed to report a count.** Name a
specific line the run emits when it really ran, and require it first:

```
grep -q '<the line a successful step prints>' "$LOG" || die 'gate did not run'
grep -c  'HTTP 400' "$LOG"
```

**Absence of an error string is not evidence of success.** A file that was never written, a run that
died in its first second, and a flawless run all produce the same grep result. It is the same shape as
the identical arms in §4, and one layer down it is the same shape as a health endpoint answering 200
for the wrong model.

---

## 7. Free the run from the session, and verify it rather than assuming

**The trap.** Long runs belong in a transient unit rather than in your shell:

```
systemd-run --user --unit=<name> ...
```

But `--user` only outlives your disconnection if the user manager **lingers**. Without linger, logging
out tears down the user manager and everything beneath it — including the run you deliberately
detached. Neither fact is visible from the run's own output, so check both:

```
loginctl show-user "$(id -un)" -p Linger     # want: Linger=yes
cat /proc/<pid>/cgroup                       # want: .../app.slice/...
                                             # NOT:  .../session-NN.scope
```

The cgroup is the one that settles it. **A process in `session-NN.scope` is killed when the session
ends**, whatever the unit looks like from the outside; a process in `app.slice` is not attached to your
login at all.

**What it costs when it is wrong.** A run that ends the moment you disconnect, with no error anywhere.
Just a results directory that stopped growing.

**Then note the trade, and state it in writing.** Transient units do **not** come back after a reboot.
So a long queue is one of exactly two things, decided on purpose:

- a real enabled unit, or
- an accepted loss on reboot — **accepted and written down**, not discovered afterwards.

The reboot half of this trade is [LOCAL-INFERENCE-FACTS.md](LOCAL-INFERENCE-FACTS.md) §6. Linger and
the cgroup are what make the transient case work at all in the meantime.

---

## Checklist before believing a benchmark number

- [ ] no grader check is an allow-list of phrasings — each tests the **shape** of the claim
- [ ] every widening is regraded on archived arms, and the **control lane's** movement is reported
- [ ] every widening carries an exemption veto, calibrated to fire **zero** times on known-good answers
- [ ] the veto keys on a value never asked about, not on the name of the source
- [ ] each control can actually fail — outside the treated region, and needs the source under test
- [ ] the arms provably differ: instrumentation read, not scores compared
- [ ] fixtures sized from a **measured** ratio, and **one step** run and read before the full run
- [ ] a tie between arms is a suspected broken instrument until the instrumentation lines differ
- [ ] the statistic could have shown the effect — subset or extreme where the effect lives on a subset
- [ ] every gate asserts a positive expected line **before** it reports any count
- [ ] `Linger=yes`, cgroup in `app.slice`, and the reboot case chosen on purpose
