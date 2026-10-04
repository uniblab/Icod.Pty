# Icod.Pty roadmap

## Direction and constraints

Icod.Pty provides pseudoterminal process hosting for consumers that supply their own terminal interface. Development prioritizes reliable interactive sessions, explicit process ownership, and consistent resource cleanup.

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.

## Completed foundation

[PR #1](https://github.com/uniblab/Icod.Pty/pull/1) was merged on 2026-10-04. It delivers process launch, arguments, environment and working-directory settings, byte streams, resize, exit status, cancellation-aware waiting, forced termination, and disposal. It also establishes packaging and verification across six OS/architecture combinations and three target frameworks.

The user reported successful net10.0 smoke and interactive CMD/Windows PowerShell 5.1 command checks on Windows build 10.0.26200.9457. These establish command input and output; full interactive acceptance remains part of the next milestone.

Historical documents: [foundation design](docs/PTY-Design.md) and [foundation implementation plan](docs/PTY-Implementation-Plan.md).

## Selected milestone: interactive hosting and controlled shutdown

**Decision:** approved for planning and implementation on 2026-10-04. The selection consists of these five features:

1. A fully interactive sample with immediate input, resize forwarding, and host-terminal restoration.
2. A terminal interrupt operation with explicit, mode-dependent Ctrl+C semantics.
3. Controlled shutdown using an application-specific request, a grace period, and optional forced termination.
4. Cancellable asynchronous startup with resource cleanup before failure is reported.
5. Expanded interactive verification across all six supported platforms.

**Acceptance goal:** launch a shell, interact without waiting for Enter, resize it, interrupt a running command, and close the session cleanly while preserving final output under the documented draining contract.

The [selected design](docs/Interactive-Hosting-Design.md) specifies the approved contracts. [PR #2](https://github.com/uniblab/Icod.Pty/pull/2) implements them; the [development roadmap](docs/Interactive-Hosting-Implementation-Plan.md) records tranches IH01-IH08 and verification evidence. The APIs, interactive host, package checks, and completion review are implemented and verified in [six-platform CI run 22](https://github.com/uniblab/Icod.Pty/actions/runs/37192831712). Windows laptop acceptance of the interactive host remains pending. Package version remains 0.1.0-alpha.1; release preparation will select the next package version separately.

Verification also exposed a [native ConPTY fragmented-query limitation](docs/ConPTY-Input-Limitations.md), reproduced without the sample. The host's byte forwarding is verified independently; arbitrary fragmented query-reply delivery on Windows remains a native limitation, with an opt-in reproducer. A VT parser or replacement native console is outside this milestone.

## Options considered and deferred

| Option | Decision | Value | Reason for deferral / return condition |
| --- | --- | --- | --- |
| Interactive hosting and controlled shutdown | Selected | Make ordinary shells and interactive applications practical consumers of the existing PTY foundation. | Current milestone; see the five features above. |
| Process-group and descendant management | Deferred | Explicit Unix signals and foreground-job control; stronger Windows process ownership; cleanup of child-launched programs. | Requires separate ownership contracts for background jobs, changed process groups, detached descendants, and Windows jobs. Revisit when a consumer requires session-wide cleanup beyond the primary child. |
| High-concurrency I/O and deployment | Deferred | Lower Unix worker-thread use, measured session capacity, and wider deployment validation. | First obtain a representative server workload and deployment target. This option includes the two workstreams below. |

The deferred high-concurrency/deployment option can be split into independently useful work:

- **I/O scalability:** event-driven Unix readiness, process-wait costs, sustained throughput, cancellation latency, and measured limits at increasing session counts.
- **Deployment validation:** trimmed consumers, NativeAOT consumers, single-file deployment behavior, and broader Unix runtime/distribution coverage. The managed Unix helper requirement remains in force unless a later design explicitly changes it.

These alternatives remain available; they are not implicit commitments for the next release. Terminal emulation/rendering, graphics protocols, persistent detach/reconnect sessions, and a public cross-platform terminal-mode API are also outside the selected milestone.

## Completion policy

- Mark a tranche complete only with its verification evidence recorded in the development roadmap.
- Preserve existing API behavior unless the approved design explicitly documents a change.
- Test shipped library and package behavior on all three frameworks and six platforms.
- Record Windows laptop acceptance separately from hosted CI results.
- Merge and publication remain separate user decisions.
