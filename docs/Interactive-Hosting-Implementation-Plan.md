# Interactive Hosting and Controlled Shutdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL for the recommended native execution approach: use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Recommendation: implement in the current session without implementation subagents; the user can select a different execution approach when reviewing the plan.

**Goal:** Make the PTY library and sample support immediate interaction, terminal interrupts, explicit shutdown policy, and cancellable startup on all six supported platforms.

**Architecture:** Add shared lifecycle contracts to `PtyProcess` while retaining platform-specific creation and resource ownership. Keep output draining under the consumer's control. Place host-console mode handling and transparent forwarding in the sample, outside the public library API.

**Tech Stack:** C# 13, .NET 8/9/10, AnyCPU, Windows ConPTY, Unix OS PTYs and the existing managed helper, xUnit, CMD/SH/PowerShell 5.1-compatible tooling.

**Spec:** [Interactive hosting design](Interactive-Hosting-Design.md). Read it together with this plan and the [main roadmap](../ROADMAP.md).

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.

This is a planning change. The selected scope is approved; the proposed design and development sequence are presented for review before product implementation. No package version bump is part of this planning PR.

## Review focus

1. Cancellation immediately after native creation must clean up before the startup task completes; no abandoned successful factory result. Owned by IH01 and IH02.
2. A shutdown request blocked by input backpressure must respect the grace budget and caller cancellation. Owned by IH04.
3. A child using raw input must receive byte `0x03`; interrupt handling must not kill the sample or require Enter. Owned by IH03 and IH06.
4. Host console modes, encodings, and pending reads must be restored/stopped after launch failure and normal exit. Owned by IH05 and IH06.
5. A descendant retaining a terminal handle must not make the sample report a clean drain or wait forever. Owned by IH06.

## Sequence and evidence

| Tranche | Deliverable | Depends on | State |
| --- | --- | --- | --- |
| IH01 | Shared asynchronous startup ownership and Windows creation | Approved design/plan | Planned |
| IH02 | Asynchronous Unix handshake and cancellation cleanup | IH01 | Planned |
| IH03 | Terminal interrupt input contract | IH01-IH02 | Planned |
| IH04 | Controlled shutdown contracts and coordinator | IH01-IH03 | Planned |
| IH05 | Immediate-input sample and host-console restoration | IH01-IH04 | Planned |
| IH06 | Interactive integration and failure-path acceptance | IH03-IH05 | Planned |
| IH07 | Package consumer coverage and documentation | IH01-IH06 | Planned |
| IH08 | Six-platform verification and completion review | IH01-IH07 | Planned |

Implement these sequentially. Each tranche ends with focused verification and a commit. New behavior uses a failing test before implementation; document-only corrections do not require artificial tests. Run the broader matrix after the integrated changes, and repeat it only when changes or failures warrant it.

Use the existing project layout. New tests live under `src/Tests/Icod.Pty.Tests/`, fixture sources under `src/Tests/Icod.Pty.TestChild/`, and sample sources under `src/Sample/`. Project files stay at their existing paths. Existing sample/test glob exclusions in `Icod.Pty.csproj` must continue excluding those sources from the public DLL.

### IH01: asynchronous startup ownership and Windows creation

**Files:** modify `src/PtyProcess.cs`, `src/Windows/WindowsBackend.cs`; create `src/Properties/AssemblyInfo.cs`, `src/Tests/Icod.Pty.Tests/PtyStartupTests.cs`, and `src/Tests/Icod.Pty.Tests/ControlledBackend.cs`.

**Interfaces:**
- Produces `public static Task<PtyProcess> StartAsync(PtyStartInfo startInfo, CancellationToken cancellationToken = default)`.
- Produces internal `Task<PtyProcess> StartCoreAsync(LaunchConfiguration launch, CancellationToken cancellationToken, Func<LaunchConfiguration, CancellationToken, Task<IPtyBackend>> backendFactory)` on `PtyProcess`.
- Produces internal Windows `Task<IPtyBackend> StartAsync(LaunchConfiguration launch, CancellationToken cancellationToken)`.
- `ControlledBackend` implements existing `IPtyBackend` with configurable streams and an exit `TaskCompletionSource<int>`, plus `DisposeCount` and `TerminateCount` observations. Grant only `Icod.Pty.Tests` internal access.

- [ ] Add failing tests `Precancelled_start_does_not_invoke_factory`, `Cancellation_after_creation_disposes_before_completion`, `Successful_start_transfers_ownership`, and `Factory_failure_preserves_original_exception`. Gate the per-call factory with `TaskCompletionSource` using asynchronous continuations; count calls/disposals explicitly.

```csharp
Assert.Equal(0, factoryCalls); // precancelled path
Assert.Equal(1, backend.DisposeCount); // cancelled after factory returns
Assert.Equal(0, backend.DisposeCount); // successful ownership transfer
```

- [ ] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~PtyStartupTests`. Expect missing API/failed assertions before implementation; restore/build failures unrelated to the tests are not the expected red result.
- [ ] Implement snapshot-before-work, the internal ownership seam, and Windows worker-based creation. Check cancellation before native creation and before ownership transfer; dispose a backend returned after cancellation. Do not cancel only the wait on a still-running creation task.
- [ ] Route synchronous `Start` through the shared ownership path without changing its validation or timeout contract. Initially the Unix factory may wrap existing synchronous creation; IH02 replaces that wrapper. Use `ConfigureAwait(false)` on the shared await path.
- [ ] Add real-process tests for captured argument/environment mutation after `StartAsync` returns its task, synchronous/async startup equivalence, and invocation under a non-pumping synchronization context. Use an internal gated factory for deterministic snapshot timing.
- [ ] Rerun `PtyStartupTests` on net8.0, net9.0, and net10.0, plus the existing argument/environment and missing-executable tests. Expect all pass; successful instances remain usable until disposed.
- [ ] Commit as `feat: add cancellable asynchronous PTY startup ownership` and record the tested platforms/frameworks.

### IH02: asynchronous Unix helper handshake

**Files:** modify `src/Unix/UnixBackend.cs`, `src/PtyStartInfo.cs`; extend `src/Tests/Icod.Pty.Tests/PtyStartupTests.cs` and the existing helper failure tests in `src/Tests/Icod.Pty.Tests/PtyTests.cs`.

**Interfaces:**
- Consumes IH01 ownership transfer and cancellation token.
- Produces internal Unix `Task<IPtyBackend> StartAsync(LaunchConfiguration launch, CancellationToken cancellationToken)`.
- Keeps `StartTimeout` as a Unix handshake-only timeout, default `TimeSpan.FromSeconds(15)`.

- [ ] Add failing tests `Unix_cancelled_handshake_reaps_helper`, `Unix_handshake_timeout_is_not_caller_cancellation`, and `Unix_caller_cancellation_wins_over_handshake_timeout`. Use a temporary SH helper that records its PID and keeps the inherited status pipe open; coordinate readiness through a bounded PID-file observation and clean the fixture in `finally`. Do not add C or Python fixtures.

```csharp
Assert.Equal(callerToken, cancelledException.CancellationToken);
Assert.IsType<TimeoutException>(timeoutException);
Assert.False(helperStillRunning);
```

- [ ] Run the Unix-specific startup tests on local Linux net10.0; expect failure against the old synchronous handshake path.
- [ ] Await status/stdout, diagnostics/stderr, and configuration writing with linked handshake/caller cancellation. Dispose linked token sources. Await or observe every started pipe task on both success and failure. Close the PTY, terminate/reap the helper, and dispose process resources before reporting failure.
- [ ] Replace the Unix wrapper from IH01 with true handshake awaits, preserving helper lookup, dotnet host lookup, exec diagnostics, and the close-on-exec ready-byte protocol. Document that native creation and cleanup are not hard deadline guarantees.
- [ ] Verify cancellation before creation, while the handshake is blocked, and after backend creation. Retain exec failure, missing helper handshake, explicit host path, and timeout tests. Run on all three local frameworks; require macOS x64/ARM64 validation in IH08.
- [ ] Commit as `feat: make Unix PTY startup cancellation-aware`.

### IH03: terminal interrupt input

**Files:** modify `src/PtyProcess.cs`; create `src/Tests/Icod.Pty.Tests/PtyInterruptTests.cs`, `src/Tests/Icod.Pty.TestChild/TerminalModes.cs`; extend `src/Tests/Icod.Pty.TestChild/Program.cs`.

**Interfaces:**
- Produces `public ValueTask SendInterruptAsync(CancellationToken cancellationToken = default)`.
- Fixture `TerminalModes` supplies `IDisposable EnterRawInput()` and `IDisposable EnterProcessedInput()`; the returned scope restores captured modes. These are fixture-local facilities, not public library API.
- Add fixture modes `raw-input` and `interrupt-handler`: emit a ready marker after mode/handler setup; raw input reports byte values; the handler reports interruption and continues until an explicit quit command.

- [ ] Add failing tests `Interrupt_writes_exactly_one_etx_byte`, `Raw_child_receives_interrupt_as_input`, `Processed_child_handles_interrupt_and_remains_usable`, `Interrupt_after_exit_is_rejected`, and `Cancelled_interrupt_does_not_terminate_child`. Ready markers must precede input.

```csharp
Assert.Equal(new byte[] { 0x03 }, capturedInput);
Assert.False(process.HasExited); // handler acknowledged and continued
```

- [ ] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~PtyInterruptTests`; expect missing API/failed behavior before implementation.
- [ ] Implement the method using existing stream writes, a disposal check, and an already-collected-exit check. Add XML documentation defining terminal input, mode dependence, single-writer coordination, and partial-write cancellation. Do not use `GenerateConsoleCtrlEvent` on the hosting console or signal an assumed Unix PID/group.
- [ ] Implement the C# fixtures with OS-correct terminal settings. With the handler enabled, Ctrl+C must affect the child fixture while the test host survives. With raw mode enabled, assert the literal `0x03` byte. Restore settings on fixture exit.
- [ ] Run the new tests for all local frameworks and retain all-six-platform execution in IH08. Commit as `feat: expose terminal interrupt input`.

### IH04: controlled shutdown and results

**Files:** create `src/PtyShutdownOptions.cs`, `src/PtyShutdownResult.cs`, `src/ShutdownCoordinator.cs`, `src/Tests/Icod.Pty.Tests/PtyShutdownTests.cs`; modify `src/PtyProcess.cs` and the test-child modes; extend `ControlledBackend.cs` with a token-aware blocked input stream.

**Interfaces:**
- Produces the exact `PtyShutdownOptions`, `PtyShutdownStatus`, and `PtyShutdownResult` types specified in the design.
- Produces `public Task<PtyShutdownResult> ShutdownAsync(PtyShutdownOptions options, CancellationToken cancellationToken = default)`.
- Internal coordinator: `Task<PtyShutdownResult> RunAsync(IPtyBackend backend, byte[] request, TimeSpan gracePeriod, bool forceTermination, TimeSpan terminationTimeout, CancellationToken cancellationToken)`.
- Options are snapshotted in the public entry point; a per-process operation guard rejects concurrent shutdowns and is always released.

- [ ] Add failing tests for all four result-table rows: `Graceful_request_collects_exit`, `Timeout_without_force_keeps_session_usable`, `Timeout_with_force_collects_exit`, and `Termination_collection_timeout_reports_requested_force`. Use the controlled backend for collection timeout and the real child for normal/forced termination.

```csharp
Assert.Equal(PtyShutdownStatus.Exited, graceful.Status);
Assert.Equal(23, graceful.ExitCode);
Assert.False(graceful.ForcedTerminationRequested);
Assert.Equal(PtyShutdownStatus.TimedOut, timedOut.Status);
Assert.Null(timedOut.ExitCode);
Assert.True(forced.ForcedTerminationRequested);
```

- [ ] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~PtyShutdownTests`; expect missing contracts or failing result assertions.
- [ ] Implement validation, request-byte snapshot, guard, and the design's state machine. Defaults are empty request, 5-second grace, `ForceTermination = false`, and 5-second termination collection. Durations accept only finite positive values up to `int.MaxValue` milliseconds. Use one grace timer across write and wait. Recheck exit/caller cancellation before escalation; do not read or dispose output.
- [ ] Add `Blocked_request_obeys_grace_budget`, `Caller_cancellation_never_initiates_force`, `Concurrent_shutdown_is_rejected`, `Retry_after_timeout_is_allowed`, `Request_snapshot_is_independent`, `Write_failure_does_not_escalate`, and `Already_exited_skips_request`. Assert exact request bytes, `TerminateCount`, guard release, and nullable exit-code rules. Gate races with the controlled backend rather than relying on scheduler timing.
- [ ] Add a real fixture that emits a large final marker sequence before exit and drain it concurrently; assert every expected byte/marker survives graceful shutdown. Add concurrent disposal and natural-exit-versus-escalation cases, permitting only the documented exceptions/results and never double cleanup.
- [ ] Run new tests on each local framework and existing final-output, cancellation, and disposal tests. Commit as `feat: add explicit PTY shutdown policy and results`.

### IH05: interactive sample and host-console lifecycle

**Files:** create `src/Sample/HostConsole.cs`, `src/Sample/WindowsHostConsole.cs`, `src/Sample/UnixHostConsole.cs`, `src/Sample/InteractiveSession.cs`, and `src/Tests/Icod.Pty.Tests/InteractiveSampleTests.cs`; modify `src/Sample/Program.cs`, `samples/Icod.Pty.Sample/Icod.Pty.Sample.csproj`, `tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj`, and `packaging/VerifyPackageConsumer.ps1`.

**Interfaces:**
- Internal sample `HostConsole : IDisposable` exposes `static HostConsole Open()`, `Stream Output { get; }`, `PtySize? GetSize()`, and `ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)`; it owns mode changes and restoration, not the operating system's standard handles.
- `WindowsHostConsole` and `UnixHostConsole` implement that boundary. A `null` size means a transient unavailable/zero size.
- Internal `InteractiveSession.RunAsync(PtyStartInfo startInfo, HostConsole console, CancellationToken cancellationToken)` returns `Task<int>`.
- The test project references the sample for build ordering with `ReferenceOutputAssembly=false` and copies its complete output to a `sample/` fixture directory. Its C# sources remain only under root `src/`.

- [ ] Add a failing nested-PTY test `Interactive_sample_forwards_single_key_without_enter`: launch the sample inside an outer PTY, wait for the inner raw-input fixture's ready marker, write `x` with no newline, and assert the fixture reports byte `0x78` within the standard bounded test deadline.
- [ ] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~InteractiveSampleTests`; confirm the current line-oriented sample cannot satisfy the single-key assertion.
- [ ] Implement host mode scopes. Windows captures modes/code pages, enables VT transport, disables local echo/line/processed input, and restores every changed setting. Unix captures termios, applies raw input/output transport using separate Linux/macOS layouts, and restores without discarding unread user input. Preserve borrowed standard handles.
- [ ] Implement cancellable host reads. Unix uses readiness polling with cancellation checks. Windows uses a dedicated reader thread for synchronous console reads and `CancelSynchronousIo`, with per-session thread ownership and repeated cancellation/completion coordination to cover the gap before a read begins. Never target a shared thread-pool thread. Wait for read completion before restoration or buffer reuse; treat an operation that completed normally during cancellation as completed input, not a cancellation API failure.
- [ ] Implement `InteractiveSession` using IH01 startup and IH04 shutdown. Start one input pump, one output pump, and resize monitoring at 100 ms intervals; suppress duplicate/zero dimensions. Forward bytes without parsing VT or encoding keys. On EOF use the design's empty-request 5-second grace and optional force, then its 5-second drain deadline. Stop and await input/resize tasks before restoring host modes.
- [ ] Update CLI dispatch for implicit interactive mode, `--interactive`, `--line`, `--smoke`, optional `--`, and executable arguments. Require explicit line/smoke mode for redirected host input/output. Preserve default-shell resolution and existing smoke output.
- [ ] Update the fresh-package consumer verifier in the same tranche to copy every C# file under `src/Sample/`, retaining relative paths, and enable the sample's unsafe setting if needed. Splitting the sample must not break the existing consumer build; IH07 adds the new API smoke checks.
- [ ] Verify immediate bytes, no extra sample echo, initial dimensions, resize forwarding, CLI argument preservation, redirected-input diagnostics, and unchanged smoke behavior. Commit as `feat: add an interactive PTY forwarding sample`.

### IH06: interactive failure paths and acceptance

**Files:** extend `src/Tests/Icod.Pty.Tests/InteractiveSampleTests.cs`, `src/Tests/Icod.Pty.TestChild/Program.cs`, and `TerminalModes.cs`; create `src/Tests/Icod.Pty.TestChild/HostConsoleProbe.cs` and `samples/README.md`.

**Interfaces:**
- Fixture mode `host-console-probe` runs the sample on its inherited terminal, snapshots native modes/encodings before and after it, reports restoration, and remains usable to read a follow-up command.
- Fixture mode `hold-terminal-open` starts a descendant that retains the slave terminal after the primary exits. Record its PID and explicitly terminate it in test cleanup; primary-child ownership does not imply ownership of this descendant.
- Reuse the fixture's `raw-input` and `interrupt-handler` modes from IH03; use unique markers so screen repaint cannot satisfy an assertion with stale output.

- [ ] Add failing tests `Sample_restores_host_after_start_failure`, `Sample_restores_host_after_child_exit`, `Sample_interrupt_reaches_child_not_host`, `Sample_resize_reaches_inner_child`, and `Sample_reports_drain_timeout_for_retained_terminal`. Inspect modes in the supervising fixture on the same terminal; checking a different terminal is insufficient.

```csharp
Assert.True(modesRestored);
Assert.True(supervisorAcceptedFollowupInput);
Assert.Equal(1, sampleExitCode); // retained terminal drain timeout
```

- [ ] Add chunk-boundary cases containing UTF-8, arrow-key VT sequences, and terminal-query replies. Assert the raw fixture receives the original bytes in order without requiring Enter; do not assert that the library renders them.
- [ ] Exercise natural exit while host input is blocked, failure after partial console setup, cancellation immediately before a read starts, and an output pump fault. Fix only the uncovered sample lifecycle paths. Use a fixture-local fault seam for partial setup if needed; do not add public CLI flags or production environment switches solely for tests.
- [ ] Run `InteractiveSampleTests` and all interrupt/shutdown tests on the local frameworks. Confirm bounded cleanup with an outer test timeout and explicit cleanup of descendants even when assertions fail.
- [ ] Write manual acceptance commands and expected observations in `samples/README.md`: CMD and Windows PowerShell 5.1 shell editing/history/Tab/Escape, a long-running command interrupted with Ctrl+C, resize during output, exit, then restored editing/echo in the original shell. Include Unix SH commands and an optional locally installed full-screen editor check. External editor checks are recorded separately from automated fixture coverage.
- [ ] Commit as `test: verify interactive forwarding and terminal restoration`.

### IH07: package consumers and user documentation

**Files:** modify `packaging/VerifyPackageConsumer.ps1`, `README.md`, `samples/README.md`, and public XML comments in `src/PtyProcess.cs`, `src/PtyStartInfo.cs`, `src/PtyShutdownOptions.cs`, `src/PtyShutdownResult.cs`. Add sample verification switches in `src/Sample/Program.cs` and create `src/Sample/PackageSmokeChecks.cs` for use without a host terminal.

**Interfaces:**
- `--lifecycle-smoke` launches the platform shell through `StartAsync`, begins output draining, calls `ShutdownAsync` with an encoded `exit` request and `ForceTermination=false`, and requires exit plus complete drain.
- `--cancel-start-smoke` uses an already-cancelled token and requires `OperationCanceledException` with no returned process.
- `--interrupt-smoke` starts the same managed program with `--interrupt-child`, waits for its readiness marker, invokes `SendInterruptAsync`, requires acknowledgement, and then requests exit. The child owns its interrupt handler and needs no external test fixture.
- `PackageSmokeChecks` implements `Task<int> RunLifecycleAsync()`, `Task<int> RunCancelledStartAsync()`, `Task<int> RunInterruptAsync()`, and `Task<int> RunInterruptChildAsync()`; each mode has a 30-second outer verification deadline, deterministic cleanup, and returns 0 only after its assertions pass.
- These are documented verification commands, not hidden runtime hooks. `--smoke` remains compatible. All smoke modes are valid with redirected host input/output.

- [ ] Add the three new smoke modes and managed verification-child dispatch; keep their implementation in `PackageSmokeChecks.cs`. Resolve the current program's launch form correctly for both `dotnet Consumer.dll` and an apphost executable. Start output draining before awaiting any acknowledgement or exit.
- [ ] Extend the fresh-package consumer verifier from IH05 to run all four smoke modes for net8.0/net9.0/net10.0 and after net10.0 publication. Continue compiling C# 13/AnyCPU from the freshly packed local package in an isolated cache. Verify the complete sample source and managed helper assets accompany the consumer.
- [ ] Update README examples for async start, interrupt, graceful request, timeout/force result, and output draining. Document single-writer coordination, partial-write cancellation, primary-child ownership, Unix timeout meaning, and interactive versus line sample behavior. Avoid implying that cancelled startup has a hard native deadline or that a Ctrl+C request proves termination.
- [ ] Run `dotnet build Icod.Pty.sln -c Release`, `dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts`, then `powershell -NoProfile -File packaging/VerifyPackageConsumer.ps1 -ArtifactDirectory artifacts` on Windows or the equivalent `pwsh -NoProfile -File ...` on Unix. Expect one package and every consumer/publish check to pass. Actual PowerShell 5.1 execution remains mandatory on Windows x64 CI.
- [ ] Commit as `docs: verify and document interactive PTY package usage`.

### IH08: integrated validation and handoff

**Files:** update `ROADMAP.md`, this plan's evidence record, and sample acceptance notes as evidence arrives. Change `.github/workflows/pull-request.yaml`, `main.yaml`, `distribution-validation.yaml`, or `release.yaml` only if a concrete verification gap requires it; retain the six-platform matrix and one-package release model.

**Interfaces:** consumes the completed API, sample, fixtures, and package verifier from IH01-IH07. Produces reviewable evidence; it does not authorize merge or publication.

- [ ] Run the complete Release build/test locally for all three target frameworks. Require zero build warnings/errors and no regression in the existing foundation tests.
- [ ] Push the implementation and inspect PR checks for Windows x64 (`windows-latest`), Windows ARM64 (`windows-11-arm`), Linux x64 (`ubuntu-latest`), Linux ARM64 (`ubuntu-24.04-arm`), macOS x64 (`macos-26-intel`), and macOS ARM64 (`macos-latest`). Record run URL, head SHA, frameworks, and any genuine skip with its reason. Platform-specific fixture branches must not hide a missing implementation of common behavior.
- [ ] Verify package contents and fresh/published consumers, including actual Windows PowerShell 5.1 tooling. Preserve AnyCPU, helper deployment, and library-only package output.
- [ ] Have the user run the documented Windows laptop acceptance on build 10.0.26200.9457 or later; record exact build, shell, runtime, and outcomes. CI evidence does not substitute for this check.
- [ ] Review resource ownership, cancellation boundaries, stream exclusivity, mode restoration, disposal races, and unsupported-platform behavior against the design. Resolve actionable findings and rerun only affected gates plus required CI.
- [ ] Mark each verified tranche complete, update the main roadmap, and report readiness for user review. Leave merging, version/tag selection, and publication to a separate instruction.

## Requirement coverage

| Selected requirement | Tranches | Acceptance evidence |
| --- | --- | --- |
| Fully interactive sample | IH05-IH06 | Single-key input, VT/UTF-8 preservation, resize, no extra echo, restored host settings. |
| Terminal interrupt operation | IH03, IH06-IH07 | Exactly one ETX byte, processed/raw child behaviors, live hosting process, package consumer. |
| Controlled shutdown | IH04, IH06-IH07 | Four result states, request backpressure deadline, cancellation, force policy, final output. |
| Cancellable asynchronous startup | IH01-IH02, IH07 | Ownership race tests, no precancelled spawn, Unix handshake cleanup, package consumer. |
| Expanded interactive verification | IH06-IH08 | Three frameworks, six platforms, PowerShell 5.1, nested PTY checks, laptop acceptance. |

## Evidence record

Planning baseline: main commit `2613ba527955d341fe04d81317d633ce28df58fb`, following merged PR #1. This PR changes documentation only. Implementation tranches are not yet executed; the unchecked steps above are the pending work, not failed checks.

For each completed tranche, record the implementation commit, commands/run links, platforms/frameworks actually exercised, and any remaining acceptance work. The final acceptance record must distinguish automated fixture coverage from manually verified shells or full-screen applications.
