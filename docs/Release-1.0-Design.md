# Icod.Pty 1.0 release design

**Status:** approved on 2026-10-08; release-candidate implementation is in progress.

## Objective

Ship Icod.Pty 1.0 as a stable, documented contract for cross-platform pseudoterminal process hosting.
The product surface is feature-complete after PR #10. The remaining work is release engineering: reconcile the
manual, history, compatibility evidence, version, package, and promotion gates without adding another feature
family.

## Release shape

The first deliverable is `1.0.0-rc.1`. It freezes the post-PR #10 public API and version-1 recording format,
packages the complete supported feature set, and permits validation through the public NuGet delivery path.
Stable `1.0.0` follows only after the release candidate has been installed by a fresh consumer, the remaining
manual Windows interaction checks have been recorded, and no blocking defect is found. Promotion should change
only the version, changelog/release status, and tag unless a demonstrated defect requires a reviewed correction.

No release tag or package publication is part of this pull request. Those remain explicit operator actions after
merge.

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

The manual states the selected version and distinguishes verified support from feasibility observations. It links
to the full changelog and focused design documents rather than repeating milestone history.

## Release history

`CHANGELOG.md` is the complete chronological feature history. The `1.0.0-rc.1` entry covers PRs #1 through #10,
including additions, behavior changes, fixes, deployment qualification, and known limitations. It must mention
timed playback as supported and must not leave PR #9 or PR #10 implicit. Future work returns to an empty
`Unreleased` section above the candidate entry.

Historical design and implementation documents retain the package versions and evidence that were accurate for
their milestones. They are not rewritten to pretend that an earlier milestone built the release candidate.

## Acceptance and evidence

PR #10 was merged at `210d6346619b3133d58c7c3485d3dcf1cd562e97`. Before merge, its final head
`6d45dbfb69ca9b94de9533e569c6e7ee724efb25` passed the complete six-platform, three-framework matrix and the
user ran `--timed-playback-smoke` successfully on net8.0, net9.0, and net10.0 from that exact head.

The release-candidate pull request must pass:

1. the complete Release test suite on all three target frameworks;
2. exact-package metadata and artifact validation for `1.0.0-rc.1`;
3. fresh package consumers on all three frameworks;
4. framework-dependent, self-contained, single-file, and trimmed consumers on all six target RIDs;
5. Windows PowerShell 5.1 parsing and execution gates; and
6. the informational NativeAOT probes without promoting them to supported status.

The user's existing exact-head timed-playback runs close that feature's manual evidence gap. Interactive Windows
acceptance—editing/history, Ctrl+C, resize forwarding, and restoration of the original console—remains a stable
`1.0.0` promotion gate rather than an RC publication blocker. The manual provides the commands and observations
to record.

## Non-goals

The release does not add broader signals, high-concurrency redesign, live terminal reconfiguration, generic
telemetry/exporters, input recording, playback speed/pause/seek, regex or screen-aware automation, branching
scripts, resource limits, persistent sessions, terminal emulation, 32-bit support, musl qualification, or
NativeAOT support. These remain post-1.0 roadmap choices.
