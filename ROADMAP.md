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

### Reusable session orchestration and focused lifecycle diagnostics

**Decision, 2026-10-04:** the user selected **option 3 plus a focused subset of option 4** and requested a
new planning PR, an updated full option menu, and a proper development roadmap.

**Status:** [PR #4](https://github.com/uniblab/Icod.Pty/pull/4) merged on 2026-10-05. Its final head passed
[six-platform CI run 51](https://github.com/uniblab/Icod.Pty/actions/runs/37342768776) on all three target
frameworks and all package-consumer modes. On 2026-10-05, Release net10.0 `--session-smoke` and
`--session-scope-smoke` also passed on the identified Windows x64 laptop. Interactive CMD/Windows PowerShell 5.1
checks for Ctrl+C, resize, and host restoration remain separately pending. That milestone introduced no version
bump or publication; the package remains 0.1.0-alpha.1.

Added an optional session owner above PtyProcess to coordinate ordered input, output forwarding, application
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

### Terminal configuration and focused capability discovery

**Decision, 2026-10-05:** the user selected **option 7 plus a focused portion of option 4**, requested a new
planning PR, preservation of the full option menu, and a proper development roadmap.

**Status:** [PR #5](https://github.com/uniblab/Icod.Pty/pull/5) merged on 2026-10-06. Its final head passed
[six-platform CI run 72, attempt 2](https://github.com/uniblab/Icod.Pty/actions/runs/37363586412) on all three frameworks, including exact package consumers. Manual Windows laptop execution of `--terminal-config-smoke` remains separately pending. Read the
[approved design](docs/Terminal-Configuration-Design.md) and
[development roadmap and evidence](docs/Terminal-Configuration-Implementation-Plan.md), tranches TC01-TC09.
No version, tag, or publication is selected.

The completed first increment configures the child terminal at launch: echo, canonical/noncanonical input,
terminal-generated signals, control characters, read timing, and an explicit Raw preset. A side-effect-free
capability query lets consumers discover supported controls before launch. Null/default requests preserve
today's behavior. Both PtyProcess and PtySession use the same capture and validation path.

- Gate production work on a pure-C# native feasibility matrix across Linux/macOS x64 and ARM64.
- Configure and read back the newly allocated Unix slave before either helper launch path starts a child.
- Report unsupported controls honestly on Windows; reject explicit requests before launch, without emulation
  or mutation of the parent console. Verify default launch remains unchanged on both Windows architectures.
- Separate initial configuration from the child's later state; live read/update/restoration is deferred within option 7.
- Limit option 4 to prelaunch capabilities, unsupported-setting validation, and native configuration-failure context.
- Verify failure cleanup, cancellation, both ownership policies, all three target frameworks, and actual package consumers.

**Acceptance goal:** a consumer can deliberately choose supported initial terminal behavior and determine
unsupported requests without guessing, while old callers retain their defaults and lifecycle contracts.

## Selected milestone: deployment portability and focused compatibility hardening

**Decision, 2026-10-06:** the user selected **option 6 plus a focused portion of option 13** after PR #5
merged. [PR #6](https://github.com/uniblab/Icod.Pty/pull/6) records the design and development roadmap. Read the
[deployment design](docs/Deployment-Portability-Design.md) and
[implementation plan](docs/Deployment-Portability-Implementation-Plan.md), tranches DP01-DP09.

**Status:** implemented and qualified in PR #6. Framework-dependent, self-contained, single-file, and trimmed
consumers passed the full three-framework/six-platform matrix from exact package artifacts. The public API remains
unchanged and is pinned by a 252-entry compatibility baseline. NativeAOT passed a net10.0 feasibility probe on all
six target RIDs but is not promoted to supported status. No version, tag, or publication is selected.

**Goal:** a consumer can determine which published application forms work on each supported
OS/architecture/framework, what external assets and runtime they require, and how failures behave.
Start from the exact NuGet package and its existing framework-dependent Unix helper. Qualify ordinary,
self-contained, single-file, and trimmed consumers by running the final published executable. Keep
the helper external for single-file consumers and report the installed `dotnet` runtime prerequisite
truthfully. Make targeted packaging/runtime fixes only when an actual consumer test demonstrates a defect.

The focused option 13 portion pins the merged PR #5 public API surface, repeats critical startup,
ownership, drain, and disposal scenarios on published artifacts, and records a verified support matrix.
NativeAOT and wider Unix environments receive bounded feasibility investigation; build success alone
does not establish runtime support. Retain all existing six-platform, three-framework ordinary package checks.

**Acceptance goal:** every supported matrix cell has build, published-layout, and executed behavior
evidence; unresolved cells carry a reproducible limitation or remain explicitly unverified. Preserve
the current package and public contracts while recording Windows laptop results separately from hosted CI.

## Selected next milestone: focused recording and replay

**Decision, 2026-10-06:** after PR #6 merged, the user selected **option 8 as a focused next milestone** and approved implementation in [PR #7](https://github.com/uniblab/Icod.Pty/pull/7). The [accepted design](docs/Recording-Replay-Design.md) and [development roadmap](docs/Recording-Replay-Implementation-Plan.md), tranches RR01–RR08, define the implementation and acceptance gates.

**Status:** implementation is present on PR #7. Local Linux x64 checks cover all three target frameworks, the native package smoke path, and a warning-free Release build. Final-head six-platform package qualification remains the completion gate; no version, tag, merge, or publication is selected.

**Goal:** an opt-in `PtySession` recorder writes a bounded, versioned binary record of output bytes accepted by the consumer's output destination and successful terminal resizes. A streaming reader validates and replays the ordered byte and resize events without launching a process. The first increment has finite file and reader limits, explicit complete/truncated/stopped/faulted recording results, and no change to session behavior when recording is disabled.

The plan keeps terminal content out of diagnostics and error messages, distinguishes recorder failure from live session output failure, and verifies format, corruption handling, resource ownership and cross-platform package consumers. Timing is captured as metadata; replay is deterministic in event order without wall-clock pacing. Input capture, bounded output matching, scripting, terminal emulation, generic observer/exporter hooks, and timed playback remain deferred.

**Acceptance goal:** a consumer can capture a bounded session output/resize transcript, detect truncation or failure without ambiguity, and read/replay its valid prefix on another supported platform; the existing package and session contracts remain intact. Merge, version selection, and publication remain separate decisions.

## Full current menu

Effort is relative, not a schedule. Deferred options remain available and are not release commitments.

| # | Option | Decision | Value and return condition |
| --- | --- | --- | --- |
| 1 | Process ownership and descendant cleanup | Completed in PR #3 | Windows job / Unix initial-group ownership, explicit cleanup and shutdown integration. Broader containment needs a separate design. |
| 2 | Broader signals and foreground-job control | Remaining work deferred; medium-large | Named initial-group/primary signals are complete. Revisit additional signals, suspend/resume, or foreground retargeting when a consumer requires their identity and platform rules. |
| 3 | Reusable session orchestration | Completed in PR #4 | Coordinates ordered input, forwarding, shutdown, draining, failure results and deterministic ownership above PtyProcess. |
| 4 | Diagnostics and capability discovery | Lifecycle subset completed in PR #4; terminal-configuration subset complete in PR #5 | Adds prelaunch terminal capabilities and configuration-failure context. General startup tracing, metrics/exporters, callbacks and transcripts remain deferred. |
| 5 | High-concurrency I/O and process waiting | Deferred; large | Reduce worker/polling costs after measuring throughput, memory and cancellation with a representative concurrent-session workload. |
| 6 | Deployment and runtime portability | Qualified in PR #6 | Exact-package framework-dependent, self-contained, single-file, and trimmed consumers are verified across three TFMs and six target RIDs. NativeAOT is a net10.0 feasibility result; wider Unix remains untested. |
| 7 | Terminal configuration controls | Initial launch-time increment complete in PR #5 | Launch-time echo, canonical/noncanonical and Raw input, signal processing, control characters and read timing, with native feasibility/readback gates. Live query/update/restoration and broader controls remain deferred. |
| 8 | Recording, replay, and automation | Focused recording/replay implemented in PR #7; qualification in progress | Opt-in bounded, timestamped output/resize records and validated event-order replay. Input capture, output matching, scripting, timed playback, and screen-aware automation remain deferred. |
| 9 | ConPTY compatibility investigation | Deferred bounded research | Investigate the documented fragmented-query behavior with a minimal C# reproducer. No guaranteed native fix; existing evidence/exclusion remains visible. |
| 10 | Resource controls | Deferred; large | Platform-supported process, CPU and memory limits, with separate contracts and no security-sandbox claim. |
| 11 | Persistent sessions and detach/reattach | Deferred; very large | Separate broker, buffering, reconnect protocol and access controls to survive client disconnects. |
| 12 | Terminal emulation and rendering integration | Deferred; very large | Screen model/custom rendering and adjacent Icod integration above byte transport; graphics protocols remain outside this milestone. |
| 13 | Release stabilization and compatibility hardening | Focused API/stress/support-matrix portion completed in PR #6 | The PR #5 public surface is pinned, published lifecycle regressions run in the deployment matrix, and the support matrix records prerequisites and limitations. Broader release work follows a chosen release target. |

The earlier combined high-concurrency/deployment option is now split into options 5 and 6.
Interactive hosting, scoped process ownership, reusable session orchestration, and launch-time terminal configuration are completed history. The remaining portions of options 4, 7, 8, and 13 stay available after this focused milestone.

## Completion policy

- Mark a tranche complete only with verification evidence recorded in its development roadmap.
- Preserve existing API behavior unless the reviewed design explicitly documents an opt-in change.
- Test shipped library and package behavior on all three frameworks and six platforms.
- Report primary exit, scope request, descendant observation, and output EOF separately.
- Record Windows laptop acceptance separately from hosted CI results.
- Merge, version selection, and publication remain separate user decisions.
