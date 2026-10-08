# Icod.Pty 1.0 Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user selected native execution with no subagents.

**Goal:** Prepare, qualify, and document `1.0.0-rc.1` as the feature-complete candidate for stable Icod.Pty 1.0.

**Architecture:** Freeze the merged PR #10 product and public surface, then change only release documentation,
package metadata, and verification evidence. The candidate is promoted to stable later through a minimal version
and status change after public-package and manual interactive acceptance.

**Tech Stack:** C# 13, .NET 8/9/10, MSBuild/NuGet, PowerShell 5.1-compatible verification, GitHub Actions.

**Spec:** `docs/Release-1.0-Design.md`

## Global constraints

- No new product feature or public API beyond the post-PR #10 baseline.
- Preserve LGPL-3.0-or-later, one NuGet library package, AnyCPU, and net8.0/net9.0/net10.0.
- Preserve Windows/Linux/macOS x64/ARM64 and the verified Windows floor `10.0.26200.9457`.
- Preserve CMD, SH, and Windows PowerShell 5.1 compatibility; introduce no C or Python.
- Keep historical evidence accurate to the commit and version it originally described.
- Do not tag or publish from this pull request.

## Review focus

- A stale `0.1.0-alpha.1` statement must not describe the candidate package outside historical evidence.
- The packaged changelog must name every supported feature through PR #10 and must not call timed playback deferred.
- The manual must retain ownership, cancellation, cleanup, security, and deployment warnings after restructuring.
- A `v1.0.0-rc.1` tag must select exactly one matching DLL package and mark the GitHub release as prerelease.
- Stable promotion must remain blocked on manual interactive Windows acceptance and public-candidate consumption.

---

## RP01: Select and record the candidate

**Files:** `ROADMAP.md`, `docs/Release-1.0-Design.md`, `docs/Release-1.0-Implementation-Plan.md`

- [x] Merge PR #10 at its reviewed exact head and create `release/1.0.0-rc.1` from merged `main`.
- [x] Record the approved no-new-features release design, the RC-first strategy, support boundary, and stable
  promotion gates.
- [x] Update the main roadmap with PR #10 merge evidence and the selected 1.0 release-candidate milestone.
- [x] Commit the planning tranche as `docs: plan the 1.0 release candidate`.

## RP02: Publish a man-page-style package manual and complete history

**Files:** `README.md`, `CHANGELOG.md`, `samples/README.md`

- [ ] Reorganize `README.md` under man-page-style sections without removing behavioral guidance or examples.
- [ ] Add the package version, verified platform boundary, deployment prerequisites, known limitations, files,
  references, authorship, and release procedure in their canonical sections.
- [ ] Write a dated `1.0.0-rc.1` changelog entry covering PRs #1–#10 in chronological order; leave a new empty
  `Unreleased` section for later changes.
- [ ] Record the exact-head net8.0/net9.0/net10.0 timed-playback laptop results in the manual, sample guide, roadmap,
  and timed-playback evidence plan.
- [ ] Scan the live release documents for stale statements that timed playback is deferred or manual acceptance is
  unreported. Preserve historical statements only where their historical context is explicit.
- [ ] Commit as `docs: prepare the 1.0 package manual and history`.

## RP03: Select and protect the candidate package version

**Files:** `Icod.Pty.csproj`, package verification scripts or tests only if a demonstrated gap requires them.

- [ ] Change the package version to `1.0.0-rc.1` and point NuGet release notes to the complete release history,
  compatibility notes, and known limitations in the packaged changelog.
- [ ] Confirm the tag validator accepts `v1.0.0-rc.1`, classifies it as a prerelease, and the package selector
  requires an exact `1.0.0-rc.1` match.
- [ ] Pack once, run metadata/artifact verification with `-ExpectedVersion 1.0.0-rc.1`, and inspect the package for
  README, CHANGELOG, LICENSE, all three assemblies/XML files, build target, and three Unix helper files.
- [ ] Commit as `build: select 1.0.0-rc.1`.

## RP04: Reconcile readiness and stable-promotion gates

**Files:** `docs/Release-Readiness-Report.md`, `ROADMAP.md`, `README.md`, `samples/README.md`

- [ ] Update the readiness report for the post-PR #10 surface, run 132, and exact-head manual timed-playback runs.
- [ ] Classify all named platform and deployment limitations as blocking or nonblocking for the candidate.
- [ ] Provide one Windows interactive acceptance procedure covering command editing/history, Ctrl+C, resize, clean
  shell exit, and restoration of console modes/code pages.
- [ ] State that a fresh consumer must install the public RC package before stable promotion and that promotion
  changes no feature/API contract unless a blocking defect is found.
- [ ] Commit as `docs: define 1.0 promotion gates`.

## RP05: Qualify the exact candidate

**Files:** evidence sections in `docs/Release-1.0-Implementation-Plan.md` and `ROADMAP.md`; production or harness
files only for a demonstrated defect.

- [ ] Run the Release solution on net8.0, net9.0, and net10.0 with zero failures and record expected skips.
- [ ] Run the exact `1.0.0-rc.1` package consumer on all three frameworks and the locally available published and
  relocated layouts.
- [ ] Run the complete pull-request matrix on Windows/Linux/macOS x64/ARM64 and record the exact head, run URL,
  jobs, package version, layouts, and NativeAOT classification.
- [ ] Review the whole branch against the design, public API baseline, packaged documents, and promotion gates;
  correct Important findings with a failing regression or verification check first.
- [ ] Record any unavailable local tool separately; do not treat an unavailable runner as a pass or product failure.
- [ ] Commit final evidence as `docs: complete 1.0 release candidate acceptance`.

## RP06: Promote after external acceptance

**Files:** `Icod.Pty.csproj`, `CHANGELOG.md`, `ROADMAP.md`, `docs/Release-Readiness-Report.md`

- [ ] After this PR merges, tag `v1.0.0-rc.1` only when the operator intends the release workflow to publish it.
- [ ] Install the public RC from a fresh consumer and run representative process, session, recording, automation,
  and timed-playback checks.
- [ ] Complete and record the Windows interactive acceptance procedure.
- [ ] If no blocker is found, open a minimal stable-promotion PR changing `1.0.0-rc.1` to `1.0.0`, move the
  changelog status/date, repeat the exact package/matrix gates, merge, and tag `v1.0.0`.

## Evidence

- PR #10 merged at `210d6346619b3133d58c7c3485d3dcf1cd562e97` from reviewed head
  `6d45dbfb69ca9b94de9533e569c6e7ee724efb25`.
- The execution container has no `dotnet` or `pwsh` executable. Local RP03/RP05 runtime checks therefore remain
  open until a capable runner is available; the complete hosted matrix is the pull-request gate.

