# Local inference — facts for serving GGUF models unattended

Written 2026-08-21 out of a day of BarrkBOT model bake-offs on a four-GPU box, but almost none of it
is BarrkBOT-specific. It applies to anything running `llama-server` under systemd on hardware you
cannot see, and especially to anything that launches a long unattended run and walks away.

Companion to [SELF-MEASUREMENT-FACTS.md](SELF-MEASUREMENT-FACTS.md), which covers a benchmark that
runs safely and tells you something false. This one covers the layer underneath: **the server that
reports itself healthy while serving the wrong thing, or nothing at all.**
Companion also to [BENCHMARK-HYGIENE.md](BENCHMARK-HYGIENE.md), which covers the instrument on top —
graders, controls, fixtures, statistics and gates that return a clean number from a measurement that
never happened.

The pattern they share: **every failure here looks like patience.** A unit stuck in `activating`, a
health poll that never returns, an empty results file — none of them are errors. They are exactly
what a large model loading slowly looks like. That is what makes them expensive: you wait.

---

## 1. `systemd` strips double quotes out of `ExecStart`

**The trap.** A unit line written as:

```
ExecStart=... --jinja --chat-template-kwargs {"enable_thinking":false}
```

reaches the process as `{enable_thinking:false}` — **not valid JSON**. systemd's own quote handling
consumes the double quotes before `argv` is built. `llama-server` rejects it and exits 1.

**What it cost.** Seven minutes of a health poll against a server that had already died, with both
GPUs sitting empty, on the first launch of an overnight run. Had nobody looked, the unattended arm
would have polled until its timeout and reported a model failure that was a punctuation failure.

**The fix.** Wrap the whole JSON value in single quotes:

```
--chat-template-kwargs '{"enable_thinking":false}'
```

**Then verify what systemd actually built, not what you typed:**

```
systemctl --user cat <unit> | grep ExecStart      # what you wrote
systemctl --user show <unit> -p ExecStart          # what it will RUN — read this one
```

The second form prints the parsed `argv[]`. Any unit argument containing `{`, `"`, `%`, `$` or a
space deserves that check before the first launch, not after the first mystery.

---

## 2. `activating` and "loading a big model" are the same picture

**The trap.** `systemctl status` said `activating (start)`. That is the correct, expected state for a
14B model paging onto a GPU over thirty-plus seconds. It is also the state of a unit whose binary
exited immediately and is being restarted on a backoff. **Unit state cannot distinguish them.**

**The tell is downstream of systemd.** Check the thing the model would be occupying:

```
# AMD/Vulkan
rocm-smi --showmeminfo vram
# NVIDIA
nvidia-smi --query-gpu=memory.used --format=csv
```

**VRAM climbing = loading. VRAM flat at zero after 60s = dead**, whatever the unit says. A health
poll should never be the first thing that notices; by the time it times out you have burned its
entire budget.

**This is not hypothetical. It happened here, six times in a row, the same day this was written.**
A queue ran six models through a 172-step benchmark. Its stop-list named the six *base* units but
not the six *per-arm* units it created itself, so each arm's server could not bind the port the
previous arm still held. It exited; the previous model kept answering; the health poll passed; the
arm scored. All six arms benchmarked the same model. The scores came back 53, 59, 53, 55, 54, 56 —
a tight, plausible band that reads exactly like "six small models are all mediocre," and is in fact
one model's run-to-run variance.

**The script printed the truth every single time** — `serving phi4-mini.gguf`, once per arm, in its
own log — and nobody read it, because nothing failed. A sibling script that asserted the same string
instead of logging it ran correctly on the same box on the same day.

**Corollary — a health endpoint that answers 200 is not proof of the right model.** `llama-server`
answers `/health` for whatever it loaded. Read the served model back and compare it to what you
intended:

```
curl -s localhost:$PORT/v1/models | jq -r '.data[0].id'
```

Assert on that string, and **make the mismatch fatal to the arm**. Logging it is not enough; the log
is only read after something else has already gone wrong. An arm that silently benchmarks the
previous model produces a full, plausible, completely worthless result set — and it looks like a
successful run.

**Two rules fall out of this, and the second is the one that actually saves you:**

1. **Stop by enumeration, not by list.** Derive the stop-set from `systemctl list-unit-files` at
   runtime and subtract what you mean to keep. A hand-maintained list of units to stop is wrong the
   moment the script starts creating units of its own — which is exactly what happened above.
2. **A benchmark arm must refuse to run when its identity cannot be confirmed.** Skipping an arm
   costs an hour. Recording it costs the credibility of every number in the table, including the
   ones that were right, because afterwards you cannot tell which were which.

---

## 3. `-ub` is a hard floor for embeddings: oversize inputs are REJECTED, not truncated

**The trap.** `-c` (context) is what everyone sets. `-ub` (micro-batch, default **512**) is what
actually bounds an embedding request, because embedding takes the non-splittable path through the
batching code. An input longer than `n_ubatch` does not get truncated, degraded, or warned about at
half quality — **the request errors.**

This is the opposite of the generation path's behaviour, which is why it surprises people: for chat,
`-ub` is a throughput knob you can leave alone.

**What it cost.** Nothing, because it was caught before launch — but the measurement is the point.
Against a 4,895-chunk corpus:

| | tokens |
|---|---|
| p50 | 93 |
| max | 651 |
| **chunks over 512** | **12** |

Twelve. Two of the five encoders queued (both 2048-ctx models) would have hard-errored on exactly
those twelve chunks, mid-run, hours in — and a 4,883/4,895 index is still an index. It builds. It
answers. It is quietly missing its longest, most information-dense passages.

**The rule: set `-ub` and `-b` equal to `-c` for any embedding server.**

```
-c 2048 -ub 2048 -b 2048     # a 2048-ctx encoder
-c 512  -ub 512  -b 512      # a 512-ctx encoder — the cap is real, chunk to it
```

**And measure your corpus's token maximum before you pick the model**, not after. `max`, not `p99` —
the failure is per-request, so one long chunk is enough.

---

## 4. Pass `--pooling` as a tripwire, not as a setting

**The trap.** Pooling type lives in GGUF metadata (`NONE=0, MEAN=1, CLS=2, LAST=3, RANK=4`). Get it
wrong and you get vectors — wrong ones. Cosine similarity still returns numbers in [-1, 1], retrieval
still returns a top-k, and nothing anywhere reports an error.

**The technique.** `llama.cpp` warns **only when the flag disagrees with the metadata**. So pass the
value you believe is correct, and treat the *absence* of a warning as the confirmation:

```
llama-server -m encoder.gguf --embedding --pooling mean ...
# a warning here = your belief about this model is wrong; silence = the GGUF agrees
```

You get a free assertion on every start for the cost of one flag. This is the general shape worth
stealing: **state your assumption to the tool in a form it can contradict.**

---

## 5. Per-encoder prefixes are part of the model, and omitting them is silent

Embedding models trained with asymmetric prefixes produce **degraded but entirely plausible** vectors
without them. There is no error and no obvious quality cliff in a spot check — just a retrieval
system that is quietly worse than the model you paid for.

They are not interchangeable, and the query side usually differs from the document side:

| family | query | document |
|---|---|---|
| nomic-embed | `search_query: ` | `search_document: ` |
| bge | an instruction sentence prepended to the query | bare |
| EmbeddingGemma | `task: search result \| query: ` | `title: none \| text: ` |
| Qwen3-Embedding | instruct wrapper on the query | bare |

**Keep the prefix table adjacent to the model registry in code**, so adding a model without adding
its prefixes is visible in the diff. A comparison across encoders where one arm is missing its
prefixes is not a comparison.

---

## 6. `systemd-run` transient units do not survive a reboot

**The trap.** `systemd-run --user --unit=foo` is the right tool for "launch this overnight run and
let me close the laptop." It is the wrong tool for anything that must survive the machine
restarting: transient units exist only in the running systemd's memory. A reboot — a kernel update,
a power blip, an OOM-triggered restart — and there is no unit, no file, no trace, and no error
anywhere. Just a results directory that stopped growing.

**What to do about it:** decide explicitly, and write the relaunch command down where the person who
finds the silence will look.

- **Fine as transient:** anything you will check within a day, on a box with known uptime.
- **Needs a real unit file** (`~/.config/systemd/user/`, `Restart=`, `WantedBy=`): anything longer,
  and anything whose loss costs GPU-hours to redo.

Make long runs **resumable at the arm boundary** — write each arm's results to disk as it finishes
and skip completed arms on start. Then a reboot costs one arm instead of the run, and the relaunch
is a command rather than a decision.

---

## 7. `enabled` and `running` are different questions, and the gap is silent until it isn't

**The trap.** On a box serving several models, `systemctl --user is-active` and
`systemctl --user is-enabled` can disagree — and only the second one decides what comes back after a
reboot.

Found live: the correct production model was running but **disabled**, while a different model — the
one that had leaked raw tool calls to users, and had been deliberately taken out of service — was
**enabled at boot.** Nothing was wrong. Nothing would have been wrong until the next reboot silently
swapped production's brain for the known-bad one, with every service healthy and every log clean.

**Check the pair after any unit work, and check both columns:**

```
for u in $(systemctl --user list-unit-files 'llama-*' --no-legend | awk '{print $1}'); do
  printf '%-28s active=%-10s enabled=%s\n' "$u" \
    "$(systemctl --user is-active "$u")" "$(systemctl --user is-enabled "$u")"
done
```

Any unit where those two disagree is a question. Usually the answer is "fine, deliberate." Once it
won't be.

---

## 8. Measure the fit; do not compute it

**The trap.** Estimating VRAM from parameter count and quantisation is close enough to be dangerous.
KV cache scales with `context × slots` and is frequently the larger term; the compute buffer, the
Vulkan/ROCm allocator's own overhead, and fragmentation are all real and none are in the arithmetic.

**What actually happened:** the question was "does the 8B fit one 8176 MiB card at 32K context with
2 slots?" The arithmetic said comfortably. The measurement said **7692 MiB used, 484 MiB free** — it
fits, but the quantisation was *forced* by that headroom, not chosen. A better quant plus its KV
would not have fit, and would have been discovered by an OOM eight hours into an unattended run.

**Start it once, read the number, then design around the number.** Ten minutes, and it converts an
assumption into a fact you can put in a plan.

**Corollary:** when a fit is that tight, say so in writing. "Q5_K_M" reads like a preference; "Q5_K_M
is forced by 484 MiB of headroom at `-c 32768 -np 2`" tells the next person why they cannot casually
upgrade it.

---

## 9. Gate an unattended run on a cheap smoke test with a hard pass/fail

Long arms are expensive to lose and expensive to *not* lose — a broken arm that runs to completion
burns the hours anyway and then hands you a plausible number.

Put a short, fast, **assertive** step between "server up" and "run the real thing." Not a health
check — an actual small slice of the real work, with numeric gates:

```
run 8 real steps
gate: http_400_count == 0 AND tool_call_count >= 1
```

Both halves matter. Zero 400s catches malformed wire protocol (one model in this bake-off failed
every request on tool-call-id handling and would otherwise have scored 0 and looked merely bad at the
task). At least one tool call catches a model that answers fluently while ignoring its tools —
which grades as a capability result when it is actually a template result.

**Eight steps cost about a minute against fifty-five for the full arm.** A gate that fires once pays
for every time it doesn't.

---

## Checklist before walking away from an unattended run

- [ ] `systemctl --user show <unit> -p ExecStart` — read the **parsed** argv, not the file
- [ ] VRAM climbing on the intended device within 60s
- [ ] `/v1/models` returns the model you meant, asserted in the script
- [ ] `-ub` ≥ corpus token **max** for every embedding server
- [ ] `--pooling` passed and **no** warning printed
- [ ] query/document prefixes present for every encoder in the comparison
- [ ] smoke gate with numeric pass/fail before the long arm
- [ ] `is-active` vs `is-enabled` agree, or the disagreement is deliberate
- [ ] transient vs real unit chosen on purpose; relaunch command written down
- [ ] each arm writes results as it completes, and completed arms are skipped on restart
