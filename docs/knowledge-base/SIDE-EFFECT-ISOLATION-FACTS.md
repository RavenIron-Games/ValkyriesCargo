# Side-effect isolation — facts for any project that lets a model call tools

Written 2026-08-17 out of BarrkBOT, but none of it is BarrkBOT-specific. It applies to anything that
gives a model tools with real consequences and then wants to benchmark it.

---

## 1. Put the interlock at the transport, never at the tools

A guard per destructive tool needs a new entry every time a tool is added, and a forgotten entry is
silently unprotected. That is not a hypothetical — it is the exact shape of the hole below.

Count the **pipes** instead. A pipe is one outbound transport, and there are usually far fewer of
them than tools. BarrkBOT has 43 admin tools and **three** pipes:

| pipe | reaches |
|---|---|
| Discord REST | the guild — ban, kick, timeout, roles, channels, purge |
| a TCP command socket | the live game server — `say`, `kick`, `ban`, `addwhitelist`, `stopevent`, `rcon` |
| FTP | the server filesystem — config overwrite, and a `removeDir` that deletes a directory |

Three places to be right, and every tool added later inherits the protection for free.

**Put the guard inside the client factory**, not in the test harness. A harness-applied wrapper only
protects callers that remember to apply it. BarrkBOT's original interlock wrapped the REST client in
the probe; a *conditional* version of that wrapper let twenty "ban <name>" steps through and **two
real members were banned by a benchmark**. The recorded lesson was "a safety interlock must not have
a condition to get wrong", and it is right as far as it goes — the deeper version is that it must
not have a *caller* to get wrong either.

## 2. One interlock is not the blast radius — enumerate every pipe

The proxy above was correct and had been trusted for months. It covered Discord. The other two pipes
had **no guard at all**, and the tools that used them were reachable on any admin step, because a
model picking the wrong tool is the thing being measured — "the expected tool is harmless" is not an
argument.

An audit of 97 stored benchmark runs found game-server write tools had fired **hundreds of times**
down the unguarded pipes. Nothing landed, because every call named a target that did not exist and
validation or a 404 stopped it. That is the *second* layer doing the first layer's job. A single
step naming a real ID would have gone through.

**Read live state to confirm, do not reason about it.** The verdict "nothing landed" came from
reading the server's whitelist, admin list and mod directories — not from arguing that the targets
were fake.

## 3. Keep reads live

Sever writes only. A read cannot damage anything, and keeping reads real is what makes the benchmark
faithful: name resolution still returns a genuine "no member found", channel listings are real.
Severing the whole socket measures a different system.

## 4. Guard placement decides whether the test proves anything

Two failures of this exact kind, in one afternoon:

- A guard sitting **next to the write** is only reached by arguments valid enough to get that far.
  An isolation test passed three tools on *validation errors* — wrong key name, wrong list name —
  having never executed the guard. Move the guard to the **top of the function** when the function's
  whole purpose is a write.
- A test stub that is more permissive than reality proves nothing. A stub returning the whole member
  roster for any query made a fuzzy resolver "find" a member that does not exist, so a ban guard
  passed while doing the opposite of its job. Model the real API's filtering.

## 5. Prove the test can fail

A test that passes against both the broken and the fixed code is decoration. Revert the fix in a
scratch copy and re-run: the ban-target guard scored 7/7 on the fix and **4/7 on the old code**, and
only then was it worth keeping. Do this once per guard, when it is written.

## 6. Failure is data, and the data has no single shape

If tool failures are returned rather than thrown, `!result.error` is **not** a sound success test.
One codebase returned `{error}`, `{success:false}`, `{refreshed:false}`, `{found:false}`, and — worst
— `{response: "<raw text>"}` where the remote service's own "player not found" rode inside a field
that looked like success.

Write an explicit outcome predicate, and make it **pessimistic**: anything not recognisably
successful counts as failed. The two mistakes do not cost the same. A false negative suppresses a
true claim and the user asks again; a false positive tells an admin somebody was banned who was not.

## 7. Text written alongside a tool call is a prediction, not a report

A model can emit reply text in the same response as a tool call. Usually the next turn overwrites it
with the real answer. When the final turn returns *empty* text, that prediction is what ships — and
a `if (!text)` guard passes a stale string happily.

This is how "X has been removed from the server" survives a removal that never happened. Smaller
local models emit that preamble far more often than frontier ones, so moving work onto a local model
makes it **more** likely, not less. Track whether the final text predates a state-changing call, and
strike unbacked claims from it.

## 8. An invented success is worse than a crash

When an oversized request started failing, the fix was to shed tool schemas — which then discarded
the very tool the request was about, and the model announced the action as done. A rejected request
is loud, honest and recoverable. Prefer it. Mark state-changing tools as never-sheddable and let the
request fail.

## 9. Interlocks must be provider-agnostic

Guard at the pipe and a call from a cloud model takes the identical path as one from a local model,
with no per-lane branch to get wrong. If you find yourself writing "block writes when provider ===
X", the guard is in the wrong place.

## 10. Announce the interlock state in the run's own output

Have the harness print whether isolation is ON, read back **from the guard module** rather than from
the variable the harness just set, and refuse to start if it reads OFF. An import-order slip
otherwise produces a run that looks exactly like a safe one.
