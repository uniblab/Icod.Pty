# Session Orchestration Implementation Plan

> **Acceptance:** implementation and review are complete on the feature branch. Six-platform CI evidence is
> recorded below; Windows laptop observations remain a separate manual acceptance item.

**Goal:** Add an optional session owner coordinating PTY I/O, shutdown, output draining, cleanup, and focused diagnostics.

**Architecture:** Keep PtyProcess and its native backends as the process/terminal layer. Add a session facade,
a serialized writer, pump/lifetime coordinator, and bounded diagnostic journal. Console ownership remains
with the sample; process-scope ownership remains the policy selected at launch.

**Tech Stack:** C# 13, .NET 8/9/10, AnyCPU, Stream/Task/ValueTask, existing PTY backends, xUnit,
CMD/SH/PowerShell 5.1-compatible tooling.

**Spec:** [Session orchestration design](Session-Orchestration-Design.md).
**Baseline:** merged process-scope milestone, PR #3, main commit `bfe754f930acabfb2a7c9f1a0c42965c410a1c1f`.
The established implementation has 138 test cases per framework; the exact new total is evidence, not a target.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.
- Preserve existing PtyProcess, shutdown, ownership, native signal, and package contracts.
- No third-party runtime dependency, version bump, merge, tag, or publication in this milestone.

## Review focus

1. A supplied stream ignores cancellation: do not announce completion while a pump still uses it; SS03/SS05 test deferred unblocking.
2. Shutdown races a partial write, source EOF, or primary exit: no byte interleaving, escalation on caller cancellation, or erased native result; SS02/SS04 pin these cases.
3. The primary exits while a descendant holds output open: distinguish primary exit, drain expiry, and scoped disposal; SS05/SS07 independently observe the descendant.
4. Output/flush, process observation, and cleanup fail together: retain every independently observed failure and perform cleanup once; SS05 covers the precedence and aggregation.
5. A consumer invokes control or disposes during startup/completion: preserve stream ownership transfer, prevent double teardown, and restore the host only after pumps settle; SS01/SS05/SS07 cover these races.

## Tranches and acceptance

| Tranche | Deliverable | Dependency | State |
| --- | --- | --- | --- |
| SS01 | Public contracts, startup capture, ownership handoff | Approved design/plan | Complete |
| SS02 | Serialized input and input sealing | SS01 | Complete |
| SS03 | Stream forwarding and independent output completion | SS01-SS02 | Complete |
| SS04 | Shutdown integration and cancellation boundaries | SS02-SS03 | Complete |
| SS05 | Finalization, drain deadlines, and truthful results | SS03-SS04 | Complete |
| SS06 | Bounded lifecycle diagnostics | SS01-SS05 | Complete |
| SS07 | Native integration and interactive sample adoption | SS01-SS06 | Complete |
| SS08 | Packaged examples and consumer documentation | SS07 | Complete |
| SS09 | Six-platform acceptance and completion review | SS01-SS08 | Complete |

Execute sequentially. Each implementation tranche has a failing test, a focused green check, the existing
regression suite, and a commit. No behavior is accepted from controlled tests alone where native behavior matters.
Commit subjects below identify deliverables; a tranche may require more than one commit while resolving evidence.

## File and responsibility map

| Path | Responsibility |
| --- | --- |
| `src/PtySession.cs` | Public facade, startup capture, coordinated operation entry points |
| `src/PtySessionOptions.cs`, `src/Session/SessionConfiguration.cs` | Validated immutable snapshot and caller-stream ownership |
| `src/PtySessionResult.cs` | End reason, output status, failures, and final result types |
| `src/PtySessionDiagnostics.cs` | Phase/event enums and detached public diagnostic records |
| `src/Session/SessionWriter.cs` | One writer gate, seal/stop cancellation, no interleaved writes |
| `src/Session/SessionPumps.cs` | One input/output loop, bounded buffers, flush/EOF observation |
| `src/Session/SessionCoordinator.cs` | Terminal triggers, draining, cleanup, shared completion |
| `src/Session/SessionJournal.cs` | Last 32 events, counters, coherent immutable snapshots |
| `src/PtyProcess.cs`, `src/ShutdownCoordinator.cs` | Narrow internal captured-start and request-writer hooks |
| `src/Tests/Icod.Pty.Tests/SessionTestSupport.cs` | Gated/cancellable streams and session factory for controlled backend |
| `src/Tests/Icod.Pty.Tests/PtySession*Tests.cs` | Public contracts, ordering, pumps, shutdown, finalization, diagnostics, native tests |
| `src/Tests/Icod.Pty.TestChild/Program.cs`, `ProcessScopeFixture.cs` | Managed native fixtures; extend only when existing modes do not cover a case |
| `src/Sample/InteractiveSession.cs`, `SessionSmokeChecks.cs`, `Program.cs` | Console adapter integration and packaged examples |
| `README.md`, `samples/README.md`, `packaging/VerifyPackageConsumer.ps1` | Consumer guidance and artifact verification |

Public types/signatures/defaults are exactly those in the spec. Do not expose IPtyBackend, the owned process,
or raw PTY streams through PtySession. No platform backend redesign is required by this plan.

## Verification convention

For every `PtySessionXTests` filter below, run from the root:

```text
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net10.0 --filter FullyQualifiedName~PtySessionXTests
```

Replace X with the specified test class suffix. RED means the named assertion fails because the required
contract is absent; initially missing-type compilation failures establish absent public contracts only.
Restore/SDK failures are infrastructure failures, not behavioral RED evidence. After implementation expect
zero failed cases. Before committing each tranche, run the complete suite on available platforms/frameworks;
use normal dotnet test in CI. If local test transport is restricted, record the alternate runner and its limits.

### SS01: contracts and startup ownership

**Files:** create PtySession, options/result/diagnostic public files, SessionConfiguration, SessionCoordinator
skeleton, SessionTestSupport, and `PtySessionStartTests.cs`; modify PtyProcess only for captured startup.

**Interfaces:** produce the spec's complete public records/enums/options. Add internal
`PtyProcess.StartCapturedAsync(LaunchConfiguration, CancellationToken)` delegating to the existing native
factory. Add `PtySession.StartCoreAsync(LaunchConfiguration, SessionConfiguration, CancellationToken,
Func<LaunchConfiguration, CancellationToken, Task<PtyProcess>>)` for production startup and controlled tests.
SessionConfiguration captures Output/Input, both leave-open flags, and DrainTimeout. No public adoption API.

- [x] Write `PtySessionStartTests`: options mutated after StartAsync do not change captured streams/5-second drain default; invalid streams, same-stream input/output, zero/infinite timeout create zero children; default ownership is PrimaryProcess; pre-cancelled startup creates zero children.
- [x] Run that filter and verify RED for missing capture/validation behavior.
- [x] Implement synchronous capture and validated ownership transfer. Add startup cases for cancellation after child creation and failure during coordinator setup: child disposed once, supplied streams left open even with both leave-open flags false, original and rollback errors retained.
- [x] Run the startup filter and existing PtyStartupTests/PtyOwnershipTests: expect all green on each TFM; compile XML documentation with no new warnings.
- [x] Commit `feat: define owned PTY session startup and result contracts`.

### SS02: serialized input and sealing

**Files:** create SessionWriter and `PtySessionInputTests.cs`; wire facade methods and test streams.
**Interfaces:** `SessionWriter(Stream input)`; `ValueTask WriteAsync(ReadOnlyMemory<byte>, CancellationToken)`;
`ValueTask WriteShutdownAsync(ReadOnlyMemory<byte>, CancellationToken)`; `void Seal()` cancels normal writes;
`void Stop()` also cancels shutdown writes. Internal task observation is owned by the coordinator.
WriteAsync and SendInterruptAsync on PtySession use the normal path; native signals use the process directly.

- [x] Write a gated first write test: await first admission, enqueue second, release first, assert exact bytes `ABC` then `DEF` and no overlapping native writes. ETX is one byte `0x03` between explicitly sequenced requests. Do not assert scheduler FIFO between unordered concurrent calls.
- [x] Add tests: cancel before admission produces zero bytes; cancel after a controlled two-byte prefix reports cancellation without retrying that prefix; sealing rejects future writes and cancels pending normal writes; private shutdown writes still work; stopping rejects/cancels both paths.
- [x] Run `PtySessionInputTests` for behavioral RED, then implement the writer gate and linked cancellation without copying pending caller buffers or splitting one admitted write across other writers.
- [x] Run the filter plus legacy PtyInterruptTests and full regression suite; expect unchanged ETX and cancellation semantics.
- [x] Commit `feat: serialize session input and seal it for shutdown`.

### SS03: forwarding and independent EOF

**Files:** create SessionPumps and `PtySessionPumpTests.cs`; wire coordinator pump tasks and OutputCompletion.
**Interfaces:** coordinator owns `Task` for optional input forwarding and
`Task<PtySessionOutputStatus>` for output forwarding. OutputCompletion exposes the latter terminal outcome;
Completion remains distinct. Pumps consume SessionConfiguration and SessionWriter, use 16 KiB buffers,
and report faults/EOF to the coordinator through internal methods, never consumer callbacks.

- [x] Write tests: source EOF stops forwarding but leaves explicit writes/primary alive; output EOF and successful flush complete OutputCompletion but leave Completion pending until primary exit; output bytes including VT/NUL/multibyte fragments are forwarded unchanged.
- [x] Write backpressure/ownership tests: gated output allows at most one 16 KiB chunk ahead in this pump; flush failure is an Output failure; cancellation causes no orphan read/write. An intentionally noncooperative stream keeps the pump pending until a test gate releases it, with all fixture cleanup in finally.
- [x] Run `PtySessionPumpTests` for RED; implement the loops, fault observation, cancellation recognition, and EOF/flush handling. Neither EOF implicitly sends an exit request.
- [x] Run the pump filter and BufferedOutputTests on all TFMs available; expect byte equality, bounded buffering, no abandoned task, and no early session-completion claim.
- [x] Commit `feat: coordinate session stream forwarding and EOF`.

### SS04: application-directed shutdown

**Files:** modify ShutdownCoordinator/PtyProcess and session coordinator/facade; add `PtySessionShutdownTests.cs`.
**Interfaces:** preserve public PtyProcess.ShutdownAsync. Add internal
`Task<PtyShutdownResult> PtyProcess.ShutdownAsync(PtyShutdownOptions options,
Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> writeRequest, CancellationToken cancellationToken)`.
Thread that callback into
ShutdownCoordinator.RunAsync as an optional final parameter, defaulting to backend.Input.WriteAsync.
Session captures/copies options before sealing and supplies SessionWriter.WriteShutdownAsync. Reuse
PtyProcess's existing validation, concurrent-shutdown guard, target dispatch, and result semantics.

- [x] Write tests: invalid/unsupported/pre-cancelled shutdown leaves input unsealed; accepted shutdown seals normal input permanently; a second concurrent shutdown is rejected; sequential retry is allowed after timeout/cancellation and cannot reopen normal input.
- [x] Add a gate-blocked write test: the grace budget includes waiting for the writer, and a queued request cannot wait indefinitely before its deadline begins. Record completed write bytes to prove no normal input follows the shutdown request.
- [x] Add controlled races: cancellation before escalation sends zero force requests; cancellation after dispatch does not erase the request; primary exit during shutdown preserves the returned result; failed/cancelled retry preserves a prior LastShutdownResult. No shutdown method consumes output.
- [x] Run `PtySessionShutdownTests` for RED, implement the narrow hook and session quiescence. Register active shutdown so finalization joins it without holding the writer/lifecycle lock. Preserve existing snapshot/deadline validation and rollback exception behavior.
- [x] Run new filter, PtyShutdownTests and PtyScopeShutdownTests, then full suite: expect existing defaults and positional result compatibility unchanged.
- [x] Commit `feat: integrate session shutdown with ordered input`.

### SS05: draining and finalization

**Files:** complete SessionCoordinator/PtySession; add `PtySessionCompletionTests.cs`.
**Interfaces:** Completion returns exactly one PtySessionResult; DisposeAsync joins that finalizer and throws
AggregateException when Failures is nonempty. All listed metadata remains readable afterward. Use the spec's
first-trigger precedence and stage-tagged failures. Output statuses are never inferred from primary exit.

- [x] Write tests: primary exits 37, final output arrives later, Completion waits for bytes and flush, then reports PrimaryExited/37/EndOfStream; output never reaches EOF, drain expires, result reports TimedOut and releases resources; early EOF alone cannot finish the session.
- [x] Write tests: explicit disposal interrupts drain and reports Stopped unless output already has a terminal outcome; input/output/process fault triggers cleanup; cleanup errors do not skip other releases; simultaneous faults all appear with their stage and original exception identity.
- [x] Write tests: concurrent/repeated DisposeAsync disposes the process and each owned stream once; leave-open streams stay open; startup failure never transfers stream ownership; noncooperative streams delay Completion until externally unblocked. No caller-owned stream is closed to fake cancellation.
- [x] Add a shutdown-versus-primary-exit barrier proving no use-after-dispose and a complete LastShutdownResult, plus cancellation of Completion.WaitAsync proving zero termination side effects.
- [x] Run `PtySessionCompletionTests` for RED; implement terminal-trigger arbitration, drain cancellation, native teardown before joining blocked PTY I/O as appropriate, task joining, external-stream cleanup, result collection, and synchronous Dispose over the same path.
- [x] Run all new session filters and existing startup/shutdown/backpressure/scope tests. Expected: no deadlock, one finalizer, truthful errors/results, and no lossless-output assertion after abrupt release.
- [x] Commit `feat: finalize sessions with bounded drain requests and complete outcomes`.

### SS06: focused lifecycle diagnostics

**Files:** create SessionJournal and `PtySessionDiagnosticsTests.cs`; wire coordinator/writer/pump event points.
**Interfaces:** `PtySession.GetDiagnostics()` returns the spec's detached snapshot; internal journal records
phase, input seal, byte counters, lifecycle events, sequence, monotonic elapsed time, and eviction count.
No event subscriber API or automatic transcript capture.

- [x] Write tests: controlled lifecycle emits Started through Completed in causal order with increasing sequences/nondecreasing elapsed values; more than 32 events retains only the latest 32 and increments DroppedEvents exactly; earlier snapshots do not change after new events.
- [x] Add tests: read/write counters count completed operations, not cancelled prefixes; concurrently requested snapshots are coherent; caller cannot mutate returned collections; diagnostics after disposal remain available; sentinel command/environment/input/output strings never occur in event records.
- [x] Run `PtySessionDiagnosticsTests` for RED; implement the bounded journal with short synchronization and no arbitrary callback under a lock. Preserve exceptions in result failures without automatically logging their text.
- [x] Run diagnostics filter and whole suite; expect bounded history and no change in pump/control outcomes.
- [x] Commit `feat: expose bounded session lifecycle diagnostics`.

### SS07: real PTYs and interactive host

**Files:** create `PtySessionIntegrationTests.cs`; modify InteractiveSession and InteractiveSampleTests;
extend existing C# fixture/support only for missing modes.
**Interfaces:** public session API from SS01-SS06. InteractiveSession keeps HostConsole.ReadAsync/Output,
GetSize, cancellation-capable input, 100 ms resize monitoring, and explicit host EOF shutdown policy.
It awaits session completion and host pump settlement before HostConsole disposal restores native modes.

- [x] Write native cases: final marker survives cooperative exit; explicit ETX and request bytes remain ordered; quiet surviving descendant causes drain expiry then owned disposal; independently observe the known descendant exit. Default ownership does not acquire a scope implicitly.
- [x] Add native teardown/backpressure and primary-exit races on both ownership policies. Keep fixture cleanup bounded and independently tracked; do not signal a cached unrelated PID to clean up a failed test.
- [x] Add/adapt interactive tests for immediate keys, resize, Ctrl+C, input/output EOF behavior, launch failure, and restoration after pump failure. Keep --line behavior and the existing ConPTY exclusion unchanged.
- [x] Run RED cases, implement sample adoption and missing native fixture support, then run all session and interactive tests on six platform jobs for net8.0/net9.0/net10.0. Expected: no new skips hiding session failures; record platform-specific observations.
- [x] Commit `feat: host interactive sessions through reusable coordination`.

### SS08: examples, XML docs, and actual package consumers

**Files:** create SessionSmokeChecks; modify Program, PackageSmokeTests, VerifyPackageConsumer.ps1,
README, samples README, and XML comments for every new public member.
**Interfaces:** `--session-smoke` prints `PTY session smoke check passed.`;
`--session-scope-smoke` prints `PTY session scope smoke check passed.`.

- [x] Add failing smoke cases to PackageSmokeTests before adding the switches. Assert primary status, drained final marker, supplied-stream leave-open behavior, completed diagnostics, and nonempty failure details in a controlled example failure path.
- [x] Implement both checks with C# fixtures under src. The scope case independently observes descendant exit following automatic session finalization, rather than trusting Requested/TargetUnavailable.
- [x] Extend both verifier mode lists for fresh consumers on all TFMs and published net10.0. Expected: all seven smoke modes pass using the packaged DLL/helper, no project-reference substitute.
- [x] Document low-level versus session ownership, sealed input/retry behavior, cancellation/partial writes, source EOF, bounded drain requests versus hard deadlines, output completion, scope limits, failure handling, and metadata privacy. Document host stream cancellation requirements and provide CMD/SH/PowerShell 5.1 commands.
- [x] Run smoke tests and package verification on all six jobs; Windows x64 runs actual PowerShell 5.1. Compile public XML docs and check sample commands against the built artifact.
- [x] Commit `docs: demonstrate and verify coordinated PTY sessions`.

### SS09: completion review and acceptance record

**Files:** update this plan, its design status, and ROADMAP with actual evidence. Preserve all six workflows.

- [x] Run complete Release build/test/pack and existing six-platform CI, with all three TFMs and fresh/published package consumers. Record commit SHA, commands, test counts, failures/fixes, and links below.
- [x] Conduct one independent whole-branch review against the spec and Review Focus; resolve important findings with failing regression tests followed by green suites. Record consciously deferred findings and implementation decisions.
- [x] Verify low-level API compatibility, AnyCPU/package layout, unchanged helper/runtime contract, absence of C/Python and new runtime dependencies, and no accidental version/publication changes.
- [x] Record laptop observations only when supplied: on 2026-10-05 the user ran the Release net10.0 `--session-smoke` and `--session-scope-smoke` commands on the identified Windows x64 laptop; both passed. Host-interaction checks remain pending and separate from CI.
- [x] Mark only evidenced tranches complete and report PR readiness. Commit `docs: record session orchestration acceptance`.

## Final verification commands

```text
dotnet build Icod.Pty.sln -c Release
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net8.0 --no-build
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net9.0 --no-build
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net10.0 --no-build
dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --session-smoke
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --session-scope-smoke
```

From PowerShell (Windows PowerShell 5.1 on the Windows acceptance host):

```powershell
./packaging/VerifyPackageArtifact.ps1 -ArtifactDirectory artifacts -Configuration Release
./packaging/VerifyPackageConsumer.ps1 -ArtifactDirectory artifacts
```

Expected completion is met: existing/new tests and consumer modes pass across six platforms/three frameworks,
review findings are resolved, and laptop evidence is neither inferred nor copied from an earlier milestone.

## Implementation evidence and open acceptance

- User selected option 3 plus focused option 4 and approved implementation on 2026-10-04.
- SS01-SS08 implement the approved public surface, serialized input, optional input forwarding, output forwarding, shutdown integration, bounded draining, deterministic cleanup, diagnostics, native tests, sample adoption, and seven packaged smoke modes.
- Before the resumed review, local Release validation used the repository's existing reflection runner because its then-current container blocked normal test-host transport: 176/176 passed per TFM, the solution built with zero warnings/errors, and exact-package verification passed 28/28 modes. The resumed runtime did not contain a .NET SDK, so the final review fixes were freshly compiled and exercised by normal `dotnet test` in CI.
- Whole-branch review reproduced four lifecycle races in [run 38](https://github.com/uniblab/Icod.Pty/actions/runs/37299542369): accepted shutdown did not cancel the optional source read; primary exit could surface writer-stop cancellation instead of its exit result; a secondary input failure did not interrupt primary-exit draining; and unrelated pump cancellation could be suppressed. Regression tests failed for all four before the implementation fixes.
- Follow-up CI exposed and fixed preservation of a real output fault over a synthetic stop, atomic descendant-readiness publication, Windows linked-token normalization at the console-adapter boundary, and platform-correct Linux timeout versus Windows/macOS EOF expectations. [Run 41](https://github.com/uniblab/Icod.Pty/actions/runs/37302086646) then passed five platforms and exposed only a load-sensitive five-second stress-test budget on Windows x64; that native final-output test now uses the existing 15-second stress budget without changing product defaults.
- Initial implementation commit `e704f86b8a5a7fb58bdf5deaa07352796a02cff6` passed [six-platform CI run 42](https://github.com/uniblab/Icod.Pty/actions/runs/37302728188). Subsequent local Windows 10.0.26200.9457 Debug verification exposed one test-harness gap on every TFM: the startup JSON probe removed CSI cursor controls but not ConPTY OSC window-title controls. [Run 44](https://github.com/uniblab/Icod.Pty/actions/runs/37324409476) proved the new OSC regression RED independently on every platform/framework before the test-only stripper fix.
- A second local Windows 10.0.26200.9457 Debug run confirmed the OSC correction: the original startup JSON test passed on every TFM. It also exposed an unrelated net9.0-only synchronization flaw in the explicit interactive-delimiter test, which stopped at the first ConPTY newline before the child's `READY:` record. Test commit `68abf00be4fd687dc4e744b4a6b6310997d2cb85` now waits for semantic readiness and then the record terminator.
- A subsequent docs-only run exposed a separate lifecycle-diagnostics test race on Linux x64 net8.0: an immediate in-memory output EOF could legitimately precede the test's nominally initial snapshot. Test commit `ac1dd974e721ccf4d7bf84ce943d2a1cd60b56b9` gates that read so the before/after states are deterministic.
- The final test-corrected implementation passed [six-platform CI run 49](https://github.com/uniblab/Icod.Pty/actions/runs/37329904247). Linux and macOS passed 181/181 tests per TFM. Windows passed 179 tests with the one documented ConPTY fragmented-query test skipped (180 total) per TFM, including net9.0 on Windows x64. Every job built with zero warnings and ran all 28 package smoke invocations: seven modes on fresh net8.0/net9.0/net10.0 consumers plus published net10.0 output. Windows x64 used Windows PowerShell 5.1 for artifact and consumer verification. These corrections change only test synchronization/normalization; shipped library/package behavior is unchanged.
- Compatibility review found the low-level public API unchanged and the new surface additive. The library remains AnyCPU, net8.0/net9.0/net10.0, version `0.1.0-alpha.1`; the exact package retains all three DLL/XML targets and the managed Unix helper. No C/Python source, runtime package dependency, tag, publication, or version change was introduced. No review finding is consciously deferred.
- On 2026-10-05 the user ran the Release net10.0 `--session-smoke` and `--session-scope-smoke` commands on the identified Windows x64 laptop. The outputs were `PTY session smoke check passed.` and `PTY session scope smoke check passed.` CMD/Windows PowerShell 5.1 interaction, Ctrl+C, resize, and restored host editing/history remain pending manual evidence and do not block the hosted CI acceptance recorded here.
