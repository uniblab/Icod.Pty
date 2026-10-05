# Icod.Pty roadmap

## Direction and constraints

Icod.Pty provides pseudoterminal process hosting for consumers that supply their own terminal interface.
Development prioritizes reliable interactive sessions, explicit process ownership, and consistent resource cleanup.

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.

## Completed milestones

### Foundation

[PR #1](https://github.com/uniblab/Icod.Pty/pull/1) was merged on 2026-10-04.
It delivers process launch, arguments, environment and working-directory settings, byte streams, resize,
exit status, cancellation-aware waiting, primary-process forced termination, disposal, and package verification.

The user reported successful net10.0 smoke and CMD/Windows PowerShell 5.1 command checks on Windows
10.0.26200.9457. These establish command input/output; they do not establish later interactive-host acceptance.

Historical documents: [foundation design](docs/PTY-Design.md) and
[foundation implementation plan](docs/PTY-Implementation-Plan.md).

### Interactive hosting and controlled shutdown

[PR #2](https://github.com/uniblab/Icod.Pty/pull/2) was merged on 2026-10-04.
It adds immediate sample input, resize forwarding, host-console restoration, terminal Ctrl+C input,
application-directed shutdown with optional primary termination, cancellable asynchronous startup, and
expanded interactive verification.

The [design](docs/Interactive-Hosting-Design.md) and
[development roadmap](docs/Interactive-Hosting-Implementation-Plan.md) retain IH01-IH08 and acceptance evidence.
Automated verification passed in
[six-platform CI run 22](https://github.com/uniblab/Icod.Pty/actions/runs/37192831712).
Windows laptop acceptance of the new interactive host remains unrecorded and separate from hosted CI.

The [native ConPTY fragmented-query limitation](docs/ConPTY-Input-Limitations.md) remains documented.
Its opt-in reproducer and independent byte-forwarding tests remain in place.

### Process-group control and descendant cleanup

[PR #3](https://github.com/uniblab/Icod.Pty/pull/3) was merged on 2026-10-04.
It adds opt-in Windows job and Unix initial-group ownership, named Unix signals, explicit control outcomes,
shutdown targeting, and scoped disposal. Primary-process defaults remain unchanged. Native requests,
primary exit, descendant observations, and output EOF remain distinct.

Automated acceptance passed on all six platforms and three frameworks, including fresh/published consumers,
in [run 37216992802](https://github.com/uniblab/Icod.Pty/actions/runs/37216992802).
The user reported Windows x64 Release net10.0 `--scope-smoke` success on 2026-10-04.
Other laptop observations remain separately recorded in the
[implementation evidence](docs/Process-Group-Cleanup-Implementation-Plan.md).
The [design](docs/Process-Group-Cleanup-Design.md) records initial-group coverage, exclusive Unix child-wait
ownership, glibc requirements, retained identity, and native permission limitations.

## Selected milestone: reusable session orchestration and focused lifecycle diagnostics

**Decision, 2026-10-04:** the user selected **option 3 plus a focused subset of option 4** and requested a
new planning PR, an updated full option menu, and a proper development roadmap.

**Status:** implemented and reviewed on PR #4. The test-corrected implementation passed
[six-platform CI run 47](https://github.com/uniblab/Icod.Pty/actions/runs/37327912155) on all three target
frameworks and all package-consumer modes. No version bump or publication is part of this PR; the package
remains 0.1.0-alpha.1. Windows laptop observations remain separate and pending.

Add an optional session owner above PtyProcess to coordinate ordered input, output forwarding, application
shutdown, drain deadlines, and cleanup. Preserve the low-level API and existing backend/scope semantics.
The session owns newly launched processes; supplied-stream ownership and cancellation requirements are explicit.
Focused diagnostics consist of bounded lifecycle history, counters, coherent snapshots, and staged completion
failures. No terminal-content recording or callbacks/exporters are introduced in this milestone.

- Capture launch/session options before yielding and transfer stream ownership only after successful startup.
- Serialize normal input, ETX, and shutdown request bytes; define partial-write cancellation and permanent input sealing after accepted shutdown.
- Distinguish source EOF, output EOF/flush, primary exit, drain timeout, and completed resource release.
- Preserve opt-in force and platform-scope limits; cancellation of a wait does not implicitly terminate a process.
- Adapt the interactive sample while keeping console modes, resize discovery, and restoration in the host.
- Prove ordering/fault behavior with controlled fixtures and native behavior with all six platform jobs, all three frameworks, and actual package consumers.

**Acceptance goal:** a consumer can run and shut down a PTY session without reconstructing competing pump
and teardown loops, and can inspect what completed or failed without confusing request acceptance with exit.

Read the [accepted design](docs/Session-Orchestration-Design.md) and
[development roadmap and evidence](docs/Session-Orchestration-Implementation-Plan.md), tranches SS01-SS09.
The recorded contracts include permanent input sealing after accepted shutdown and the requirement for
cancellation-cooperative streams.

## Full current menu

Effort is relative, not a schedule. Deferred options remain available and are not release commitments.

| # | Option | Decision | Value and return condition |
| --- | --- | --- | --- |
| 1 | Process ownership and descendant cleanup | Completed in PR #3 | Windows job / Unix initial-group ownership, explicit cleanup and shutdown integration. Broader containment needs a separate design. |
| 2 | Broader signals and foreground-job control | Remaining work deferred; medium-large | Named initial-group/primary signals are complete. Revisit additional signals, suspend/resume, or foreground retargeting when a consumer requires their identity and platform rules. |
| 3 | Reusable session orchestration | Implemented on PR #4 | Coordinates ordered input, forwarding, shutdown, draining, failure results and deterministic ownership above PtyProcess. |
| 4 | Diagnostics and capability discovery | Focused lifecycle subset implemented on PR #4 | Adds bounded session lifecycle history, counters/snapshots and completion failure stages. Native startup-stage tracing, general metrics/exporters, callback subscriptions and transcripts remain deferred. |
| 5 | High-concurrency I/O and process waiting | Deferred; large | Reduce worker/polling costs after measuring throughput, memory and cancellation with a representative concurrent-session workload. |
| 6 | Deployment and runtime portability | Deferred; medium-large | Validate or extend trimming, NativeAOT, single-file/self-contained consumers and wider Unix environments. Strong alternative when standalone distribution becomes the immediate priority. |
| 7 | Terminal configuration controls | Deferred; medium-large | Explicit echo, canonical/raw input and control-character settings with truthful platform-specific capabilities. Return when application control of modes is required. |
| 8 | Recording, replay, and automation | Deferred; medium | Timestamped output/resize records, replay and bounded output matching. Benefits from session orchestration first; input capture must be opt-in and screen-aware matching needs a terminal model. |
| 9 | ConPTY compatibility investigation | Deferred bounded research | Investigate the documented fragmented-query behavior with a minimal C# reproducer. No guaranteed native fix; existing evidence/exclusion remains visible. |
| 10 | Resource controls | Deferred; large | Platform-supported process, CPU and memory limits, with separate contracts and no security-sandbox claim. |
| 11 | Persistent sessions and detach/reattach | Deferred; very large | Separate broker, buffering, reconnect protocol and access controls to survive client disconnects. |
| 12 | Terminal emulation and rendering integration | Deferred; very large | Screen model/custom rendering and adjacent Icod integration above byte transport; graphics protocols remain outside this milestone. |
| 13 | Release stabilization and compatibility hardening | Deferred as a dedicated milestone; small-medium | API compatibility checks, targeted lifecycle stress, support matrix and release documentation. Ordinary regression/package checks remain required now; broader release hardening follows when the release target is chosen. |

The earlier combined high-concurrency/deployment option is now split into options 5 and 6.
Interactive hosting and scoped process ownership are completed history. Option 13 records the additional
release-stabilization alternative considered alongside the remaining feature menu.

## Completion policy

- Mark a tranche complete only with verification evidence recorded in its development roadmap.
- Preserve existing API behavior unless the reviewed design explicitly documents an opt-in change.
- Test shipped library and package behavior on all three frameworks and six platforms.
- Report primary exit, scope request, descendant observation, and output EOF separately.
- Record Windows laptop acceptance separately from hosted CI results.
- Merge, version selection, and publication remain separate user decisions.
