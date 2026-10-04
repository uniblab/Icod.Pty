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

## Selected milestone: process-group control and descendant cleanup

**Decision, 2026-10-04:** the user selected process-group control and descendant cleanup and requested a new
planning PR, the full current option menu, and a development roadmap.

**Status:** design and development roadmap approved for implementation on 2026-10-04 in
[PR #3](https://github.com/uniblab/Icod.Pty/pull/3); the native feasibility gate and initial ownership/control matrix passed on all six platforms.
Implementation, lifecycle fault tests, package checks, and independent review fixes are complete.
Final automated acceptance passed in [six-platform run 37216992802](https://github.com/uniblab/Icod.Pty/actions/runs/37216992802), including all three frameworks and package consumers. Windows laptop acceptance remains pending. Package version remains 0.1.0-alpha.1; version selection is separate.

The selection combines option 1, the scoped Unix signal operations from option 2, and the ownership-specific
diagnostics from option 4. It adds opt-in ownership with explicit platform boundaries, preserves existing
primary-process defaults, and distinguishes native requests from confirmed primary exit and output EOF.

- Windows: establish job ownership before child application code runs; clean associated descendants even
  after primary exit.
- Unix: safely identify and signal the initial process group, including members surviving primary exit.
  Prove the identity lifetime before shipping; a cached numeric PGID is insufficient.
- Preserve the distinction between an initial group, a shell's changing foreground group, and all descendants.
  Arbitrary foreground-job retargeting and escaped/background groups outside the initial group are deferred.
- Integrate explicit scope selection with shutdown and opt-in disposal; preserve caller-owned output draining.
- Verify cancellation, failure rollback, primary-before-descendant exit, and resource cleanup on all six platforms.

**Acceptance goal:** a consumer can deliberately own and clean the documented scope, identify unsupported
operations and native failures, and avoid interpreting primary exit as proof that every descendant stopped.

Read the [approved design](docs/Process-Group-Cleanup-Design.md) and
[development roadmap](docs/Process-Group-Cleanup-Implementation-Plan.md).
PG01 passed before production ownership work. Current evidence and any acceptance gaps are recorded in the development roadmap.

## Full current menu

Effort is relative, not a schedule. Deferred options remain available and are not release commitments.

| # | Option | Decision | Value and return condition |
| --- | --- | --- | --- |
| 1 | Process ownership and descendant cleanup | Selected; large | Explicit Windows job and Unix initial-group ownership; predictable scoped cleanup. |
| 2 | Native signals and foreground-job control | Focused subset selected; medium-large | Include Unix hangup, interrupt, terminate, and kill for the primary/owned initial group. Defer arbitrary signals, foreground retargeting, and suspend/resume until a consumer needs broader job control and identity rules are proved. |
| 3 | Reusable session orchestration | Deferred; medium | Coordinate input/output pumps, cancellation, draining, shutdown, and completion. Recommended follow-on to ownership. |
| 4 | Diagnostics and capability discovery | Focused subset selected; small-medium | Include ownership mode, supported targets, request outcomes, and native operation/error context. Defer general tracing, metrics, and raw transcript logging. |
| 5 | High-concurrency I/O and process waiting | Deferred; large | Reduce worker/polling costs and measure throughput, memory, and cancellation latency. Return with a representative concurrent-session workload. |
| 6 | Deployment and runtime portability | Deferred; medium-large | Validate trimming, NativeAOT, single-file and self-contained consumers, plus wider Unix coverage. Prioritize when a concrete distribution target requires it; retain the managed helper/runtime requirement meanwhile. |
| 7 | Terminal configuration controls | Deferred; medium-large | Explicit terminal modes, echo, canonical input, and control characters. Requires honest platform-specific capabilities. |
| 8 | Recording, replay, and automation | Deferred; medium | Timestamped output/resize recording and bounded output-matching helpers. Input capture must be opt-in; screen-aware assertions need a separate terminal model. |
| 9 | ConPTY compatibility investigation | Deferred bounded research | Investigate the recorded fragmented-query behavior with a minimal C# reproducer. No guaranteed native fix; not a prerequisite for this milestone. |
| 10 | Resource controls | Deferred; large | Platform-supported process, CPU, and memory limits. Requires its own contracts; not a security sandbox. |
| 11 | Persistent sessions and detach/reattach | Deferred; very large | Survive client disconnects with a broker, buffered output, protocol, and access controls. Separate host component. |
| 12 | Terminal emulation and rendering integration | Deferred; very large | Screen model, custom UI rendering, and integration with adjacent Icod projects. Separate layer above PTY transport; graphics protocols remain outside this milestone. |

The earlier combined high-concurrency/deployment option is now split into options 5 and 6.
The previous interactive-hosting choice is completed history rather than an unimplemented option.

## Completion policy

- Mark a tranche complete only with verification evidence recorded in its development roadmap.
- Preserve existing API behavior unless the reviewed design explicitly documents an opt-in change.
- Test shipped library and package behavior on all three frameworks and six platforms.
- Report primary exit, scope request, descendant observation, and output EOF separately.
- Record Windows laptop acceptance separately from hosted CI results.
- Merge, version selection, and publication remain separate user decisions.
