# Deployment Portability Implementation Plan

> **For agentic workers:** use superpowers:executing-plans to implement this plan task by task. Do not dispatch subagents for this Icod.Pty work. Steps use checkbox syntax; mark a step complete only with recorded evidence.

**Goal:** Qualify real packaged application deployment forms, fix demonstrated portability defects, and pin focused compatibility and lifecycle evidence.

**Architecture:** Build fresh consumers from the exact local NuGet package, publish each requested form to an isolated output tree, run the shipped executable and existing smoke modes, and verify the external Unix helper/host contract. Keep the baseline package and public API stable; separate NativeAOT and wider-Unix feasibility from guaranteed support.

**Tech Stack:** C# 13; .NET 8/9/10; AnyCPU library; Windows ConPTY and Unix PTYs; xUnit; PowerShell 5.1/CMD/SH-compatible scripts; GitHub Actions.

**Spec:** [Deployment portability design](Deployment-Portability-Design.md). Read the [main roadmap](../ROADMAP.md) and the existing [terminal configuration plan](Terminal-Configuration-Implementation-Plan.md) for the PR #5 baseline.

**Status:** planning only. No deployment form beyond the already evidenced ordinary package consumer is promoted to supported by this document.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU library assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64; Windows floor 10.0.26200.9457.
- One LGPL-3.0-or-later NuGet library package, currently 0.1.0-alpha.1; no new runtime package dependencies or checked-in native helper binary. NativeAOT consumer builds are feasibility probes.
- CMD/SH/Windows PowerShell 5.1-compatible tooling; C# fixtures, no C or Python source.
- Root solution/library project; C# sources under `src/`.
- Preserve the existing Unix managed helper, its separate buildTransitive assets, explicit DotNetHostPath, and initial installed-runtime prerequisite unless a separately reviewed design changes them.
- Preserve process/session APIs, ownership defaults, output/exit separation, cancellation, and cleanup. No version bump, tag, merge, or publication in the planning PR.
- Run tests for actual changed behavior before implementation (expected RED), then GREEN. Discovery-only tranches report observed evidence without pretending that a successful baseline test was RED.

## Review focus

1. A single-file/self-contained consumer starts on Unix without global `dotnet`: fail with an identifiable host/runtime error and no orphan child; never infer support from the consumer's bundled runtime. Test in DP04-DP05.
2. A complete output directory is moved to a new location: external helper lookup uses the running published application/assembly location and both ownership paths start. Test in DP03-DP05.
3. A publish or trim drops one of the helper DLL/deps/runtimeconfig files: detect the incomplete set before claiming support, and verify startup cleanup rather than producing a hanging handshake. Test in DP03/DP06.
4. An explicit invalid DotNetHostPath or missing helper fails during cancellation/cleanup: retain the original failure and leave no child, descriptors, or transferred caller-owned streams. Test in DP03/DP08.
5. A trim/AOT build succeeds but the first real PTY launch fails: mark that matrix cell unsupported until executable behavior passes; do not suppress warnings to manufacture a green cell. Test in DP06-DP07.

## File and interface map

| Path | Responsibility |
| --- | --- |
| `packaging/VerifyPortableConsumer.ps1` (new) | Create a fresh isolated package consumer, publish a selected framework/RID/mode, inspect output, and execute the actual published artifact with a watchdog. |
| `src/Tests/Icod.Pty.Tests/PortabilityConsumerTests.cs` (new) | Focused missing-assets/host and relocation assertions where CI script observations alone are insufficient. |
| `src/Sample/Program.cs`, existing smoke check sources | Reuse all eight modes; add a narrowly justified mode only when the existing ones cannot prove a behavior. |
| `packaging/VerifyPackageConsumer.ps1`, `packaging/VerifyPackageArtifact.ps1` | Retain ordinary baseline; call or share focused portable-consumer checks without weakening exact-package assertions. |
| `packaging/buildTransitive/Icod.Pty.targets`, `Icod.Pty.csproj`, `tools/Icod.Pty.Host/Icod.Pty.Host.csproj` | Change only if published artifacts demonstrate a packaging defect. |
| `src/Unix/UnixBackend.cs`, `src/Unix/UnixSpawn.cs`, `src/LaunchConfiguration.cs` | Change only for a reproduced host/helper discovery or trim/AOT runtime defect; preserve helper handshake and cleanup. |
| `src/Tests/Icod.Pty.Tests/PublicApiCompatibilityTests.cs`, `packaging/PublicApiBaseline.txt` (new) | Deterministic public surface baseline from merged PR #5 across all TFMs, allowing deliberate reviewed additions. |
| `.github/workflows/pull-request.yaml`, `.github/workflows/distribution-validation.yaml` | Keep the six existing jobs; add bounded published-consumer gates or a separate portability matrix when runner time requires it. |
| `README.md`, `samples/README.md`, `ROADMAP.md`, these two deployment documents | Published layout, runtime prerequisites, verified support matrix, and deferred outcomes. |

The existing `packaging/VerifyPackageConsumer.ps1` constructs its consumer from `src/Sample/*.cs`, builds all three TFMs, and publishes net10.0 conventionally. The new harness may reuse those sources and modes, but must install the local package in an isolated packages directory; it must not accidentally resolve a workspace ProjectReference or a previously published package.

## Sequence

| Tranche | Deliverable | Gate |
| --- | --- | --- |
| DP01 | Freeze exact-package, API, and runner baseline | A |
| DP02 | Deterministic fresh-consumer publish harness | A |
| DP03 | Helper layout, relocation, and missing-host failures | B |
| DP04 | Self-contained final-output consumers | B |
| DP05 | Single-file final-output consumers | C |
| DP06 | Trimmed consumers and targeted corrections | D |
| DP07 | NativeAOT and wider-Unix feasibility report | E |
| DP08 | Focused API/lifecycle compatibility gate | F |
| DP09 | Documentation, independent review, and acceptance | F |

Each tranche is a reviewable commit. Record its commit SHA, exact command, expected versus actual outcome, platform/framework, and workflow URL in this plan as execution proceeds. Do not mark a tranche complete because another tranche's workflow passed.

### DP01: baseline and feasibility inventory

**Files:** `docs/Deployment-Portability-Design.md`, this plan; no production change.

**Interfaces:** produce the fixed merged-PR-#5 baseline SHA and platform/RID/framework list consumed by later tranches.

- [ ] Inspect current package artifact, buildTransitive copy/publish targets, `FindHelper` and `FindDotNet`, both Unix launch paths, and sample modes. Record helper path and installed-runtime assumptions.
- [ ] Run `dotnet build Icod.Pty.sln -c Release`, `dotnet test Icod.Pty.sln -c Release --no-build`, `dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts`, then `./packaging/VerifyDistribution.ps1 -Configuration Release` on a representative runner; compare to the PR #5 six-platform baseline.
- [ ] Probe SDK/runner availability for self-contained, single-file, trim, and NativeAOT publishing on every OS/architecture without claiming runnable support. Record which SDK workload/native toolchain is missing, if any.
- [ ] Commit the feasibility evidence and any necessary scope correction. **Expected:** existing ordinary consumers and current package checks remain green; each unproven mode remains marked unverified.

### DP02: fresh-package deployment harness

**Files:** create `packaging/VerifyPortableConsumer.ps1`; add only the minimal fixture source under `src/Sample/` if existing eight modes need a new observation.

**Interfaces:** script parameters `-ArtifactDirectory <path> -Framework <net8.0|net9.0|net10.0> -RuntimeIdentifier <win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64> -Mode <FrameworkDependent|SelfContained|SingleFile|Trimmed>`; output one structured pass/fail per mode with publish path, host architecture, package version, and helper-layout check. PowerShell 5.1 syntax and argument arrays; no shell-quoted command strings.

- [ ] Write a harness self-test that fails if it resolves a project reference or an old global package, accepts a wrong RID, or executes `Consumer.dll` instead of the final apphost for SingleFile. Verify RED.
- [ ] Create the fresh consumer from the packed artifact, publish into isolated mode/framework/RID directories, inspect the complete published tree, and run the published app with a bounded watchdog. Read failure output without embedding environment or terminal content in the support report.
- [ ] Run the self-test GREEN and the existing eight modes on an ordinary net10.0 consumer. **Expected:** the old path and new harness agree on package identity and behavior.
- [ ] Commit `test: add portable package consumer harness`.

### DP03: external helper and failure boundaries

**Files:** `src/Tests/Icod.Pty.Tests/PortabilityConsumerTests.cs`; conditional changes to `src/Unix/UnixBackend.cs`, `src/Unix/UnixSpawn.cs`, `packaging/buildTransitive/Icod.Pty.targets`, or `Icod.Pty.csproj` only for observed defects.

**Interfaces:** DP02 harness emits a relocatable final output tree. Missing-asset tests remove one file from a copied output, leaving the source package untouched.

- [ ] Add separate tests for moving the entire output directory; missing helper DLL/deps/runtimeconfig; absent `dotnet`/compatible runtime; invalid explicit DotNetHostPath; and cancellation after a failed start. Cover `PtyProcess` and `PtySession`, primary and PlatformScope paths, and caller-owned streams. On Windows assert that Unix helper absence does not affect ConPTY.
- [ ] Run each new test against the current package; record RED only for a real failure of the specified behavior. A passing baseline is evidence, not a contrived failing test.
- [ ] Apply the smallest packaging/discovery fix for any reproduced defect, preserving exception/cleanup contracts and the managed helper protocol. Otherwise record the current boundary and leave product code unchanged.
- [ ] Rerun the focused tests on net8.0/net9.0/net10.0 and existing startup/ownership/session regressions. **Expected:** no leaked child or PTY, no changed default, no swallowed original failure.
- [ ] Commit `test: verify published helper discovery and failure cleanup` (or `fix:` when behavior changed).

### DP04: self-contained consumer qualification

**Files:** DP02 harness and CI workflow; conditional packaging/runtime files only after RED.

**Interfaces:** publish with `-r <RID> --self-contained true` and `PublishSingleFile=false`; run the published executable, with the external helper directory present. Maintain the installed `dotnet` host on Unix for the positive case.

- [ ] Add a failing harness assertion for an incomplete self-contained output or for a mode that exits before the requested process/session smoke actually completes. Verify RED against a deliberately incomplete copied fixture, not production code.
- [ ] Publish and execute all eight modes from fresh net8.0/net9.0/net10.0 consumers on all six runner targets, with separate positive and missing-helper/host negative cases on Unix. Preserve package baseline checks.
- [ ] Fix only a reproduced support defect; test it RED→GREEN. **Expected:** matrix entries distinguish publish success from runtime behavior, with explicit `dotnet` prerequisite on Unix.
- [ ] Commit evidence/harness and any focused fix.

### DP05: single-file consumer qualification

**Files:** DP02 harness; `packaging/buildTransitive/Icod.Pty.targets` or `Icod.Pty.csproj` only for a proven copy/publish defect.

**Interfaces:** publish with `-r <RID> --self-contained true -p:PublishSingleFile=true`. Invoke the final executable directly from both original and relocated output; the three helper assets remain visible beside it under `Icod.Pty.Host/` on Unix.

- [ ] Add a test that detects missing helper files after publish and one that fails when only the apphost is copied without its required external assets; verify the harness catches both.
- [ ] Run primary-only, PlatformScope, cancellation, and terminal-configuration modes on all six platforms/all three frameworks. Assert the process being invoked is the final executable and no project/build output is consulted.
- [ ] Repair any observed asset-copy or discovery bug with a failing regression first; rerun existing ordinary/self-contained modes. **Expected:** success means executable startup and smoke completion, not merely a successful single-file publish.
- [ ] Commit focused code and evidence.

### DP06: trimmed consumer qualification

**Files:** DP02 harness, relevant production/host code and package targets only if evidence points there, targeted tests.

**Interfaces:** publish self-contained with `-p:PublishTrimmed=true` for each supported RID/framework and run the same final-output smoke checks.

- [ ] Capture ILLink/AOT analysis warnings per application, Icod.Pty library, and helper; reproduce the first observable trim failure with a targeted test or package consumer, verify RED.
- [ ] Make narrowly scoped annotations, rooting, or source-generation changes only for reproduced failures. Do not disable trim analysis globally or mark a package supported based on compilation alone.
- [ ] Execute all eight modes across the six-platform/three-framework matrix; if a mode fails, record its exact boundary as unsupported until corrected. Recheck cancellation, scope cleanup, and relocated output.
- [ ] Commit evidence and any verified fix. **Expected:** no silent skips or warnings suppressed without a documented, targeted reason.

### DP07: bounded NativeAOT and broader Unix feasibility

**Files:** report in `docs/Deployment-Portability-Design.md` and this plan; optional throwaway C# probe under ignored `artifacts/` only. No public API or helper packaging changes from this tranche.

**Interfaces:** attempt a published package consumer with `-p:PublishAot=true` on all six runner targets where the native toolchain is available; execute ordinary smoke, then both Unix ownership paths. Record build, runtime, and helper prerequisite independently. Probe a wider Unix environment only when a repeatable runner/container is available.

- [ ] Run the feasibility probe and retain exact diagnostics, SDK/native-toolchain conditions, RID and runtime observations.
- [ ] Classify each cell as works, fails with reproducible product defect, toolchain unavailable, or untested; do not call a compile-only cell supported.
- [ ] If a small product defect is identified, create a RED regression and fix it in a separate reviewed tranche; a new RID helper strategy requires its own design and plan.
- [ ] Commit the feasibility record and explicit support boundary.

### DP08: focused compatibility and stress

**Files:** `packaging/PublicApiBaseline.txt`, `src/Tests/Icod.Pty.Tests/PublicApiCompatibilityTests.cs`, optional `src/Tests/Icod.Pty.Tests/PortabilityConsumerTests.cs`, CI workflow.

**Interfaces:** deterministic sorted public types/members/signatures for net8.0/net9.0/net10.0 from merged PR #5; a reviewed additive change updates the baseline explicitly. Stress run consumes published DP04-DP06 outputs, not only a test-project reference.

- [ ] Generate and review the PR #5 baseline; add a test that detects a deliberately removed/changed public signature (RED) and passes against the intended shipped API (GREEN).
- [ ] Repeat bounded startup cancellation, helper/host failures, primary exit with descendant, output drain, and disposal on published artifacts. Use watchdogs and retain failure details; distinguish timeout from an expected skip.
- [ ] Run three frameworks/six platforms, existing full tests, exact package verifier, and package consumers. **Expected:** no accidental API break, no lifecycle regression, no new hidden skip.
- [ ] Commit the compatibility gate and stress evidence.

### DP09: documentation, independent review, acceptance

**Files:** `README.md`, `samples/README.md`, `ROADMAP.md`, design and plan.

**Interfaces:** one support table with OS/architecture, TFM, deployment mode, build/publish/runtime/full-behavior evidence, helper layout, and installed-runtime/toolchain prerequisites.

- [ ] Document exact commands for CMD, SH, and Windows PowerShell 5.1, plus how to ship the external Unix helper directory. Explain any verified unsupported mode and how to interpret the failure.
- [ ] Review public API, helper path/host selection, all ownership paths, resource rollback, cancellation, and the five Review Focus cases. Resolve important findings with reproducible RED→GREEN evidence.
- [ ] Run Release build/test/pack, exact-package verification, and the final six-platform deployment matrix at the final runtime head. Record commit SHA, workflow URLs, warning counts, expected skips, and every support-table cell. Request Windows laptop observations separately; do not substitute CI for them.
- [ ] Commit acceptance evidence and report readiness without merging, choosing a version, tagging, or publishing.

## Final verification commands

```text
dotnet build Icod.Pty.sln -c Release
dotnet test Icod.Pty.sln -c Release --no-build
dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts
./packaging/VerifyDistribution.ps1 -Configuration Release
./packaging/VerifyPortableConsumer.ps1 -ArtifactDirectory artifacts -Framework net10.0 -RuntimeIdentifier <current RID> -Mode SelfContained
./packaging/VerifyPortableConsumer.ps1 -ArtifactDirectory artifacts -Framework net10.0 -RuntimeIdentifier <current RID> -Mode SingleFile
./packaging/VerifyPortableConsumer.ps1 -ArtifactDirectory artifacts -Framework net10.0 -RuntimeIdentifier <current RID> -Mode Trimmed
```

The workflow matrix repeats the last three commands for all required frameworks and RIDs, and records the NativeAOT feasibility separately. Commands above are planned interfaces until DP02 creates the script.
