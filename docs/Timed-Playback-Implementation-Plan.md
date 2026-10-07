# Focused Timed Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** planning; no timed-playback product code is implemented. Read the [design](Timed-Playback-Design.md) and [main roadmap](../ROADMAP.md) first.

**Goal:** dispatch existing validated output-and-resize recording events at their recorded relative times, without changing the v1 format or immediate replay.

**Architecture:** Extend `PtyRecordingReader` with one opt-in event callback path and a small internal monotonic scheduler. Reuse its streaming validator and `PtyRecordingReplayResult`; add one public timing-cap option. A reader-wide operation gate prevents interleaved reads/replays and a private core read avoids taking that gate recursively.

**Tech Stack:** C# 13, .NET 8/9/10, xUnit, `Stopwatch`, `Task.Delay`, the existing v1 codec and package-consumer harness. No new runtime dependency, native code, or recording format version.

**Spec:** `docs/Timed-Playback-Design.md`

## Global constraints

- Preserve one LGPL-3.0-or-later package and the current net8.0/net9.0/net10.0 AnyCPU targets on Windows/Linux/macOS x64/ARM64; minimum Windows build 10.0.26200.9457.
- Keep C# sources under `src/` and tooling usable from CMD, SH, and Windows PowerShell 5.1; no C/Python or third-party runtime package.
- Preserve the v1 header, frame kind/size, timestamps and markers, and existing `ReplayAsync(Stream)` immediate output-only behavior. Never launch a PTY during playback.
- Add only the reviewed public options/method to `packaging/PublicApiBaseline.txt`; no input recording, speed/pause/seek, terminal model, generic observer API, or version bump.
- Reuse reader byte/frame limits and source ownership; never include payload, caller input, or environment values in errors, diagnostics, or CI logs.
- Manual Windows laptop results are separate from hosted CI; NativeAOT remains informational. Do not merge, tag, or publish as part of implementation.
- The user previously chose native inline execution without subagents for Icod.Pty. Preserve that method unless the user explicitly changes it.

## Review focus

1. First event at 40 ms must not fire immediately merely because no prior event exists; test in Task 2 and Task 3.
2. A callback that takes 60 ms must make a later 50 ms event immediately due, rather than adding another 50 ms; test in Task 2.
3. Cancellation during a pending wait must not invoke that event's callback or return a success result; test in Task 3.
4. A syntactically valid event at `MaxEventElapsed + 1 tick` must fail before waiting or dispatching it, without calling it malformed; test in Task 3.
5. A second `ReadAsync`, immediate replay, or timed playback while a callback is pending must fail rather than interleave frames; test in Task 3.

## File and interface map

| Path | Responsibility |
| --- | --- |
| `src/PtyRecordingTimedPlaybackOptions.cs` (new) | Public configurable `MaxEventElapsed`; default one hour. |
| `src/Recording/TimedPlaybackScheduler.cs` (new) | Internal monotonic target scheduler with injected elapsed/delay functions for deterministic tests. |
| `src/PtyRecordingReader.cs` | Public `PlayTimedAsync`, single-operation guard, shared frame-reading core, result accounting and cancellation. |
| `src/Tests/Icod.Pty.Tests/PtyTimedPlaybackOptionsTests.cs` (new) | Public options and compatibility boundary. |
| `src/Tests/Icod.Pty.Tests/TimedPlaybackSchedulerTests.cs` (new) | First delay, late callback, equal times, interval cap, cancellation and long-duration arithmetic without wall-clock sleeps. |
| `src/Tests/Icod.Pty.Tests/PtyTimedPlaybackReaderTests.cs` (new) | Event dispatch, status/counts, malformed input, cap, ownership, concurrency and exceptions. |
| `src/Sample/TimedPlaybackSmokeChecks.cs` (new), `src/Sample/Program.cs` | Opt-in `--timed-playback-smoke` from the exact package, no payload printed. |
| `packaging/VerifyPackageConsumer.ps1`, `samples/README.md`, `README.md`, `packaging/PublicApiBaseline.txt` | Package and published-app gates; public contract and usage. |
| `docs/Timed-Playback-Design.md`, this plan, `ROADMAP.md` | Scope and exact-head evidence after implementation. |

## Tasks and verification

### Task 1 (TP01): Freeze the additive public contract

**Files:** `src/PtyRecordingTimedPlaybackOptions.cs` (new), `src/PtyRecordingReader.cs`, `src/Tests/Icod.Pty.Tests/PtyTimedPlaybackOptionsTests.cs` (new), `packaging/PublicApiBaseline.txt`.

**Interfaces:** `PtyRecordingTimedPlaybackOptions.MaxEventElapsed : TimeSpan` defaults to `TimeSpan.FromHours(1)`. `PtyRecordingReader.PlayTimedAsync(Func<PtyRecordingEvent, CancellationToken, ValueTask> onEvent, PtyRecordingTimedPlaybackOptions? options = null, CancellationToken cancellationToken = default) : Task<PtyRecordingReplayResult>`. An option value `<= TimeSpan.Zero` fails at call entry. Only a snapshot is used after the method yields.

- [ ] **Step 1: Write failing contract tests.** `Default_cap_is_one_hour`; `Zero_or_negative_cap_is_rejected_before_read`; `Null_callback_is_rejected_before_read`; and `Immediate_replay_remains_available_with_unchanged_signature`. Assert exact option and parameter names and default values.
- [ ] **Step 2: Run the focused tests and observe RED.** `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net10.0 --filter FullyQualifiedName~PtyTimedPlaybackOptionsTests -m:1 -nr:false -p:UseSharedCompilation=false`. Expected: missing options/method compile failure. If this shell still blocks VSTest's loopback socket, use exact-head hosted CI for the test execution gate and record the limitation; do not call an aborted run a test failure or a pass.
- [ ] **Step 3: Add minimal public declarations and validation.** Leave the full method behavior and cap snapshot case for Task 3; make the tests compile and verify option validation and immediate API compatibility. Update the public API baseline with the exact additive signatures only.
- [ ] **Step 4: Run focused GREEN across net8.0/net9.0/net10.0**, verify the baseline diff contains no removals, and commit `feat: define timed playback contract`.

### Task 2 (TP02): Implement monotonic scheduling

**Files:** `src/Recording/TimedPlaybackScheduler.cs` (new), `src/Tests/Icod.Pty.Tests/TimedPlaybackSchedulerTests.cs` (new).

**Interfaces:** `internal TimedPlaybackScheduler(Func<TimeSpan> elapsed, Func<TimeSpan, CancellationToken, Task> delay)` and `Task WaitUntilAsync(TimeSpan target, CancellationToken cancellationToken)`. Production construction captures one `Stopwatch` origin at `PlayTimedAsync` entry and supplies elapsed since that origin; the injected functions exist only inside the library/tests. Each wait request is positive, at most 30 seconds, and rechecks elapsed afterward.

- [ ] **Step 1: Write failing fake-clock tests.** `First_event_waits_from_origin`, `Equal_timestamp_keeps_file_order`, `Slow_callback_uses_absolute_target`, `Late_event_needs_no_wait`, `Long_target_is_split_into_bounded_waits`, and `Cancellation_during_wait_propagates`. The fake delay advances a controlled clock; assert requested waits and event dispatch order without real sleeps. Pin the Review Focus first/late cases.
- [ ] **Step 2: Run focused RED** with `--filter FullyQualifiedName~TimedPlaybackSchedulerTests`; require missing scheduler or behavioral failure.
- [ ] **Step 3: Implement `WaitUntilAsync`.** Compare `elapsed()` against `target`; request the smaller of remaining time and 30 seconds, with a minimum one-millisecond positive wait for a sub-millisecond remainder; check cancellation before returning. Use checked/saturating comparisons that do not overflow at a caller-selected large `TimeSpan`.
- [ ] **Step 4: Run focused GREEN on all three frameworks** and commit `feat: schedule recording events against monotonic time`.

### Task 3 (TP03–TP04): Dispatch events and preserve reader semantics

**Files:** `src/PtyRecordingReader.cs`, `src/Tests/Icod.Pty.Tests/PtyTimedPlaybackReaderTests.cs` (new), existing `PtyRecordingReaderTests.cs` where an earlier contract needs an explicit regression.

**Interfaces:** Uses `TimedPlaybackScheduler.WaitUntilAsync`, reader's validated `PtyRecordingEvent.Elapsed`, and `PtyRecordingReplayResult(Status, OutputBytes, EventCount)`. A reader-wide operation lease surrounds all of `ReplayAsync` and `PlayTimedAsync`; public `ReadAsync` acquires the same lease for one read. A private `ReadCoreAsync` performs validation while the holder owns the lease. Preserve existing public signatures and final status behavior.

- [ ] **Step 1: Write failing event/result tests.** A hand-authored complete golden record with binary output and resize calls the callback in order, preserves bytes/sizes, honors initial-size header without an invented resize, and returns exact successful dispatch counts. Valid `Truncated` and `Stopped` prefixes return their statuses; an already-finished reader returns its status with zero new events. Verify `ReplayAsync(Stream)` still runs without timing and copies output only.
- [ ] **Step 2: Write failing failure-boundary tests.** `Playback_captures_cap_before_yield` mutates the supplied options during the first callback and requires the entry snapshot for the next event. A pending callback rejects concurrent `ReadAsync`, `ReplayAsync` and second `PlayTimedAsync`; callback reentrant read fails promptly. A cancelled pending delay leaves the event undispatched. Callback exceptions propagate unchanged, including after earlier events. A malformed later frame throws `PtyRecordingFormatException` after its valid prefix was delivered. A valid event past `MaxEventElapsed` throws payload-free `InvalidOperationException` before wait/callback. Check `LeaveOpen` and disposal behavior.
- [ ] **Step 3: Run RED**, then implement the method and one-operation lease. Snapshot options before awaiting. Read one validated event, enforce its cap, await its absolute target, invoke the callback once, then increment bytes/events only after callback success. Return the reader's validated terminal status. Release the lease on every exception/cancellation, without promising a failed reader can be resumed.
- [ ] **Step 4: Run focused reader/scheduler/contract suites on all three frameworks**, then the full solution tests. Commit `feat: dispatch timed recording output and resizes`.

### Task 4 (TP05): Prove exact-package behavior

**Files:** `src/Sample/TimedPlaybackSmokeChecks.cs` (new), `src/Sample/Program.cs`, `packaging/VerifyPackageConsumer.ps1`, `samples/README.md`.

**Interfaces:** `--timed-playback-smoke` constructs or captures a v1 record with at least one output and one resize separated in time, opens it with the package reader, and calls `PlayTimedAsync`. It verifies binary output/size order, a nonzero first-event wait with generous real-time tolerance, and a valid terminal result; it prints only `PTY timed playback smoke check passed.`

- [ ] **Step 1: Add the sample dispatcher case that calls `TimedPlaybackSmokeChecks.RunAsync()` and build the sample.** Require RED because the smoke class does not exist yet; do not run the unrecognized command against the old sample, which treats it as an executable name.
- [ ] **Step 2: Implement `TimedPlaybackSmokeChecks` and include the mode in all three framework loops and the published net10.0 loop of `VerifyPackageConsumer.ps1`.** Update `samples/README.md` with CMD/PowerShell 5.1 and SH invocations, expected output, and the limits of a noninteractive check.
- [ ] **Step 3: Pack once and run metadata/artifact, exact-package consumer and published-consumer verification.** Run the portable layout harness where locally available; commit `test: verify timed playback from exact package`.

### Task 5 (TP06–TP07): Documentation, compatibility and final matrix

**Files:** `README.md`, `docs/Timed-Playback-Design.md`, `docs/Timed-Playback-Implementation-Plan.md`, `ROADMAP.md`, `packaging/PublicApiBaseline.txt`; CI files only for a demonstrated harness gap.

**Interfaces:** The public API baseline records only Task 1 additions. The documentation describes callback ownership, first-event pacing, absolute scheduling, best-effort timing, cap and failure outcomes, source ownership, binary privacy, and unchanged immediate replay.

- [ ] **Step 1: Add a concise usage example and limits to `README.md`.** Separate a valid `Truncated`/`Stopped` terminal result from format, cap, callback and cancellation exceptions. Mark the feature's manual Windows acceptance unreported unless an exact-head result is actually supplied.
- [ ] **Step 2: Review the entire PR diff** for payload leakage, cancellation after consumption, callback reentrancy, overflow, time-origin drift, reader ownership, and the additive API. Add a failing regression before any correction.
- [ ] **Step 3: Run full Release tests and Staging pack; verify the exact package and every consumer smoke on all three frameworks.** Run the complete six-platform CI matrix at the final implementation head: Windows/Linux/macOS x64/ARM64, net8.0/net9.0/net10.0, ordinary and exact-package consumers, framework-dependent/self-contained/single-file/trimmed layouts, and informational NativeAOT probes. If a test cannot start in the local sandbox, record that distinctly and rely on the CI gate rather than guessing.
- [ ] **Step 4: Record final SHA, workflow URLs, exact outcomes and expected skips in this plan and the roadmap.** Keep laptop observations separate. Commit `docs: complete timed playback acceptance` only after evidence; no version/tag/publish/merge in this tranche.

## Completion condition

The feature is ready for review when a package consumer receives output and resize events in recorded order at best-effort recorded times; the existing immediate replay and binary format remain unchanged; cap, cancellation, reader-concurrency, format, and callback failures are explicit; and the exact final head passes all six platform jobs and three frameworks. The plan stays pending until the user reviews the planning PR and authorizes implementation.
