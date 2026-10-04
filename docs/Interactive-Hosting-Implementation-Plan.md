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

The design and development sequence were approved for implementation on 2026-10-04. PR #2 implements the selected scope; verification is recorded below. Package version selection, merge, and publication remain separate decisions.

## Review focus

1. Cancellation immediately after native creation must clean up before the startup task completes; no abandoned successful factory result. Owned by IH01 and IH02.
2. A shutdown request blocked by input backpressure must respect the grace budget and caller cancellation. Owned by IH04.
3. A child using raw input must receive byte `0x03`; interrupt handling must not kill the sample or require Enter. Owned by IH03 and IH06.
4. Host console modes, encodings, and pending reads must be restored/stopped after launch failure and normal exit. Owned by IH05 and IH06.
5. A descendant retaining a terminal handle must not make the sample report a clean drain or wait forever. Owned by IH06.

## Sequence and evidence

| Tranche | Deliverable | Depends on | State |
| --- | --- | --- | --- |
| IH01 | Shared asynchronous startup ownership and Windows creation | Approved design/plan | Complete |
| IH02 | Asynchronous Unix handshake and cancellation cleanup | IH01 | Complete |
| IH03 | Terminal interrupt input contract | IH01-IH02 | Complete |
| IH04 | Controlled shutdown contracts and coordinator | IH01-IH03 | Complete |
| IH05 | Immediate-input sample and host-console restoration | IH01-IH04 | Complete |
| IH06 | Interactive integration and failure-path acceptance | IH03-IH05 | Automated coverage complete; native limitation recorded |
| IH07 | Package consumer coverage and documentation | IH01-IH06 | Complete |
| IH08 | Six-platform verification and completion review | IH01-IH07 | Automated verification/review complete; laptop acceptance pending |

Implement these sequentially. Each tranche ends with focused verification and a commit. New behavior uses a failing test before implementation; document-only corrections do not require artificial tests. Run the broader matrix after the integrated changes, and repeat it only when changes or failures warrant it.

Use the existing project layout. New tests live under `src/Tests/Icod.Pty.Tests/`, fixture sources under `src/Tests/Icod.Pty.TestChild/`, and sample sources under `src/Sample/`. Project files stay at their existing paths. Existing sample/test glob exclusions in `Icod.Pty.csproj` must continue excluding those sources from the public DLL.

### IH01: asynchronous startup ownership and Windows creation

**Files:** modify `src/PtyProcess.cs`, `src/Windows/WindowsBackend.cs`; create `src/Properties/AssemblyInfo.cs`, `src/Tests/Icod.Pty.Tests/PtyStartupTests.cs`, and `src/Tests/Icod.Pty.Tests/ControlledBackend.cs`.

**Interfaces:**
- Produces `public static Task<PtyProcess> StartAsync(PtyStartInfo startInfo, CancellationToken cancellationToken = default)`.
- Produces internal `Task<PtyProcess> StartCoreAsync(LaunchConfiguration launch, CancellationToken cancellationToken, Func<LaunchConfiguration, CancellationToken, Task<IPtyBackend>> backendFactory)` on `PtyProcess`.
- Produces internal Windows `Task<IPtyBackend> StartAsync(LaunchConfiguration launch, CancellationToken cancellationToken)`.
- `ControlledBackend` implements existing `IPtyBackend` with configurable streams and an exit `TaskCompletionSource<int>`, plus `DisposeCount` and `TerminateCount` observations. Grant only `Icod.Pty.Tests` internal access.

- [x] Add failing tests `Precancelled_start_does_not_invoke_factory`, `Cancellation_after_creation_disposes_before_completion`, `Successful_start_transfers_ownership`, and `Factory_failure_preserves_original_exception`. Gate the per-call factory with `TaskCompletionSource` using asynchronous continuations; count calls/disposals explicitly.

```csharp
Assert.Equal(0, factoryCalls); // precancelled path
Assert.Equal(1, backend.DisposeCount); // cancelled after factory returns
Assert.Equal(0, backend.DisposeCount); // successful ownership transfer
```

- [x] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~PtyStartupTests`. Expect missing API/failed assertions before implementation; restore/build failures unrelated to the tests are not the expected red result.
- [x] Implement snapshot-before-work, the internal ownership seam, and Windows worker-based creation. Check cancellation before native creation and before ownership transfer; dispose a backend returned after cancellation. Do not cancel only the wait on a still-running creation task.
- [x] Route synchronous `Start` through the shared ownership path without changing its validation or timeout contract. Initially the Unix factory may wrap existing synchronous creation; IH02 replaces that wrapper. Use `ConfigureAwait(false)` on the shared await path.
- [x] Add real-process tests for captured argument/environment mutation after `StartAsync` returns its task, synchronous/async startup equivalence, and invocation under a non-pumping synchronization context. Use an internal gated factory for deterministic snapshot timing.
- [x] Rerun `PtyStartupTests` on net8.0, net9.0, and net10.0, plus the existing argument/environment and missing-executable tests. Expect all pass; successful instances remain usable until disposed.
- [x] Commit as `feat: add cancellable asynchronous PTY startup ownership` and record the tested platforms/frameworks.

### IH02: asynchronous Unix helper handshake

**Files:** modify `src/Unix/UnixBackend.cs`, `src/PtyStartInfo.cs`; extend `src/Tests/Icod.Pty.Tests/PtyStartupTests.cs` and the existing helper failure tests in `src/Tests/Icod.Pty.Tests/PtyTests.cs`.

**Interfaces:**
- Consumes IH01 ownership transfer and cancellation token.
- Produces internal Unix `Task<IPtyBackend> StartAsync(LaunchConfiguration launch, CancellationToken cancellationToken)`.
- Keeps `StartTimeout` as a Unix handshake-only timeout, default `TimeSpan.FromSeconds(15)`.

- [x] Add failing tests `Unix_cancelled_handshake_reaps_helper`, `Unix_handshake_timeout_is_not_caller_cancellation`, and `Unix_caller_cancellation_wins_over_handshake_timeout`. Use a temporary SH helper that records its PID and keeps the inherited status pipe open; coordinate readiness through a bounded PID-file observation and clean the fixture in `finally`. Do not add C or Python fixtures.

```csharp
Assert.Equal(callerToken, cancelledException.CancellationToken);
Assert.IsType<TimeoutException>(timeoutException);
Assert.False(helperStillRunning);
```

- [x] Run the Unix-specific startup tests on local Linux net10.0; expect failure against the old synchronous handshake path.
- [x] Await status/stdout, diagnostics/stderr, and configuration writing with linked handshake/caller cancellation. Dispose linked token sources. Await or observe every started pipe task on both success and failure. Close the PTY, terminate/reap the helper, and dispose process resources before reporting failure.
- [x] Replace the Unix wrapper from IH01 with true handshake awaits, preserving helper lookup, dotnet host lookup, exec diagnostics, and the close-on-exec ready-byte protocol. Document that native creation and cleanup are not hard deadline guarantees.
- [x] Verify cancellation before creation, while the handshake is blocked, and after backend creation. Retain exec failure, missing helper handshake, explicit host path, and timeout tests. Run on all three local frameworks; require macOS x64/ARM64 validation in IH08.
- [x] Commit as `feat: make Unix PTY startup cancellation-aware`.

### IH03: terminal interrupt input

**Files:** modify `src/PtyProcess.cs`; create `src/Tests/Icod.Pty.Tests/PtyInterruptTests.cs`, `src/Tests/Icod.Pty.TestChild/TerminalModes.cs`; extend `src/Tests/Icod.Pty.TestChild/Program.cs`.

**Interfaces:**
- Produces `public ValueTask SendInterruptAsync(CancellationToken cancellationToken = default)`.
- Fixture `TerminalModes` supplies `IDisposable EnterRawInput()` and `IDisposable EnterProcessedInput()`; the returned scope restores captured modes. These are fixture-local facilities, not public library API.
- Add fixture modes `raw-input` and `interrupt-handler`: emit a ready marker after mode/handler setup; raw input reports byte values; the handler reports interruption and continues until an explicit quit command.

- [x] Add failing tests `Interrupt_writes_exactly_one_etx_byte`, `Raw_child_receives_interrupt_as_input`, `Processed_child_handles_interrupt_and_remains_usable`, `Interrupt_after_exit_is_rejected`, and `Cancelled_interrupt_does_not_terminate_child`. Ready markers must precede input.

```csharp
Assert.Equal(new byte[] { 0x03 }, capturedInput);
Assert.False(process.HasExited); // handler acknowledged and continued
```

- [x] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~PtyInterruptTests`; expect missing API/failed behavior before implementation.
- [x] Implement the method using existing stream writes, a disposal check, and an already-collected-exit check. Add XML documentation defining terminal input, mode dependence, single-writer coordination, and partial-write cancellation. Do not use `GenerateConsoleCtrlEvent` on the hosting console or signal an assumed Unix PID/group.
- [x] Implement the C# fixtures with OS-correct terminal settings. With the handler enabled, Ctrl+C must affect the child fixture while the test host survives. With raw mode enabled, assert the literal `0x03` byte. Restore settings on fixture exit.
- [x] Run the new tests for all local frameworks and retain all-six-platform execution in IH08. Commit as `feat: expose terminal interrupt input`.

### IH04: controlled shutdown and results

**Files:** create `src/PtyShutdownOptions.cs`, `src/PtyShutdownResult.cs`, `src/ShutdownCoordinator.cs`, `src/Tests/Icod.Pty.Tests/PtyShutdownTests.cs`; modify `src/PtyProcess.cs` and the test-child modes; extend `ControlledBackend.cs` with a token-aware blocked input stream.

**Interfaces:**
- Produces the exact `PtyShutdownOptions`, `PtyShutdownStatus`, and `PtyShutdownResult` types specified in the design.
- Produces `public Task<PtyShutdownResult> ShutdownAsync(PtyShutdownOptions options, CancellationToken cancellationToken = default)`.
- Internal coordinator: `Task<PtyShutdownResult> RunAsync(IPtyBackend backend, byte[] request, TimeSpan gracePeriod, bool forceTermination, TimeSpan terminationTimeout, CancellationToken cancellationToken)`.
- Options are snapshotted in the public entry point; a per-process operation guard rejects concurrent shutdowns and is always released.

- [x] Add failing tests for all four result-table rows: `Graceful_request_collects_exit`, `Timeout_without_force_keeps_session_usable`, `Timeout_with_force_collects_exit`, and `Termination_collection_timeout_reports_requested_force`. Use the controlled backend for collection timeout and the real child for normal/forced termination.

```csharp
Assert.Equal(PtyShutdownStatus.Exited, graceful.Status);
Assert.Equal(23, graceful.ExitCode);
Assert.False(graceful.ForcedTerminationRequested);
Assert.Equal(PtyShutdownStatus.TimedOut, timedOut.Status);
Assert.Null(timedOut.ExitCode);
Assert.True(forced.ForcedTerminationRequested);
```

- [x] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~PtyShutdownTests`; expect missing contracts or failing result assertions.
- [x] Implement validation, request-byte snapshot, guard, and the design's state machine. Defaults are empty request, 5-second grace, `ForceTermination = false`, and 5-second termination collection. Durations accept only finite positive values up to `int.MaxValue` milliseconds. Use one grace timer across write and wait. Recheck exit/caller cancellation before escalation; do not read or dispose output.
- [x] Add `Blocked_request_obeys_grace_budget`, `Caller_cancellation_never_initiates_force`, `Concurrent_shutdown_is_rejected`, `Retry_after_timeout_is_allowed`, `Request_snapshot_is_independent`, `Write_failure_does_not_escalate`, and `Already_exited_skips_request`. Assert exact request bytes, `TerminateCount`, guard release, and nullable exit-code rules. Gate races with the controlled backend rather than relying on scheduler timing.
- [x] Add a real fixture that emits a large final marker sequence before exit and drain it concurrently; assert every expected byte/marker survives graceful shutdown. Add concurrent disposal and natural-exit-versus-escalation cases, permitting only the documented exceptions/results and never double cleanup.
- [x] Run new tests on each local framework and existing final-output, cancellation, and disposal tests. Commit as `feat: add explicit PTY shutdown policy and results`.

### IH05: interactive sample and host-console lifecycle

**Files:** create `src/Sample/HostConsole.cs`, `src/Sample/WindowsHostConsole.cs`, `src/Sample/UnixHostConsole.cs`, `src/Sample/InteractiveSession.cs`, and `src/Tests/Icod.Pty.Tests/InteractiveSampleTests.cs`; modify `src/Sample/Program.cs`, `samples/Icod.Pty.Sample/Icod.Pty.Sample.csproj`, `tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj`, and `packaging/VerifyPackageConsumer.ps1`.

**Interfaces:**
- Internal sample `HostConsole : IDisposable` exposes `static HostConsole Open()`, `Stream Output { get; }`, `PtySize? GetSize()`, and `ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)`; it owns mode changes and restoration, not the operating system's standard handles.
- `WindowsHostConsole` and `UnixHostConsole` implement that boundary. A `null` size means a transient unavailable/zero size.
- Internal `InteractiveSession.RunAsync(PtyStartInfo startInfo, HostConsole console, CancellationToken cancellationToken)` returns `Task<int>`.
- The test project references the sample for build ordering with `ReferenceOutputAssembly=false` and copies its complete output to a `sample/` fixture directory. Its C# sources remain only under root `src/`.

- [x] Add a failing nested-PTY test `Interactive_sample_forwards_single_key_without_enter`: launch the sample inside an outer PTY, wait for the inner raw-input fixture's ready marker, write `x` with no newline, and assert the fixture reports byte `0x78` within the standard bounded test deadline.
- [x] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~InteractiveSampleTests`; confirm the current line-oriented sample cannot satisfy the single-key assertion.
- [x] Implement host mode scopes. Windows captures modes/code pages, enables VT transport, disables local echo/line/processed input, and restores every changed setting. Unix captures termios, applies raw input/output transport using separate Linux/macOS layouts, and restores without discarding unread user input. Preserve borrowed standard handles.
- [x] Implement cancellable host reads. Unix uses readiness polling with cancellation checks. Windows uses a dedicated reader thread for synchronous console reads and `CancelSynchronousIo`, with per-session thread ownership and repeated cancellation/completion coordination to cover the gap before a read begins. Never target a shared thread-pool thread. Wait for read completion before restoration or buffer reuse; treat an operation that completed normally during cancellation as completed input, not a cancellation API failure.
- [x] Implement `InteractiveSession` using IH01 startup and IH04 shutdown. Start one input pump, one output pump, and resize monitoring at 100 ms intervals; suppress duplicate/zero dimensions. Forward bytes without parsing VT or encoding keys. On EOF use the design's empty-request 5-second grace and optional force, then its 5-second drain deadline. Stop and await input/resize tasks before restoring host modes.
- [x] Update CLI dispatch for implicit interactive mode, `--interactive`, `--line`, `--smoke`, optional `--`, and executable arguments. Require explicit line/smoke mode for redirected host input/output. Preserve default-shell resolution and existing smoke output.
- [x] Update the fresh-package consumer verifier in the same tranche to copy every C# file under `src/Sample/`, retaining relative paths, and enable the sample's unsafe setting if needed. Splitting the sample must not break the existing consumer build; IH07 adds the new API smoke checks.
- [x] Verify immediate bytes, no extra sample echo, initial dimensions, resize forwarding, CLI argument preservation, redirected-input diagnostics, and unchanged smoke behavior. Commit as `feat: add an interactive PTY forwarding sample`.

### IH06: interactive failure paths and acceptance

**Files:** extend `src/Tests/Icod.Pty.Tests/InteractiveSampleTests.cs`, `src/Tests/Icod.Pty.TestChild/Program.cs`, and `TerminalModes.cs`; create `src/Tests/Icod.Pty.TestChild/HostConsoleProbe.cs` and `samples/README.md`.

**Interfaces:**
- Fixture mode `host-console-probe` runs the sample on its inherited terminal, snapshots native modes/encodings before and after it, reports restoration, and remains usable to read a follow-up command.
- Fixture mode `hold-terminal-open` starts a descendant that retains the slave terminal after the primary exits. Record its PID and explicitly terminate it in test cleanup; primary-child ownership does not imply ownership of this descendant.
- Reuse the fixture's `raw-input` and `interrupt-handler` modes from IH03; use unique markers so screen repaint cannot satisfy an assertion with stale output.

- [x] Add failing tests `Sample_restores_host_after_start_failure`, `Sample_restores_host_after_child_exit`, `Sample_interrupt_reaches_child_not_host`, `Sample_resize_reaches_inner_child`, and `Sample_reports_drain_timeout_for_retained_terminal`. Inspect modes in the supervising fixture on the same terminal; checking a different terminal is insufficient.

```csharp
Assert.True(modesRestored);
Assert.True(supervisorAcceptedFollowupInput);
Assert.Equal(1, sampleExitCode); // retained terminal drain timeout
```

- [x] Add chunk-boundary cases containing UTF-8, arrow-key VT sequences, and terminal-query replies. Assert the raw fixture receives the original bytes in order without requiring Enter; do not assert that the library renders them.
- [x] Exercise natural exit while host input is blocked, failure after partial console setup, cancellation immediately before a read starts, and an output pump fault. Fix only the uncovered sample lifecycle paths. Use a fixture-local fault seam for partial setup if needed; do not add public CLI flags or production environment switches solely for tests.
- [x] Run `InteractiveSampleTests` and all interrupt/shutdown tests on the local frameworks. Confirm bounded cleanup with an outer test timeout and explicit cleanup of descendants even when assertions fail.
- [x] Write manual acceptance commands and expected observations in `samples/README.md`: CMD and Windows PowerShell 5.1 shell editing/history/Tab/Escape, a long-running command interrupted with Ctrl+C, resize during output, exit, then restored editing/echo in the original shell. Include Unix SH commands and an optional locally installed full-screen editor check. External editor checks are recorded separately from automated fixture coverage.
- [x] Commit as `test: verify interactive forwarding and terminal restoration`.

### IH07: package consumers and user documentation

**Files:** modify `packaging/VerifyPackageConsumer.ps1`, `README.md`, `samples/README.md`, and public XML comments in `src/PtyProcess.cs`, `src/PtyStartInfo.cs`, `src/PtyShutdownOptions.cs`, `src/PtyShutdownResult.cs`. Add sample verification switches in `src/Sample/Program.cs` and create `src/Sample/PackageSmokeChecks.cs` for use without a host terminal.

**Interfaces:**
- `--lifecycle-smoke` launches the platform shell through `StartAsync`, begins output draining, calls `ShutdownAsync` with an encoded `exit` request and `ForceTermination=false`, and requires exit plus complete drain.
- `--cancel-start-smoke` uses an already-cancelled token and requires `OperationCanceledException` with no returned process.
- `--interrupt-smoke` starts the same managed program with `--interrupt-child`, waits for its readiness marker, invokes `SendInterruptAsync`, requires acknowledgement, and then requests exit. The child owns its interrupt handler and needs no external test fixture.
- `PackageSmokeChecks` implements `Task<int> RunLifecycleAsync()`, `Task<int> RunCancelledStartAsync()`, `Task<int> RunInterruptAsync()`, and `Task<int> RunInterruptChildAsync()`; each mode has a 30-second outer verification deadline, deterministic cleanup, and returns 0 only after its assertions pass.
- These are documented verification commands, not hidden runtime hooks. `--smoke` remains compatible. All smoke modes are valid with redirected host input/output.

- [x] Add the three new smoke modes and managed verification-child dispatch; keep their implementation in `PackageSmokeChecks.cs`. Resolve the current program's launch form correctly for both `dotnet Consumer.dll` and an apphost executable. Start output draining before awaiting any acknowledgement or exit.
- [x] Extend the fresh-package consumer verifier from IH05 to run all four smoke modes for net8.0/net9.0/net10.0 and after net10.0 publication. Continue compiling C# 13/AnyCPU from the freshly packed local package in an isolated cache. Verify the complete sample source and managed helper assets accompany the consumer.
- [x] Update README examples for async start, interrupt, graceful request, timeout/force result, and output draining. Document single-writer coordination, partial-write cancellation, primary-child ownership, Unix timeout meaning, and interactive versus line sample behavior. Avoid implying that cancelled startup has a hard native deadline or that a Ctrl+C request proves termination.
- [x] Run `dotnet build Icod.Pty.sln -c Release`, `dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts`, then `powershell -NoProfile -File packaging/VerifyPackageConsumer.ps1 -ArtifactDirectory artifacts` on Windows or the equivalent `pwsh -NoProfile -File ...` on Unix. Expect one package and every consumer/publish check to pass. Actual PowerShell 5.1 execution remains mandatory on Windows x64 CI.
- [x] Commit as `docs: verify and document interactive PTY package usage`.

### IH08: integrated validation and handoff

**Files:** update `ROADMAP.md`, this plan's evidence record, and sample acceptance notes as evidence arrives. Change `.github/workflows/pull-request.yaml`, `main.yaml`, `distribution-validation.yaml`, or `release.yaml` only if a concrete verification gap requires it; retain the six-platform matrix and one-package release model.

**Interfaces:** consumes the completed API, sample, fixtures, and package verifier from IH01-IH07. Produces reviewable evidence; it does not authorize merge or publication.

- [x] Run the complete Release build/test locally for all three target frameworks. Require zero build warnings/errors and no regression in the existing foundation tests.
- [x] Push the implementation and inspect PR checks for Windows x64 (`windows-latest`), Windows ARM64 (`windows-11-arm`), Linux x64 (`ubuntu-latest`), Linux ARM64 (`ubuntu-24.04-arm`), macOS x64 (`macos-26-intel`), and macOS ARM64 (`macos-latest`). Record run URL, head SHA, frameworks, and any genuine skip with its reason. Platform-specific fixture branches must not hide a missing implementation of common behavior.
- [x] Verify package contents and fresh/published consumers, including actual Windows PowerShell 5.1 tooling. Preserve AnyCPU, helper deployment, and library-only package output.
- [ ] Have the user run the documented Windows laptop acceptance on build 10.0.26200.9457 or later; record exact build, shell, runtime, and outcomes. CI evidence does not substitute for this check.
- [x] Review resource ownership, cancellation boundaries, stream exclusivity, mode restoration, disposal races, and unsupported-platform behavior against the design. Resolve actionable findings and rerun only affected gates plus required CI.
- [x] Mark each verified tranche complete, update the main roadmap, and report readiness for user review. Leave merging, version/tag selection, and publication to a separate instruction.

## Requirement coverage

| Selected requirement | Tranches | Acceptance evidence |
| --- | --- | --- |
| Fully interactive sample | IH05-IH06 | Single-key input, VT/UTF-8 preservation, resize, no extra echo, restored host settings. |
| Terminal interrupt operation | IH03, IH06-IH07 | Exactly one ETX byte, processed/raw child behaviors, live hosting process, package consumer. |
| Controlled shutdown | IH04, IH06-IH07 | Four result states, request backpressure deadline, cancellation, force policy, final output. |
| Cancellable asynchronous startup | IH01-IH02, IH07 | Ownership race tests, no precancelled spawn, Unix handshake cleanup, package consumer. |
| Expanded interactive verification | IH06-IH08 | Three frameworks, six platforms, PowerShell 5.1, nested PTY checks, laptop acceptance. |

## Evidence record

Implementation baseline: main commit `2613ba527955d341fe04d81317d633ce28df58fb`, following merged PR #1. The user approved implementation on 2026-10-04. PR #2 implements IH01-IH07. IH08 automated verification and review are complete; user laptop acceptance remains pending.

Local Linux x64 verification uses SDK 10.0.401 and runtimes 8.0.31, 9.0.20, and 10.0.12. The sandbox denies the IPC sockets required by the normal test runner, so compilation uses single-process MSBuild and tests use the official xUnit front controller in-process. Hosted CI uses standard `dotnet test`.

| Tranche | Implementation/evidence |
| --- | --- |
| IH01 | `39050e4`: async startup ownership; 26 tests passed per framework. |
| IH02 | `10a7ac3`: cancellable Unix handshake; 29 tests passed per framework. |
| IH03 | `4b007bd`: ETX input; 35 tests passed per framework. Windows processed-read fixture corrected in `f1c857e`. |
| IH04 | `744dede`: shutdown policy; 52 tests passed per framework, including backpressure and final-output coverage. |
| IH05 | `d45fe4f`: interactive sample; 59 tests passed per framework. |
| IH06 | `c38c1ea`: same-terminal restoration/failure probes, inherited-descriptor fix, bounded macOS output read-ahead. |
| IH07 | `d50ee92`: four package smoke modes and user documentation; 72 tests passed per framework before the additional stalled-drain case (73 passed on net10.0). |
| IH08 | `a18d23c`: final implementation verification below; fresh review's blocked-output finding fixed in `a48fed7`. Windows laptop interactive acceptance pending. |

Final implementation head: **`a18d23c946f015af0e7b32e3de949442a796db84`**. [Six-platform CI run 22](https://github.com/uniblab/Icod.Pty/actions/runs/37192831712) completed successfully on 2026-10-04. All three frameworks run in each job. Subsequent evidence-only documentation changes do not change this tested source tree.

| Platform | Runner | Result per framework | Package/consumer/publish |
| --- | --- | --- | --- |
| Windows x64 | `windows-latest` | 76 passed, 1 skipped theory, 0 failed | Passed, using actual Windows PowerShell 5.1 |
| Windows ARM64 | `windows-11-arm` | 76 passed, 1 skipped theory, 0 failed | Passed |
| Linux x64 | `ubuntu-latest` | 78 passed, 0 skipped, 0 failed | Passed |
| Linux ARM64 | `ubuntu-24.04-arm` | 78 passed, 0 skipped, 0 failed | Passed |
| macOS x64 | `macos-26-intel` | 78 passed, 0 skipped, 0 failed | Passed |
| macOS ARM64 | `macos-latest` | 78 passed, 0 skipped, 0 failed | Passed |

The Windows skip is `Native_terminal_preserves_split_query_reply`, explicitly tracking the native limitation below. xUnit represents the skipped theory as one case; on Unix its direct and nested data rows execute separately. Windows still requires split Unicode/arrow input, complete-query native delivery, and the actual sample pump's arbitrary-chunk byte preservation. Local Linux x64 Release verification passed **78/78 on each framework**, with zero warnings/errors. Hosted Staging builds and Release package-consumer builds also reported zero warnings/errors.

Packing produces one library package plus symbols. A fresh local consumer restored the Release `Icod.Pty.0.1.0-alpha.1.nupkg` from the artifact directory (SDK reference/apphost packages came from the existing local SDK cache), compiled with C# 13/AnyCPU, and passed all four smoke modes on every target framework and after net10.0 publication. All three managed helper publish assets were present. Every final hosted job passed the complete package-artifact and PowerShell consumer verifier, including all four modes on each target framework and published net10.0 output; Windows x64 executed it with actual Windows PowerShell 5.1.

Implementation adjustments supported by regression evidence:

- A native Windows console read can complete without command input on Ctrl+C. The interrupt fixture remains alive until its explicit quit command.
- macOS needs bounded output read-ahead before terminal close; otherwise fast-child final output is discarded or an exit wait blocks until a reader arrives. The queue holds 16 blocks of 4 KiB, plus the active reader/writer blocks, preserving backpressure. The delayed-reader test failed on both architectures before this change and passed afterward.
- The sample's duplicated Unix input descriptor is close-on-exec; the retained-descendant fixture exposed the prior inheritance leak.
- Native descendant lifetime differs: Linux can retain an open terminal, whereas macOS revoke/Windows ConPTY teardown can produce native EOF. The real descendant fixture checks that behavior, and a controlled stalled output tests the drain deadline and restoration on every platform. Descendants are explicitly cleaned up.
- Restoration compares configurable termios fields, excluding ABI padding and Darwin's kernel `PENDIN` retype state.
- Windows host reads use an independently opened `CONIN$` handle. Cancelling a read on the borrowed standard-input handle restored modes but affected the next reader; the same-terminal follow-up probe exposed that state leak.
- Native host-output writes must be cancellable. The final review reproduced a sample that never restored its terminal after output backpressure exceeded the drain deadline. `Native_host_output_backpressure_does_not_prevent_cleanup` failed before the fix and passed afterward. Unix output reopens the terminal with independent nonblocking flags; Windows uses an owned output handle and dedicated cancellable writer. Error reporting to a blocked terminal also has a short deadline.
- The split UTF-8/VT fixture acknowledges the entire exact byte sequence once and records received bytes in a separate file. That trace proved an independent [native ConPTY fragmented-query limitation](ConPTY-Input-Limitations.md); changing acknowledgements or native read-buffer size did not fix it. Real native tests and the sample's controlled byte-forwarding test now have separate assertions.

Platform-specific coverage is intentional: Unix helper/handshake tests return early on Windows, where that helper does not exist. The real native-output-backpressure regression runs on Linux because ConPTY rendering and macOS read-ahead change that reproduction's buffering assumptions. Every platform also runs the controlled stalled-output deadline/restoration probe, real byte forwarding, and native terminal-lifetime checks. xUnit's reported counts include these platform branches. The native fragmented-query theory is explicitly skipped on Windows, with an opt-in reproducer; complete-query native delivery and arbitrary-chunk sample forwarding remain mandatory there. This is an observed native limitation, not a claim of passing Windows fragmented-query delivery.

The fresh whole-branch review found no Critical issue and one Important blocked-output cleanup issue, fixed with the failing regression above and a complete green suite. Its Minor stale-evidence note had already been corrected by the planned evidence update; no minor findings remain deferred.

Execution and review decisions:

| Decision | Reason | Cost or limitation |
| --- | --- | --- |
| Keep IH-numbered plan headings and extract task briefs with SH. | Preserve the approved roadmap's identifiers. | Generic skill scripts need that adaptation; product behavior is unaffected. |
| Add bounded macOS output read-ahead. | Retaining the slave alone still blocked exit when Darwin waited for a reader. | A pending worker and bounded buffer per session; scalability remains deferred. |
| Test retained-descendant lifetime by native platform, plus a controlled stalled drain everywhere. | Linux can retain the terminal; macOS/Windows can produce native EOF at primary exit. | Lifetime assertions depend on backend semantics. |
| Keep primary-child ownership. | Descendant/group ownership was explicitly deferred. | Callers must separately manage descendants. |
| Respect native EOF with retained descendants on macOS/Windows. | Native teardown is authoritative. | Detached terminal lifetime remains platform-specific. |
| Retain the macOS prefetch worker after final review. | Regression evidence establishes the final-output requirement. | Worker and memory costs await later scalability work. |
| Keep explicit line mode as a blocking-input demonstration. | Full host lifecycle guarantees belong to interactive mode. | Line mode is unsuitable for reusable in-process hosting. |
| Resolve restoration CI failures before handoff. | They are validation failures, not dismissed review findings. | Additional hosted verification runs. |
| Leave laptop acceptance to the user. | Hosted fixtures cannot establish physical-terminal behavior. | Manual acceptance remains a completion gate. |
| Keep transparent forwarding and expose the native ConPTY fragmented-query limitation. | Direct/nested tests and two native read-buffer sizes reproduce the same loss below the sample. Adding a parser, synthetic input, or a native replacement contradicts the selected transport design. | Windows cannot promise arbitrary fragmented query-reply delivery; its native split-query reproducer is an explicit opt-in test. |

The [sample acceptance guide](../samples/README.md) records the remaining manual commands. The earlier laptop smoke/CMD/PowerShell reports are foundation evidence; they are not claimed as acceptance of the new immediate-input host.
