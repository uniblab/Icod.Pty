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

### Focused recording and replay

**Decision, 2026-10-06:** after PR #6 merged, the user selected **option 8 as a focused next milestone** and approved implementation in [PR #7](https://github.com/uniblab/Icod.Pty/pull/7). The [accepted design](docs/Recording-Replay-Design.md) and [development roadmap](docs/Recording-Replay-Implementation-Plan.md), tranches RR01–RR08, define the implementation and acceptance gates.

**Status:** [PR #7](https://github.com/uniblab/Icod.Pty/pull/7) merged on 2026-10-06; implementation qualified before merge. The runtime and stress head passed the complete six-platform,
three-framework, exact-package and published-consumer matrix in
[run 100, attempt 2](https://github.com/uniblab/Icod.Pty/actions/runs/37504691344). The final additive boundary and
lifecycle-test head passed the same matrix in [run 102](https://github.com/uniblab/Icod.Pty/actions/runs/37507494777).
No version, tag, or publication was selected. The user reported successful Windows x64 laptop Release/net10.0 `--recording-smoke`
execution on 2026-10-06: `PTY recording smoke check passed.` Other-framework manual checks remain unreported.

**Goal:** an opt-in `PtySession` recorder writes a bounded, versioned binary record of output bytes accepted by the consumer's output destination and successful terminal resizes. A streaming reader validates and replays the ordered byte and resize events without launching a process. The first increment has finite file and reader limits, explicit complete/truncated/stopped/faulted recording results, and no change to session behavior when recording is disabled.

The plan keeps terminal content out of diagnostics and error messages, distinguishes recorder failure from live session output failure, and verifies format, corruption handling, resource ownership and cross-platform package consumers. Timing is captured as metadata; replay is deterministic in event order without wall-clock pacing. Input capture, bounded output matching, scripting, terminal emulation, generic observer/exporter hooks, and timed playback remain deferred.

**Acceptance goal:** a consumer can capture a bounded session output/resize transcript, detect truncation or failure without ambiguity, and read/replay its valid prefix on another supported platform; the existing package and session contracts remain intact. Version selection and publication remain separate decisions.

### Focused live matching and scripted interaction

**Decision, 2026-10-06:** after PR #7 merged, the user selected a **focused continuation of option 8**. The [design](docs/Focused-Automation-Design.md) and [development roadmap](docs/Focused-Automation-Implementation-Plan.md), tranches FA01–FA07, define the implementation and qualification gates.

**Status:** [PR #8](https://github.com/uniblab/Icod.Pty/pull/8) merged on 2026-10-07. Final head
`590b621fcf7632ba15911326c97972039e7ccb36` passed all six platform jobs, three target frameworks,
the exact-package consumer, and framework-dependent, self-contained, single-file, and trimmed published
consumers in [run 116](https://github.com/uniblab/Icod.Pty/actions/runs/37622622960). After fast-forwarding
the local branch to that exact head, the user reported successful Windows x64 laptop Release
`--automation-smoke` execution on net8.0, net9.0, and net10.0; each run printed
`PTY automation smoke check passed.` This manual acceptance remains separate from hosted CI.

**Goal:** a consumer opts into bounded, binary-safe live output matching and runs a short, ordered sequence of send/expect steps against a PTY session. Matches consume accepted output bytes in order; startup prompts and immediate replies remain available across asynchronous reads. Match timeout, output completion, and buffer overrun are explicit, while the live session and optional recording retain independent outcomes.

**Boundary:** integrate with the existing session output pump and ordered input writer; allocate the bounded observer only for opted-in sessions. The caller keeps ownership of the session and supplied streams. Keep the recording format and output-only capture unchanged. Input recording, timed replay, regex/text matching, screen interpretation, branching scripts, generic exporters, version selection, and publication remain deferred.

**Acceptance goal:** all three target frameworks and six platform/RID jobs exercise the exact package consumer and published application modes, with controlled tests for early prompts, chunk boundaries, cap exhaustion, faults, cancellation, and cleanup. Record manual Windows laptop observations separately from hosted CI. No merge or release is implied by selecting this milestone.

## Release stabilization and focused ConPTY investigation

**Decision, 2026-10-07:** after PR #8 merged, the user selected **option 13 plus a focused portion of
option 9**. [PR #9](https://github.com/uniblab/Icod.Pty/pull/9) records the selection. The
[approved design boundary](docs/Release-Stabilization-ConPTY-Design.md) defines the release
stabilization work and limits the ConPTY investigation to an evidence-producing, pure-C# reproducer. The
[development roadmap](docs/Release-Stabilization-ConPTY-Implementation-Plan.md), tranches RS01–RS09, defines the
implementation and evidence gates. No version, tag, publication, or production workaround is implied by
selecting this milestone.

**Status:** [PR #9](https://github.com/uniblab/Icod.Pty/pull/9) merged on 2026-10-07. Candidate head
`ddb5633a38a4a73dce8aa2b582e5ae63916d2351` passed metadata and all six platform/architecture jobs, all three
target frameworks, exact-package consumers, and all supported published layouts in
[run 128](https://github.com/uniblab/Icod.Pty/actions/runs/37663074586). The 377-entry/43-type public API baseline
remains unchanged. Bounded Windows evidence classifies fragmented query-prefix loss as a ConPTY/native limitation,
not a managed-forwarding defect. Exact-head CI also exposed and verified a private fix for an already-exiting
Windows process cleanup race. The [readiness report](docs/Release-Readiness-Report.md) recommends **ready with
named nonblocking limitations** for a separately selected prerelease; laptop acceptance, merge, version, tag,
and publication remain separate.

**Goal:** determine whether the current public surface and shipped package are ready for a deliberately chosen
prerelease, correct evidence-supported compatibility defects before they become durable contracts, and replace
the broad ConPTY fragmented-query warning with a reproducible classification. Preserve all existing default
behavior while reviewing ownership, cancellation, deadlines, failure results, capability reporting, package
metadata, documentation, samples, and the verified deployment matrix.

The focused option 9 work constructs a minimal C# experiment that compares intact and deliberately fragmented
input delivery through ConPTY. It records the framework, architecture, host, fragmentation pattern, repetition
count, and outcome without treating environmental non-reproduction as proof of absence. Deterministic managed
forwarding tests remain required; native observations report reproduced, not reproduced, or inconclusive.
Production changes are allowed only when the evidence locates a correctable Icod.Pty defect and the reviewed
compatibility boundary permits the correction. Otherwise the milestone retains the reproducer and documents the
native limitation or inconclusive result.

**Boundary:** no general tracing/exporter API, terminal emulation, new automation language, input recording,
persistent broker, resource-control subsystem, or unrelated refactoring. NativeAOT remains a feasibility result.
Version selection, release notes finalization, tagging, and publication remain separate decisions after the
readiness report.

**Acceptance goal:** complete the public-contract, documentation, sample, package, and compatibility audits;
classify the focused ConPTY behavior with repeatable evidence; run all three target frameworks and six platform/RID
jobs against exact package artifacts and published application layouts; record Windows laptop acceptance
separately; and finish with an explicit release-readiness report listing supported behavior, known limitations,
remaining blockers, and any recommended version target.

### Focused timed playback

**Decision, 2026-10-07:** after PR #9 merged, the user selected a focused continuation of **option 8:
timed playback of existing output-and-resize recordings**. The [design](docs/Timed-Playback-Design.md)
and [development roadmap](docs/Timed-Playback-Implementation-Plan.md) define this increment. Planning and review
do not select a package version, tag, or publication.

**Status:** [PR #10](https://github.com/uniblab/Icod.Pty/pull/10) merged on 2026-10-08 at
`210d6346619b3133d58c7c3485d3dcf1cd562e97`. Implementation head
`1a02ab33477244f7c15273cae0a4727054bbeec2` passed the complete matrix in
[run 131](https://github.com/uniblab/Icod.Pty/actions/runs/37684672876), and final evidence head
`6d45dbfb69ca9b94de9533e569c6e7ee724efb25` repeated it in
[run 132](https://github.com/uniblab/Icod.Pty/actions/runs/37685894498): metadata, all six
Windows/Linux/macOS x64/ARM64 jobs, all three target frameworks, exact-package consumers,
framework-dependent/self-contained/single-file/trimmed layouts, and informational NativeAOT probes passed.
From that exact final head, the user ran Release `--timed-playback-smoke` successfully on the identified Windows
x64 laptop with net8.0, net9.0, and net10.0.

**Goal:** let a consumer stream the validated recording's output and resize events at their recorded relative
times without launching a PTY. Preserve the v1 recording format, binary payloads, event order, independent
reader limits, source ownership, and the existing immediate output-only replay method. The consumer applies each
event to its own destination; Icod.Pty does not interpret terminal content or control a host console.

**Boundary:** one opt-in timed event-dispatch method with a configurable elapsed-time cap, monotonic scheduling,
cancellation, and explicit callback/format failures. No input capture, playback speed control, pause/seek, live
script branching, terminal emulation, or general observer/exporter API. This milestone does not expand NativeAOT
or the ConPTY compatibility promise.

**Acceptance goal:** deterministic scheduling and failure tests, exact-package smoke on net8.0/net9.0/net10.0,
and the complete six-platform published-consumer matrix. Record Windows laptop acceptance separately from CI.

## Selected milestone: 1.0 release candidate

**Decision, 2026-10-08:** after PR #10 merged, the user accepted the recommendation that Icod.Pty is
feature-complete for 1.0 and selected a no-new-features release-preparation milestone. The
[release design](docs/Release-1.0-Design.md) and
[development roadmap](docs/Release-1.0-Implementation-Plan.md), RP01–RP06, define the candidate and stable
promotion gates.

**Status:** [PR #11](https://github.com/uniblab/Icod.Pty/pull/11) is qualified from merged PR #10 on
`release/1.0.0-rc.1`. Exact candidate head `371df2c986d710e8d47975884bbbe52e6b812e97` passed metadata and all six
Windows/Linux/macOS x64/ARM64 jobs in
[run 136](https://github.com/uniblab/Icod.Pty/actions/runs/37780272079). The run covered all three target
frameworks, exact `1.0.0-rc.1` package consumers, framework-dependent/self-contained/single-file/trimmed and
relocated layouts, intentional incomplete-layout failures, Windows PowerShell 5.1 tooling, ConPTY classification,
and informational NativeAOT probes. This milestone selects package version
`1.0.0-rc.1`, the 381-entry/44-type post-PR #10 public compatibility baseline, and the version-1 recording format
as the candidate contracts. It does not tag or publish the package.

**Goal:** deliver a coherent package manual, complete feature history through PR #10, reconciled release-readiness
evidence, an exact candidate package, and a repeatable path from public RC validation to stable `1.0.0`.

**Boundary:** correct release documentation, metadata, verification, or demonstrated blocking defects only.
Broader signals, high-concurrency redesign, live terminal changes, generic telemetry, richer recording/automation,
resource controls, persistence, terminal emulation, NativeAOT promotion, and wider platform qualification remain
post-1.0 choices.

**Promotion gate:** the complete candidate matrix has passed, so PR #11 is ready for review and the
`1.0.0-rc.1` package is ready to publish after merge when the operator intends the release workflow to run.
Promote to `1.0.0` only after a fresh public-package consumer succeeds, the remaining Windows interactive
acceptance is recorded, and no blocking defect remains. Stable promotion should otherwise change version and
release status only.

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
| 8 | Recording, replay, and automation | Recording/replay complete in PR #7; live matching/scripts complete in PR #8; focused timed playback complete in PR #10 | Input capture, speed/seek, regex/text matching, branching, and screen-aware automation remain deferred until a demonstrated post-1.0 need. |
| 9 | ConPTY compatibility investigation | Focused investigation completed in PR #9 | The bounded pure-C# classifier reproduced native prefix loss across Windows architectures while deterministic managed forwarding remained exact. |
| 10 | Resource controls | Deferred; large | Platform-supported process, CPU and memory limits, with separate contracts and no security-sandbox claim. |
| 11 | Persistent sessions and detach/reattach | Deferred; very large | Separate broker, buffering, reconnect protocol and access controls to survive client disconnects. |
| 12 | Terminal emulation and rendering integration | Deferred; very large | Screen model/custom rendering and adjacent Icod integration above byte transport; graphics protocols remain outside this milestone. |
| 13 | Release stabilization and compatibility hardening | Completed in PR #9; 1.0 release-candidate preparation selected after PR #10 | The audited contract and six-platform qualification now feed a documentation, package-version, exact-candidate, and stable-promotion gate with no new feature surface. |

The earlier combined high-concurrency/deployment option is now split into options 5 and 6.
Interactive hosting, scoped process ownership, reusable session orchestration, launch-time terminal configuration,
focused recording/replay, focused automation, release stabilization, and the bounded ConPTY investigation are
completed history. Remaining portions of options 4, 7, 8, 9, and 13 stay available beyond this milestone.

## Completion policy

- Mark a tranche complete only with verification evidence recorded in its development roadmap.
- Preserve existing API behavior unless the reviewed design explicitly documents an opt-in change.
- Test shipped library and package behavior on all three frameworks and six platforms.
- Report primary exit, scope request, descendant observation, and output EOF separately.
- Record Windows laptop acceptance separately from hosted CI results.
- Merge, version selection, and publication remain separate user decisions.
