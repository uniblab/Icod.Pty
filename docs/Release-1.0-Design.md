# Icod.Pty 1.0 release design

**Status:** amended and approved on 2026-10-08; direct-stable implementation is in progress.

## Objective

Ship Icod.Pty 1.0 as a stable, documented contract for cross-platform pseudoterminal process hosting.
The product surface is feature-complete after PR #10. The remaining work is release engineering: reconcile the
manual, history, compatibility evidence, version, package, and publication gates without adding another feature
family.

## Release shape

The first public 1.0 deliverable is stable `1.0.0`; no intermediate public release candidate is published. This
decision follows a complete candidate-shaped six-platform qualification and exact-head Windows x64
terminal-configuration smoke checks on net8.0, net9.0, and net10.0. The stable package freezes the post-PR #10
public API and version-1 recording format.

Before tagging, the exact `1.0.0` package must pass the complete hosted matrix and the remaining Windows
interactive-host procedure must be recorded without a blocking defect. After publication, a fresh consumer must
immediately install `1.0.0` from the intended public NuGet source and run a representative PTY operation. That
post-publication check confirms the public delivery path; it is not represented as evidence available before the
stable package exists.

No release tag or package publication is part of this pull request. Those remain explicit operator actions after
merge and acceptance.

## Supported contract

- C# 13 and AnyCPU assemblies targeting net8.0, net9.0, and net10.0.
- Windows, Linux, and macOS on x64 and ARM64; 32-bit processes remain outside the contract.
- Windows support starts at build `10.0.26200.9457`. This is the deliberately narrow verified floor for the 1.0
  line, not a claim that every older ConPTY build necessarily fails.
- Unix uses the packaged external net8.0 managed helper and requires a compatible installed `dotnet` host/runtime,
  including for self-contained consumers.
- Framework-dependent, self-contained, single-file, and trimmed consumer layouts remain supported as qualified.
- NativeAOT remains an informational feasibility result rather than a supported deployment promise.
- The ConPTY fragmented terminal-query limitation remains named and documented; it is not a managed-forwarding
  defect and does not block 1.0.

The public API baseline produced by PR #10 is the 1.0 compatibility boundary. Any public removal or incompatible
signature change restarts release review. Additive API work is deferred until after 1.0 unless it corrects a
release-blocking defect that cannot be repaired privately.

## Package manual

The packaged `README.md` is the primary manual and should resemble a Unix man page while remaining useful on
NuGet and GitHub. Its top-level flow is NAME, SYNOPSIS, DESCRIPTION, SUPPORTED PLATFORMS, API GUIDE, BUILD AND
VERIFY, ACCEPTANCE, RELEASE, FILES, SEE ALSO, AUTHORS, and LICENSE. Detailed behavioral and ownership guidance
stays in the manual; the restructuring must not reduce the warnings that consumers need to use PTYs safely.

The manual states the stable version and distinguishes verified support from feasibility observations. It links
to the full changelog and focused design documents rather than repeating milestone history.

## Release history

`CHANGELOG.md` is the complete chronological feature history. The `1.0.0` entry covers PRs #1 through #10,
including additions, behavior changes, fixes, deployment qualification, and known limitations. It names timed
playback as supported and leaves a new empty `Unreleased` section for later work.

Historical design and implementation documents retain the package versions and evidence that were accurate for
their milestones. The release plan and readiness documents describe the approved direct-stable amendment while
preserving the earlier qualification runs as historical evidence.

## Acceptance and evidence

PR #10 was merged at `210d6346619b3133d58c7c3485d3dcf1cd562e97`. Before merge, its final head
`6d45dbfb69ca9b94de9533e569c6e7ee724efb25` passed the complete six-platform, three-framework matrix and the
user ran `--timed-playback-smoke` successfully on net8.0, net9.0, and net10.0 from that exact head.

PR #11 pre-conversion head `641d6f710f2fa01a4106076873e39e9ad7270755` passed
[run 137](https://github.com/uniblab/Icod.Pty/actions/runs/37782321473): metadata plus Windows, Linux, and macOS on
x64 and ARM64; all three target frameworks; exact package consumers; supported published layouts; Windows
PowerShell 5.1 tooling; ConPTY classification; and informational NativeAOT probes. From that exact head, the user
also ran Windows x64 Release `--terminal-config-smoke` successfully on net8.0, net9.0, and net10.0.

The direct-stable pull request must additionally pass:

1. the complete Release test suite on all three target frameworks;
2. exact-package metadata and artifact validation for `1.0.0`;
3. fresh package consumers on all three frameworks;
4. framework-dependent, self-contained, single-file, and trimmed consumers on all six target RIDs;
5. Windows PowerShell 5.1 parsing and execution gates; and
6. the informational NativeAOT probes without promoting them to supported status.

Interactive Windows acceptance—editing/history, Ctrl+C, resize forwarding, child-shell exit, and restoration of
the original console—is a pre-tag stable gate. The sample guide provides the commands and observations to record.
A fresh public-`1.0.0` consumer is necessarily a post-publication confirmation because no public prerelease is
introduced.

## Non-goals

The release does not add broader signals, high-concurrency redesign, live terminal reconfiguration, generic
telemetry/exporters, input recording, playback speed/pause/seek, regex or screen-aware automation, branching
scripts, resource limits, persistent sessions, terminal emulation, 32-bit support, musl qualification, or
NativeAOT support. These remain post-1.0 roadmap choices.
