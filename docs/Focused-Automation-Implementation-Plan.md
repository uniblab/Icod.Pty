# Focused Automation Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task. Do not dispatch subagents for Icod.Pty work. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in, bounded live byte matching and sequential send/expect scripting above `PtySession`.

**Architecture:** An automation buffer owned by `SessionCoordinator` observes successfully forwarded output from the existing pump. `PtySession.ExpectAsync` consumes matches from one bounded cursor; `PtyScriptRunner` composes that operation with the existing ordered input writer without taking ownership of the session.

**Tech Stack:** C# 13, .NET 8/9/10, existing pure-C# PTY backends and package-consumer harness, Windows/Linux/macOS x64/ARM64.

**Spec:** [Focused-Automation-Design.md](Focused-Automation-Design.md). The [main roadmap](../ROADMAP.md) records the selection.

## Global constraints

- Base work on merged PR #7 (`main` at merge `5ecb2ac08cd6f41b7a9040d48f163a5b97a7c891`); recheck the actual base SHA before implementation. Preserve its public API and version-1 recording format except deliberate additive signatures.
- Preserve one LGPL-3.0-or-later NuGet package, C# 13, net8.0/net9.0/net10.0 AnyCPU, Windows/Linux/macOS x64/ARM64, and Windows minimum build 10.0.26200.9457.
- Keep source under `src/`, CMD/SH/PowerShell 5.1-compatible tooling, and no C/Python. Preserve the external Unix managed helper and installed `dotnet` prerequisite.
- No subagent dispatch for this Icod.Pty work. Implement on the feature PR in reviewable commits; write behavior tests first and record observed RED/GREEN evidence. Do not invent a RED phase for documentation or feasibility checks.
- No input capture, version change, publication, regex, terminal parsing, timed replay, generic observers, or automatic session disposal in the library runner. Never commit captured terminal payloads or send bytes containing secrets.

## File and interface map

| Path | Responsibility |
| --- | --- |
| `src/PtyAutomationOptions.cs`, `src/PtyExpectResult.cs`, `src/PtyScriptStep.cs`, `src/PtyScriptResult.cs`, `src/PtyScriptRunner.cs` | Opt-in configuration, immutable result/step types, and ordered script composition. |
| `src/PtySessionOptions.cs`, `src/PtySession.cs`, `src/Session/SessionConfiguration.cs` | Capture and validate automation options, reject competing input source, expose expectation and runner concurrency gate. |
| `src/Session/SessionAutomation.cs` | Bounded unconsumed byte window, streaming matcher, single waiter and cursor; handle EOF, failure, overrun, cancellation, and disposal. |
| `src/Session/SessionCoordinator.cs`, `src/Session/SessionPumps.cs` | Observe accepted output after destination write; finish automation independently when output settles. |
| `src/Tests/Icod.Pty.Tests/PtyAutomationContractTests.cs`, `PtyAutomationMatcherTests.cs`, `PtyAutomationSessionTests.cs`, `PtyScriptRunnerTests.cs` | Validation, stream/chunk/limit correctness, lifecycle and script tests. |
| `src/Sample/AutomationSmokeChecks.cs`, `src/Sample/Program.cs`, `packaging/VerifyPackageConsumer.ps1`, `packaging/VerifyPortableConsumer.ps1` | Opt-in native and installed-package `--automation-smoke`; retain all published-consumer modes. |
| `packaging/PublicApiBaseline.txt`, `README.md`, `samples/README.md`, `ROADMAP.md`, this plan and design | Intentional API additions, safe usage, limits, evidence, and selection status. |

## Review focus

1. Prompt output occurs during startup before the caller awaits `ExpectAsync`: match retained output without a missed prompt (FA02/FA04).
2. Pattern straddles one-byte reads and a send receives a reply before the following expect starts: match exactly once, with trailing bytes retained (FA02/FA05).
3. Continuous nonmatching output overruns the cap while the destination succeeds: automation reports overrun; session output and recording continue (FA03/FA04).
4. Output destination partially writes then throws, or recorder faults: do not claim failed output as matched; keep recorder, output, and script results independent (FA04).
5. Cancellation, timeout, EOF, process failure, or disposal races with an active expectation: settle once, free waiter/timer, retain the documented cursor and stream ownership (FA03/FA04/FA05).

## Gates

| Gate | Evidence |
| --- | --- |
| A | Contract and immutable byte-input tests, baseline signature diff, focused net8.0 RED/GREEN. |
| B | Matcher, stream integration, and script tests across all three frameworks, including five review-focus cases. |
| C | Native and exact-package `--automation-smoke` on all six runners and three frameworks; all PR #6/#7 published modes remain green. |
| D | Final-head Release build, complete Staging matrix, package artifacts, published consumers, docs/self-review, and separately recorded Windows laptop acceptance. |

Each tranche ends in a reviewable commit. Append evidence below with exact SHA, command, platform/TFM, expected/actual result, and CI URL. A green prior head does not qualify a changed final head.

| Tranche | Head | Command / gate | Result |
| --- | --- | --- | --- |
| Baseline | Pending | Full existing tests and package smoke on base commit | Pending. |
| FA01–FA07 | Pending | Targeted and final gates below | Pending. |

### FA01: contract and capture (Gate A)

**Files:** `src/PtyAutomationOptions.cs`, `src/PtyExpectResult.cs`, `src/PtyScriptStep.cs`, `src/PtyScriptResult.cs`, `src/PtySessionOptions.cs`, `src/Session/SessionConfiguration.cs`, `src/Tests/Icod.Pty.Tests/PtyAutomationContractTests.cs`, `packaging/PublicApiBaseline.txt`.

**Interfaces:** `PtySessionOptions.Automation` is nullable; `PtyAutomationOptions.MaxBufferedOutputBytes` defaults to 65,536 and accepts 1–1,048,576. `PtySession.ExpectAsync(ReadOnlyMemory<byte>, TimeSpan, CancellationToken)` returns `Task<PtyExpectResult>` with status `PtyExpectStatus`. `PtyScriptStep.Send` and `.Expect` copy bytes. Freeze final additive signatures before production matcher work.

- [ ] Add contract tests asserting cap bounds, required automation opt-in, rejected `Input`, empty/oversize pattern, invalid timeout, early capture of mutable options, copied step bytes, and unchanged disabled defaults.
- [ ] Run focused `dotnet test src/Tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Staging -f net8.0 --filter FullyQualifiedName~PtyAutomationContractTests`; record actual RED failures.
- [ ] Implement captured options and public types, minimally satisfying the tests while retaining startup failure ownership of caller streams.
- [ ] Run focused tests on net8.0/net9.0/net10.0 and review the exact intentional API baseline additions; commit `test: define focused automation contract`.

### FA02: bounded streaming match (Gate B)

**Files:** `src/Session/SessionAutomation.cs`, `src/Tests/Icod.Pty.Tests/PtyAutomationMatcherTests.cs`.

**Interfaces:** internal automation instance accepts successful output chunks, holds at most `MaxBufferedOutputBytes` unconsumed bytes, and serves one cursor. `ExpectAsync` consumes through the first occurrence, leaving suffix bytes. A second pending expect is rejected.

- [ ] Add matcher tests for binary NUL/invalid UTF-8, overlapping patterns, one-byte chunk splits, leading junk plus two consecutive matches, early output, timeout/cancellation preserving cursor, and two concurrent expectations.
- [ ] Run focused matcher tests on net10.0 and record RED; implement bounded streaming search with a linear-time prefix algorithm or equivalent, copying caller pattern before awaiting.
- [ ] Run matcher tests on all three TFMs; commit `feat: add bounded byte matcher`.

### FA03: limit and terminal outcomes (Gate B)

**Files:** `src/Session/SessionAutomation.cs`, `src/Tests/Icod.Pty.Tests/PtyAutomationMatcherTests.cs`.

**Interfaces:** immutable `PtyExpectResult` reports `Matched`, `TimedOut`, `OutputEnded`, `OutputStopped`, `OutputTimedOut`, `OutputFaulted`, or `BufferLimitExceeded`; no terminal content appears in errors. Monotonic deadline applies to matching; output completion settles active and future waits after searching retained accepted bytes.

- [ ] Add tests for exact cap, cap+1 after repeated mismatches, repeated output after overrun, buffered match before EOF, output EOF/stop/drain timeout/fault before and during wait, primary exit with descendant-held output, cancellation/deadline races, disposed automation, and released waiter/timer resources.
- [ ] Run focused tests net10.0 to record RED, implement finite buffering and terminal state transitions without throwing into the output pump.
- [ ] Rerun focused tests on all TFMs and commit `feat: settle automation limits and completion`.

### FA04: session integration (Gate B)

**Files:** `src/PtySession.cs`, `src/Session/SessionCoordinator.cs`, `src/Session/SessionPumps.cs`, `src/Tests/Icod.Pty.Tests/PtyAutomationSessionTests.cs`.

**Interfaces:** `PtySession.ExpectAsync` delegates to session automation; accepted output is observed after destination write and before the next chunk. Output fault/EOF completes matching independently of `Completion` and `RecordingCompletion`.

- [ ] Add controlled-stream tests for startup prompt, successful destination write, partial-write failure, recorder sink fault, overrun while live output continues, output drain/stop, dispose race, and disabled-session regression.
- [ ] Run focused session tests net10.0 to record RED; wire the existing output pump to automation with no work in the disabled path and no caller callbacks while coordinator locks are held.
- [ ] Rerun new and existing session/recording tests on all TFMs; commit `feat: observe live session output for expectations`.

### FA05: script composition (Gate B)

**Files:** `src/PtyScriptRunner.cs`, `src/PtyScriptStep.cs`, `src/PtyScriptResult.cs`, `src/Tests/Icod.Pty.Tests/PtyScriptRunnerTests.cs`.

**Interfaces:** `PtyScriptRunner.RunAsync(PtySession, IReadOnlyList<PtyScriptStep>, CancellationToken)` executes copied Send/Expect steps serially and returns completed step count plus failed index/status. The caller retains ownership; one runner per session; direct concurrent operations have no ordering guarantee.

- [ ] Add script tests for early prompt, send then immediate response, multiple expects sharing retained suffix, empty send, timeout at a specific index, cancellation during send/expect, write fault, concurrent runner rejection, and caller-owned session surviving script failure.
- [ ] Run focused runner tests net10.0 to record RED; implement only sequential orchestration and a per-session runner gate.
- [ ] Rerun runner/session suites on all TFMs and commit `feat: add ordered send expect runner`.

### FA06: native and packaged consumer (Gate C)

**Files:** `src/Sample/AutomationSmokeChecks.cs`, `src/Sample/Program.cs`, package verification scripts, native tests.

**Interfaces:** `--automation-smoke` launches a deterministic managed child, verifies early prompt, binary cross-chunk response, consecutive matching and timeout/retry, then disposes its session; prints only a pass/fail message without payload bytes.

- [ ] Write the smoke and native failure-path tests; run it locally with `dotnet run --project samples/Icod.Pty.Sample -c Staging -f net10.0 -- --automation-smoke` and record any observed RED.
- [ ] Wire the exact packed NuGet consumer and every existing published form to run the smoke while retaining PR #6/#7 checks.
- [ ] Run the complete six-platform/three-framework PR workflow and record each job, package mode, expected ConPTY skip, and SHA. Fix demonstrated defects with failing regressions and rerun; commit `test: qualify packaged automation`.

### FA07: compatibility, docs, and acceptance (Gate D)

**Files:** API baseline, `README.md`, `samples/README.md`, `ROADMAP.md`, this plan and design; production files only for reproduced defects.

**Interfaces:** public additions remain opt-in; version-1 recording bytes and existing session defaults are unchanged.

- [ ] Stress continuous unmatched output, many short chunks, repeated timeout/cancellation, script retries, and concurrent dispose under watchdogs; inspect maximum retained memory and session/recording outcomes.
- [ ] Document usage, byte matching and cursor rules, cap/overrun, timeout and disposal responsibility, confidentiality, and ConPTY limitation. Self-review all five review-focus cases and actual additive API diff.
- [ ] Run `dotnet build Icod.Pty.sln -c Release`, full `dotnet test Icod.Pty.sln -c Staging -f net8.0` (repeat net9.0/net10.0), and final-head six-platform package/published-consumer CI. Record commands, counts, SHA, URLs, skips, and Windows laptop smoke separately; commit `docs: record automation acceptance` only with actual evidence.

## Completion condition

A package consumer can opt into bounded live byte expectations, run ordered sends/expectations without losing output between steps, distinguish timeout/EOF/fault/overrun, and retain control of its session. A failed matcher leaves the live output and optional recorder intact. Final-head tests cover all three target frameworks and six runners; any unverified manual environment stays explicit. Merge, release version, and publication are separate decisions.
