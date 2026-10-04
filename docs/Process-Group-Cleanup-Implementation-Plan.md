# Process-Group Control and Descendant Cleanup Implementation Plan

> **For agentic workers:** use `superpowers:executing-plans` to implement this plan task by task.
> Native execution was approved on 2026-10-04. Steps use checkboxes; a checked box requires recorded evidence.

**Goal:** Add opt-in process-scope ownership and explicit cleanup beyond the primary child with truthful platform limits.

**Architecture:** Preserve the existing primary-only path. Add a Windows job owner and a Unix initial-group owner
with a proven identity lifetime, then expose shared request/results and integrate explicit shutdown targeting.
Keep process exit, native request success, and output completion separate.

**Tech Stack:** C# 13, .NET 8/9/10, AnyCPU, Windows ConPTY/jobs, Unix PTYs/native spawn and wait interop,
the managed Unix helper, xUnit, CMD/SH/PowerShell 5.1-compatible tooling.

**Spec:** [Process-group cleanup design](Process-Group-Cleanup-Design.md).
Also read the [main roadmap](../ROADMAP.md). The user selected the feature set on 2026-10-04;
the design and implementation sequence were approved on 2026-10-04. Implementation and independent code review are complete. Native ownership/control acceptance passed all
six platforms before the final backpressure fix; the final six-platform/package gate is being rerun.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.

No version bump or package publication is part of this implementation. Do not add a permanent broker, native build,
general process-tree sweep, or foreground-job API as an implementation shortcut.

## Review focus

1. A primary exits before its descendants: scope control must retain a valid identity and preserve primary exit
   reporting. PG01/PG04/PG07 prove this, including release of the retained Unix child record.
2. The host has its own child reaper or Windows job: ownership must not silently weaken or disturb unrelated
   processes. PG01/PG03/PG04 cover coexistence and fail-closed behavior.
3. Startup cancellation arrives between native creation and ownership transfer: no resumed unowned application,
   orphan suspended process, or leaked Unix anchor. PG03/PG04/PG07 cover rollback.
4. A shell changes groups, a child detaches, or group delivery is partial: no assertion of universal cleanup.
   PG05/PG07 distinguish scope, permission errors, request status, and observed exits.
5. Cleanup races with output draining and disposal: no double release, global-lock wait deadlock, or invented EOF.
   PG06/PG07 preserve drain behavior and exercise injected failures.

## Sequence and evidence

| Tranche | Deliverable | Depends on | State |
| --- | --- | --- | --- |
| PG01 | Native identity and launch feasibility gate | Reviewed design/plan | Implemented; evidence below |
| PG02 | Public contracts, snapshots, and backend boundary | PG01 accepted evidence | Implemented; evidence below |
| PG03 | Windows job ownership before application execution | PG02 | Implemented; evidence below |
| PG04 | Unix initial-group ownership and retained identity | PG01-PG02 | Implemented; evidence below |
| PG05 | Scoped control and native Unix signals | PG03-PG04 | Implemented; evidence below |
| PG06 | Shutdown targeting and deterministic disposal | PG05 | Implemented; evidence below |
| PG07 | Adversarial lifecycle integration | PG03-PG06 | Implemented; evidence below |
| PG08 | Samples, XML documentation, and package consumers | PG07 | Implemented; evidence below |
| PG09 | Six-platform acceptance and completion review | PG01-PG08 | In progress |

Execute sequentially; each tranche ends with focused verification and a commit.
For new behavior: write the named failing tests, verify the expected behavioral failure, implement, then rerun.
A restore/toolchain failure is not a useful red test. Do not create artificial tests for prose-only changes.
Record actual test commands, framework/platform, commit SHA, and CI URLs here as work completes.

## File and responsibility map

| Path | Responsibility |
| --- | --- |
| `src/PtyProcessOwnership.cs`, `src/PtyProcessTarget.cs`, `src/PtySignal.cs` | Public policy and target values |
| `src/PtyProcessCapabilities.cs`, `src/PtyControlResult.cs` | Supported operations and request outcomes |
| `src/PtyStartInfo.cs`, `src/LaunchConfiguration.cs` | Synchronous ownership snapshot/validation |
| `src/PtyProcess.cs`, `src/IPtyBackend.cs` | Public dispatch and internal control boundary |
| `src/Windows/WindowsJob.cs`, `src/Windows/WindowsNative.cs` | Job handle lifetime and native declarations |
| `src/Windows/WindowsBackend.cs` | Suspended creation, assignment, resume, rollback |
| `src/Unix/UnixChildLifetime.cs`, `src/Unix/UnixSpawn.cs`, `src/Unix/DarwinProcessGroup.cs` | Opted-in child launch/wait identity and group targeting |
| `src/Unix/UnixNative.cs`, `src/Unix/UnixBackend.cs`, `src/Host/Program.cs` | OS-specific interop, backend and managed helper handshake |
| `src/PtyShutdownOptions.cs`, `src/PtyShutdownResult.cs`, `src/ShutdownCoordinator.cs` | Targeted escalation preserving primary-exit semantics |
| `src/Tests/Icod.Pty.Tests/*Tests.cs`, `ControlledBackend.cs` | Contract, fault, and integration coverage |
| `src/Tests/Icod.Pty.TestChild/ProcessScopeFixture.cs`, `Program.cs` | C# descendant/control fixture and command dispatch |
| `src/Sample/ProcessScopeSmokeChecks.cs`, `Program.cs` | Repeatable packaged ownership example/check |
| `README.md`, `samples/README.md`, `packaging/VerifyPackageConsumer.ps1` | Consumer guidance and package verification |

Create only the files actually needed for these responsibilities. Keep test/sample/helper sources excluded
from the public DLL through the existing project globs. Avoid unrelated backend/I/O restructuring.

### PG01: native identity and launch feasibility gate

**Files:** add `src/Tests/Icod.Pty.Tests/ProcessScopeFeasibilityTests.cs`,
`src/Tests/Icod.Pty.TestChild/ProcessScopeFixture.cs`; modify fixture `Program.cs`.
Record results in this plan and any resulting contract correction in the design.

**Interfaces:** fixture modes `scope-parent`, `scope-child`, `scope-detached`, and `scope-reaper-host`.
Use a unique temporary directory per run, readiness records with PID/group/session identifiers, and a
control channel independent of terminal rendering. Fixture signals and native probes remain C#.
These are internal experiments; no public API is committed by their existence.

- [ ] Build a parent/child fixture that can keep a descendant alive after primary exit; verify the observer
  distinguishes primary exit, descendant exit, and terminal EOF. Each fixture has bounded external cleanup.
- [ ] Validate Windows suspended creation followed by job assignment/resume, nested-job hosting, forced
  assignment/resume failure, and child creation immediately on resume. Assert no child executes before assignment.
- [ ] Validate Unix native spawn of the managed helper, non-reaping exit observation, group targeting after
  leader exit, and exact reaping on disposal. Verify exit-code equivalence with the current Process backend.
- [ ] Verify Linux/Darwin constants, native struct size/alignment, and x64/ARM64 layouts against current
  authoritative SDK/runtime definitions. Keep platform-specific declarations separate where necessary.
- [ ] Test .NET ordinary Process children concurrently with the native child, and an isolated host with an
  intentional competing reaper. Establish whether interference can be detected before unsafe control.
  Document unavoidable host preconditions; do not modify global SIGCHLD behavior.
- [ ] Run `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -f net10.0 --filter FullyQualifiedName~ProcessScopeFeasibilityTests`
  on all six platforms, then repeat the reaper/wait probes on net8.0 and net9.0.
- [ ] Record evidence and the identity-lifetime argument. Passing stress tests alone do not prove identifiers
  cannot be reused. If the approach cannot preserve identity, stop dependent work and present a revised design.
- [ ] Commit the proven fixtures and design findings as `test: establish process scope ownership feasibility`.

### PG02: contracts and capture


**Files:** create the five public contract files in the map and
`src/Tests/Icod.Pty.Tests/PtyOwnershipTests.cs`; modify start info, launch configuration, process/backend
boundary, and `ControlledBackend.cs`.

**Interfaces:** produce all enums, flag values, record signatures, and PtyProcess members from the design.
Extend IPtyBackend with `Ownership`, `Capabilities`, `RequestTermination(PtyProcessTarget)`, and
`SendSignal(PtySignal, PtyProcessTarget)` of matching types. Retain its existing Terminate and Exit members.

- [ ] Add `Ownership_defaults_to_primary`, `Ownership_is_snapshotted_before_yield`,
  `Invalid_ownership_does_not_create_child`, `Capabilities_do_not_claim_liveness`, and
  `Legacy_terminate_remains_primary_only`. Assert exact enum/default values and backend call counts.
- [ ] Run the PtyOwnershipTests filter; expect missing API/behavior failures, then implement capture and
  a controlled-backend seam. PlatformScope creation must fail explicitly until its backend is implemented.
- [ ] Verify all existing Start/StartAsync snapshots and startup-cancellation tests still pass.
- [ ] Compile all frameworks and commit as `feat: define explicit PTY process ownership contracts`.

### PG03: Windows job ownership

**Files:** create WindowsJob and `src/Tests/Icod.Pty.Tests/WindowsOwnershipTests.cs`;
modify WindowsNative/WindowsBackend and the scope fixture.

**Interfaces:** internal `WindowsJob : IDisposable` owns a safe job handle and provides
`void Assign(SafeProcessHandle process)` and `PtyControlResult RequestTermination()`.
The backend exposes it only through the PG02 boundary; never expose a raw job handle publicly.

- [ ] Add `Owned_child_is_assigned_before_resume`, `Job_survives_primary_exit`,
  `Assignment_failure_never_runs_child`, `Resume_failure_collects_child`,
  `Cancellation_before_transfer_cleans_job`, and `Nested_job_failure_is_explicit`.
  Use deterministic per-instance fault injection, never mutable global hooks.
- [ ] Run WindowsOwnershipTests and verify failures; implement non-inheritable job ownership, kill-on-close,
  suspended creation, assignment, checked resume, and rollback. Keep default launch behavior unchanged.
- [ ] Verify a descendant created immediately after resume belongs to the job; after primary exit request
  job termination and independently observe the known descendant stop.
- [ ] Verify errors release thread/process/job/ConPTY/pipe resources and retain useful original failure context.
  Never wait for a task that needs a lock still held by disposal.
- [ ] Run on Windows x64/ARM64, all frameworks; commit as `feat: own Windows PTY descendants with jobs`.

### PG04: Unix group lifetime

**Files:** create UnixChildLifetime, UnixProcessScope, and
`src/Tests/Icod.Pty.Tests/UnixOwnershipTests.cs`; modify UnixBackend/UnixNative and helper only as required.

**Interfaces:** internal `UnixChildLifetime : IDisposable` provides `int ProcessId`, `Task<int> Exit`,
`Task<UnixChildLifetime> StartAsync(LaunchConfiguration launch, CancellationToken token)`,
`PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target)`, and
`PtyControlResult RequestTermination(PtyProcessTarget target)`.
UnixProcessScope encapsulates validated anchored initial-group targeting; lifetime release and requests share
one synchronization policy. Final method bodies follow PG01 evidence; do not substitute Process auto-reaping.

- [ ] Add `Owned_group_survives_primary_exit`, `Primary_exit_is_reported_before_scope_disposal`,
  `Disposed_scope_reaps_anchor_once`, `Cancelled_handshake_reaps_native_child`,
  `Exec_failure_releases_all_pipes`, `Competing_reaper_disables_unsafe_targeting`, and
  `Unrelated_host_processes_are_untouched`.
- [ ] Run UnixOwnershipTests to establish failures; implement the proven native launch/wait path only for
  PlatformScope, keeping existing helper lookup, installed-runtime use, close-on-exec status, and diagnostics.
- [ ] Gate group control on completed session/group setup. During failed startup before that point, clean
  the known child only; never negate an unvalidated identifier.
- [ ] Preserve Unix timeout/caller-cancellation distinction and cleanup-before-failure semantics.
  Primary Exit reports observed status; final reaping occurs once after final group requests.
- [ ] Verify descriptors, retained wait records, and pending tasks are released on every failure and disposal
  path; maintain macOS read-ahead's primary-exit observation without stealing wait ownership.
- [ ] Run on Linux/macOS x64/ARM64, all frameworks; commit as `feat: retain Unix PTY group identity for cleanup`.

### PG05: explicit control dispatch

**Files:** implement public dispatch in PtyProcess; extend both backends and add
`src/Tests/Icod.Pty.Tests/PtyProcessControlTests.cs`.

**Interfaces:** produce `PtyControlResult RequestTermination(PtyProcessTarget target)` and
`PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target)` exactly as designed.
Map named Unix signals using platform definitions; reject arbitrary numeric casts.

- [ ] Add `Owned_scope_requires_launch_opt_in`, `Unix_native_signals_require_anchored_ownership`,
  `Windows_native_signal_is_unsupported`,
  `Unix_interrupt_is_independent_of_terminal_mode`, `Missing_target_is_not_confirmed_scope_exit`,
  `Permission_error_is_not_success`, `Invalid_signal_has_no_side_effect`,
  `No_request_uses_host_or_broadcast_group`, and `Disposed_control_is_rejected`.
- [ ] Run the control filter, implement validation/dispatch/error context, then verify request outcomes and
  exact native targets through controlled seams. A successful group request must not claim every member exited.
- [ ] Add real Unix fixtures for Hangup/Interrupt/Terminate handlers and uncatchable Kill; independently observe
  acknowledgements/exits. Keep SendInterruptAsync's existing byte-level tests unchanged.
- [ ] Test a child that moves to another group/session; report it outside scope and clean it through the
  fixture's separately tracked ownership. Do not expect terminal-close side effects to be identical across OSes.
- [ ] Run all frameworks/platforms and commit as `feat: expose scoped PTY process control`.

### PG06: shutdown and disposal integration

**Files:** modify shutdown options/result/coordinator, PtyProcess, both backends;
extend PtyShutdownTests and add `src/Tests/Icod.Pty.Tests/PtyScopeDisposalTests.cs`.

**Interfaces:** add `PtyShutdownOptions.TerminationTarget` default PrimaryProcess, and nullable init-only
`PtyShutdownResult.TerminationResult`. Preserve the positional constructor/deconstruction.
Pass the captured target through ShutdownCoordinator; primary Exit remains the completion condition.

- [ ] Add `Default_shutdown_stays_primary_only`, `Owned_shutdown_records_target_and_outcome`,
  `Graceful_primary_exit_does_not_imply_scope_cleanup`, `Unsupported_target_rejected_before_request_bytes`,
  `Shutdown_target_is_snapshotted`, and `Old_shutdown_result_constructor_and_deconstruction_work`.
- [ ] Verify failures, then integrate target selection only into forced escalation. Preserve grace/termination
  budgets, single-shutdown guard, retry, cancellation, and no-output-consumption behavior.
- [ ] Add `Owned_dispose_cleans_after_primary_exit`, `Default_dispose_does_not_dispatch_scope_control`,
  `Concurrent_dispose_releases_once`, `Control_failure_still_releases_resources`, and
  `Cancellation_does_not_initiate_escalation`.
- [ ] Implement owned disposal before identity release, attempt all cleanup, preserve primary status, and
  document that callers wanting guaranteed diagnostics should explicitly request control before disposal.
- [ ] Run shutdown/startup/disposal filters and all legacy tests; commit as
  `feat: integrate owned scope cleanup with PTY lifecycle`.

### PG07: adversarial integration and output

**Files:** add `src/Tests/Icod.Pty.Tests/ProcessScopeIntegrationTests.cs`;
extend scope fixture, PtyTestSupport, and relevant buffered-output tests.

**Interfaces:** fixture modes from PG01 remain bounded and independently cleanable.
Tests assert observed fixture identities and exit markers, not PID existence alone.

- [ ] Test same-group descendants, primary-before-descendant exit, immediate descendants, pipelines changing
  groups, a detached session, ignored graceful signals, repeated force requests, and teardown during startup.
- [ ] Use gates/readiness markers for natural-exit versus force/dispose races. Test request failure, cancellation,
  and both output backpressure and a quiet surviving descendant retaining the terminal.
- [ ] Verify a cooperative exit's complete final marker sequence with concurrent draining; do not assert lossless
  output for abrupt kill/disposal. Preserve macOS buffered output and all interactive-host tests.
- [ ] Bound all waits and fixture cleanup. Ensure an intentionally surviving out-of-scope fixture is cleaned
  without signaling reused numeric identifiers or unrelated processes.
- [ ] Run the integration filter on all six platforms/all frameworks. Commit as
  `test: verify scoped cleanup under PTY lifecycle races`.

### PG08: consumer documentation and package verification

**Files:** add sample ProcessScopeSmokeChecks; modify sample Program, README, samples README,
packaging/VerifyPackageConsumer.ps1, and PackageSmokeTests.

**Interfaces:** sample `--scope-smoke` launches managed descendants in the documented scope, requests cleanup,
observes their exit, and disposes. Success output: `PTY process-scope smoke check passed.`

- [ ] Add a failing package/sample test for --scope-smoke, then implement the check using explicit ownership.
  Include primary-before-descendant exit; fixture/helper implementation remains C# under src.
- [ ] Show ownership opt-in, capability checks, native signal versus ETX, scoped force, and separate primary
  exit/output draining. Explain Unix initial-group coverage, retained child-record cost, and disposal requirement.
- [ ] Expand XML docs for each new public member, default, exception, and result field. Compile with XML docs.
- [ ] Extend fresh NuGet and published-consumer verification across all frameworks using existing scripts.
  Run PowerShell checks with actual Windows PowerShell 5.1 where supported by the existing Windows job.
- [ ] Add CMD/PowerShell 5.1 laptop instructions for scoped cleanup, normal exit, and host-console restoration.
  Keep manual acceptance unchecked until the user reports it.
- [ ] Commit as `docs: demonstrate and verify PTY process scope ownership`.

### PG09: completion and evidence

**Files:** update this plan, design status, and main ROADMAP with actual results only.
Modify workflows only if required for these checks; retain all six platform jobs.

- [ ] Run Release build/test/pack locally on available platforms, then the existing six-platform CI matrix
  for net8.0/net9.0/net10.0. Verify the exact package and fresh/published consumers.
- [ ] Review public API compatibility, ownership lifetime, cancellation, error preservation, absence of C/Python,
  AnyCPU outputs, and docs/sample consistency. Record any unsupported operations explicitly.
- [ ] Record failures and fixes; rerun relevant jobs when needed. Do not mark native behavior proven by controlled
  tests alone, or hide new ownership failures behind skips. Preserve the existing documented ConPTY exclusion.
- [ ] Record Windows laptop results separately when supplied. Leave them pending otherwise.
- [ ] Mark only evidenced tranches complete and report merge readiness. Version selection and NuGet publication
  remain separate user actions. Commit completion evidence as `docs: record process scope cleanup verification`.

## Verification commands

Run from the repository root; forward slashes work with dotnet in CMD, SH, and PowerShell 5.1.

```text
dotnet build Icod.Pty.sln -c Release
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net8.0 --no-build
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net9.0 --no-build
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net10.0 --no-build
dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts
dotnet run --project samples/Icod.Pty.Sample -f net10.0 -- --scope-smoke
```

Existing package commands, from a PowerShell shell (Windows PowerShell 5.1 on the Windows acceptance host):

```powershell
./packaging/VerifyPackageArtifact.ps1 -ArtifactDirectory artifacts -Configuration Release
./packaging/VerifyPackageConsumer.ps1 -ArtifactDirectory artifacts
```

Expected final result: no new failures, all supported ownership cases pass on six platforms/three frameworks,
and package consumers reproduce the documented scope behavior.
Final completion requires both native CI evidence and whole-branch review.

## Implementation evidence

- Native feasibility: [run 37199198321](https://github.com/uniblab/Icod.Pty/actions/runs/37199198321), all six platforms and all three frameworks passed, including package consumers.
- PG02: ownership contract tests 5/5 on each framework locally; Linux net10 regression suite 91/91.
- PG03: [run 37200436474](https://github.com/uniblab/Icod.Pty/actions/runs/37200436474) exposed double termination during cancellation and a nested-job test setup error on Windows; fixes are under verification. The four Unix jobs passed.
- PG04: initial Linux net10 ownership tests 2/2 passed, covering primary status/final output, post-primary group signaling, descendant acknowledgement, and exactly-once reap. Broader acceptance remains pending.

Owned Linux launch requires glibc 2.34 or newer for atomic close-from file actions. macOS uses
POSIX_SPAWN_CLOEXEC_DEFAULT. Startup rollback stops the anchored primary, observes its exit without reaping,
then requests group cleanup before releasing the anchor; this prevents group creation racing rollback.

### Lifecycle and consumer checkpoint

- [Run 37201220251](https://github.com/uniblab/Icod.Pty/actions/runs/37201220251), head `06e290bb30d2798c1e6b9d4ab7e00aad8316b684`: all six jobs passed all three frameworks plus package verification. Windows restriction/cancellation and Darwin zombie-only group fixes passed.
- New integration: known descendant exit after primary exit via explicit force or concurrent disposal; detached session, changed group and real job-control pipeline outside scope; native interrupt in raw mode; ignored graceful termination followed by force; unrelated session remains usable. Linux net10 local integration passed.
- Added owned-mode large final output/backpressure checks, production SIGCHLD host guards in isolated fixture processes, exec-failure rollback, permission injection, lost wait ownership, and exactly-once cleanup checks.
- Local Release build: all frameworks, zero warnings/errors. Full Linux net10 suite: 131/131 before the final startup dual-error/deferred-reap additions.
- Scope package smoke RED: unsupported switch; GREEN: all five sample smoke modes passed locally. Fresh/published consumers now include `--scope-smoke`; current-head native package acceptance is pending.
- Local test transport uses the existing xUnit runner because this environment blocks standard test-host sockets. CI continues to use standard `dotnet test`.
- Final review and Windows laptop acceptance remain pending; only the latter requires user observations.

### Final review and acceptance fixes

The independent whole-branch review found two important primary-control issues: a void termination call could
be reported as native acceptance after an exit race, and default Unix native failures escaped as Win32Exception.
Result-aware Windows dispatch and a documented DispatchUnconfirmed outcome for the default Unix managed
path address the first; IOException normalization with native context addresses the second. Deterministic
race, native-success, denial, and managed-uncertainty tests cover these boundaries. No minor findings were raised.

[Run 37202208981](https://github.com/uniblab/Icod.Pty/actions/runs/37202208981), head `c405d0c184040cad59da050dd6fa660acbeb4823`,
passed Windows/Linux x64/ARM64 including the new scope package consumers. Both macOS jobs exposed a
backpressured owned-disposal timeout: native exit could wait for terminal drain before the owner released
terminal resources. The fix releases streams before waiting while preserving the identity anchor through
final group control/reap. The existing owned-backpressure regression remains enabled; native rerun is required.

### Final verification checkpoint (2026-10-04)

- Source head `ddcf5b503690adb16c0ce92879e0129c43fadcd7`: Release builds zero warnings/errors; Linux local full suite **137/137 on each of net8.0, net9.0, and net10.0**.
- Local fresh package-only consumers passed `--scope-smoke` on all three frameworks and published net10.0 output. SDK reference packs came from a separate local feed; the Icod.Pty package came exclusively from the freshly packed artifact.
- [Run 37202755507](https://github.com/uniblab/Icod.Pty/actions/runs/37202755507) passed Windows x64/ARM64, Linux x64/ARM64, and macOS ARM64, including package consumers and Windows PowerShell 5.1 where applicable. macOS ARM64 confirmed the backpressure regression fix.
- macOS x64 remained in SDK installation for more than twelve minutes without reaching build/tests. This documentation checkpoint starts a fresh CI attempt; no C# or package-tooling changes were made after the source head above. The [PR checks](https://github.com/uniblab/Icod.Pty/pull/3/checks) are the current acceptance gate.
- Independent whole-branch review reported two important findings, both fixed in the single review-fix pass with deterministic coverage and the 137/137 suites. No critical or minor findings remain.
- Windows laptop acceptance remains pending. Version 0.1.0-alpha.1 is unchanged; no merge, tag, or publication was performed.

### Recorded implementation decisions

| Decision | Reason and practical cost |
| --- | --- |
| Local xUnit runner/single-node builds; normal dotnet test in CI | Local test-host sockets are restricted. Local transport differs, test bodies do not. |
| Exclusive Unix wait ownership; reject PID1, SIG_IGN, SA_NOCLDWAIT | An external reaper cannot be synchronized by a private lock; violating this condition can defeat identity safety. |
| Keep PG tranche names and maintain execution ledger manually | The execution helper expects Task headings; no runtime effect. |
| Add public control dispatch in PG05, contracts in PG02 | Avoid public placeholder implementations; intermediate commits are not release candidates. |
| Stop/observe primary before final rollback group request/reap | Prevent a helper creating its group after an early failed lookup; correctness depends on retained exclusive identity. |
| Linux glibc2.34+ close-from; Darwin CLOEXEC_DEFAULT | Prevent unrelated descriptor inheritance; older libc cannot use PlatformScope. |
| Separate UnixSpawn from UnixChildLifetime | Launch/pipes and wait/control lifetime have separate responsibilities; differs from the provisional filename map. |
| Inspect anchored Darwin group only after EPERM | Distinguish zombie-only groups conservatively; inventory failures still report native errors. Enumerated PIDs never become control targets. |
| Deferred Unix reap after denied primary termination | Avoid indefinite disposal blocking; the retained observer may live until natural exit, with no cleanup guarantee for inaccessible descendants. |
| Preserve startup plus cleanup errors in AggregateException | Prevent rollback failure hiding cancellation/native error; aggregate is exceptional rollback-failure behavior. |
| DispatchUnconfirmed for default Unix primary requests | Process.Kill cannot prove native acceptance; consumers of the new result must handle a third status. |
| Keep escaped groups/external-service launches outside scope | Platform ownership cannot promise universal containment; such processes may survive. |
| Require current native CI evidence and retain laptop acceptance separately | Prior green code is insufficient for final native changes; unchecked acceptance must not be inferred from local or other-architecture tests. |
