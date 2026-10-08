# Icod.Pty 1.0 Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user selected native execution with no subagents.

**Goal:** Prepare, qualify, document, and publish Icod.Pty `1.0.0` directly as the first stable public release.

**Architecture:** Freeze the merged PR #10 product and public surface, then change only release documentation,
package metadata, verification evidence, and demonstrated release-blocking defects. No public prerelease is
introduced. Exact stable-package qualification and interactive Windows acceptance precede the tag; a fresh
public-package consumer confirms delivery immediately after publication.

**Tech Stack:** C# 13, .NET 8/9/10, MSBuild/NuGet, PowerShell 5.1-compatible verification, GitHub Actions.

**Spec:** `docs/Release-1.0-Design.md`

**Pull request:** [#11](https://github.com/uniblab/Icod.Pty/pull/11)

## Global constraints

- No new product feature or public API beyond the post-PR #10 baseline.
- Preserve LGPL-3.0-or-later, one NuGet library package, AnyCPU, and net8.0/net9.0/net10.0.
- Preserve Windows/Linux/macOS x64/ARM64 and the verified Windows floor `10.0.26200.9457`.
- Preserve CMD, SH, and Windows PowerShell 5.1 compatibility; introduce no C or Python.
- Keep historical evidence accurate to the commit and version it originally described.
- Do not merge, tag, or publish from implementation work in this pull request.

## Review focus

- The live package and manual must identify stable `1.0.0`; prerelease wording may remain only in explicit
  historical evidence or the historical branch name.
- The packaged changelog must name every supported feature through PR #10 and must not call timed playback
  deferred.
- The manual must retain ownership, cancellation, cleanup, security, and deployment warnings.
- A `v1.0.0` tag must select exactly one matching DLL package and create a stable GitHub release.
- Tagging remains blocked on exact-`1.0.0` qualification and manual interactive Windows acceptance.
- Public stable-package installation is a post-publication confirmation, not prepublication evidence.

---

### Task 1 (RP01): Select and record the 1.0 release

**Files:** `ROADMAP.md`, `docs/Release-1.0-Design.md`, `docs/Release-1.0-Implementation-Plan.md`

- [x] Merge PR #10 at its reviewed exact head and create the PR #11 release branch.
- [x] Record the approved no-new-features release design, support boundary, and qualification gates.
- [x] Qualify the candidate-shaped package and complete the package manual and history.
- [x] Amend the release design after the user selected direct stable `1.0.0` instead of a public RC.
- [x] Retain `release/1.0.0-rc.1` only as the historical branch name; it does not select the package version.

### Task 2 (RP02): Publish a man-page-style package manual and complete history

**Files:** `README.md`, `CHANGELOG.md`, `samples/README.md`

- [x] Organize `README.md` under man-page-style sections without removing behavioral guidance or examples.
- [x] Add the package version, verified platform boundary, deployment prerequisites, known limitations, files,
  references, authorship, and release procedure in their canonical sections.
- [x] Write a dated `1.0.0` changelog entry covering PRs #1–#10 in chronological order; leave an empty
  `Unreleased` section for later changes.
- [x] Record exact-head net8.0/net9.0/net10.0 timed-playback and terminal-configuration laptop results.
- [x] Replace RC-promotion instructions with direct-stable pre-tag acceptance and post-publication confirmation.

### Task 3 (RP03): Select and protect the stable package version

**Files:** `Icod.Pty.csproj`, package verification scripts or tests only if a demonstrated gap requires them.

- [x] Change the package version to `1.0.0`; retain release notes that point to the packaged changelog.
- [x] Confirm the tag validator accepts `v1.0.0`, classifies it as stable, and the package selector requires an
  exact `1.0.0` match.
- [x] Pack once, run metadata/artifact verification with `-ExpectedVersion 1.0.0`, and inspect the package for
  README, CHANGELOG, LICENSE, all three assemblies/XML files, build target, and three Unix helper files.

### Task 4 (RP04): Reconcile readiness and publication gates

**Files:** `docs/Release-Readiness-Report.md`, `ROADMAP.md`, `README.md`, `samples/README.md`

- [x] Reconcile the report with PR #10, run 137, and the direct-stable decision.
- [x] Classify all named platform and deployment limitations as blocking or nonblocking for stable 1.0.
- [x] Provide one Windows interactive acceptance procedure covering command editing/history, Ctrl+C, resize,
  clean shell exit, and restoration of console modes/code pages.
- [x] Define the fresh public-`1.0.0` consumer as immediate post-publication delivery confirmation.
- [x] State that a delivery-path defect requires prompt corrective action and is not prepublication evidence.

### Task 5 (RP05): Qualify the exact stable package

**Files:** evidence sections in this plan and `ROADMAP.md`; production or harness files only for a demonstrated
defect.

- [x] Run the Release solution on net8.0, net9.0, and net10.0 with zero failures and record expected skips.
- [x] Run exact `1.0.0` package consumers on all three frameworks and every supported published layout.
- [x] Run the complete pull-request matrix on Windows/Linux/macOS x64/ARM64 and record the exact head, run URL,
  jobs, package version, layouts, and NativeAOT classification.
- [x] Review the converted branch against the design, public API baseline, packaged documents, and publication
  gates; correct Important findings with a failing regression or verification check first.
- [x] Record unavailable local tools separately; do not treat an unavailable runner as a pass or product failure.
- [x] Commit final stable evidence and require the resulting exact-head workflow to remain green.

### Task 6 (RP06): Publish and confirm the stable delivery

**Files:** release evidence only if a demonstrated defect does not require a reviewed correction.

- [ ] Complete and record the Windows interactive acceptance procedure on the exact release head.
- [ ] Merge PR #11 after exact stable-package qualification and review.
- [ ] Tag `v1.0.0` only when the operator intends the release workflow to publish it.
- [ ] From a new consumer with no repository/local-feed dependency, install public `Icod.Pty` version `1.0.0`
  and run a representative process, output-drain, and exit-status check.
- [ ] If public delivery fails, preserve the evidence and take prompt corrective action through a reviewed patch
  release; do not rewrite the failed check as prepublication qualification.

## Evidence

- PR #10 merged at `210d6346619b3133d58c7c3485d3dcf1cd562e97` from reviewed head
  `6d45dbfb69ca9b94de9533e569c6e7ee724efb25`.
- Initial candidate-shaped run 135 exposed three harness-only timing/readiness defects: a Windows marker-file
  sharing race, a session final-output stress deadline reached under load, and a macOS helper-readiness watchdog
  reached before the intended assertion. Production code and the public API were unchanged. The release branch
  now waits for a readable Windows PID marker and gives the two stress fixtures bounded 30-second
  readiness/grace windows.
- Pre-conversion head `641d6f710f2fa01a4106076873e39e9ad7270755` passed
  [run 137](https://github.com/uniblab/Icod.Pty/actions/runs/37782321473): metadata plus Windows, Linux, and macOS
  on x64 and ARM64; net8.0/net9.0/net10.0 Release tests; exact package artifacts and consumers; supported,
  relocated, and intentionally incomplete layouts; Windows PowerShell 5.1 tooling; ConPTY classification; and
  informational NativeAOT probes.
- From that exact pre-conversion head, the user ran Windows x64 Release `--terminal-config-smoke` successfully on
  net8.0, net9.0, and net10.0.
- Exact stable head `080c45cc12bbfc5f3e6dcced74773aaf7edf4e02` passed
  [run 139](https://github.com/uniblab/Icod.Pty/actions/runs/37797487047): metadata plus all six Windows, Linux, and
  macOS x64/ARM64 jobs; net8.0/net9.0/net10.0 Release tests; exact `1.0.0` metadata, artifacts, and consumers;
  framework-dependent, self-contained, single-file, trimmed, relocated, and intentionally incomplete layouts;
  Windows PowerShell 5.1 tooling; bounded ConPTY classification; and informational NativeAOT probes. Ordinary
  Release builds completed with zero warnings/errors. Linux/macOS passed 359 tests with one expected skip per
  framework; Windows passed 357 with two expected skips per framework, plus its separately bounded ConPTY
  classifier cases.
- The final review found no Critical issue. Its two Important documentation contradictions and one wording
  ambiguity were corrected in `080c45cc12bbfc5f3e6dcced74773aaf7edf4e02`; targeted consistency checks failed
  before the corrections and passed afterward.
- Final evidence head `94689f3843934318c25e4bc3a68d94a0d309f8c9` repeated metadata and all six platform jobs
  successfully in [run 140](https://github.com/uniblab/Icod.Pty/actions/runs/37799284372).
- The execution container has neither `dotnet` nor `pwsh`; no local runtime result is claimed. Runs 139 and 140
  supply the runtime and package evidence.
