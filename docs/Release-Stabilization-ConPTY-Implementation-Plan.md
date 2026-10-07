# Release Stabilization and Focused ConPTY Investigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** approved on 2026-10-07; native inline execution is in progress without subagents in
[PR #9](https://github.com/uniblab/Icod.Pty/pull/9).

**Goal:** audit and stabilize the post-PR #8 package contract, classify the known ConPTY fragmented-input behavior with bounded evidence, and produce a release-readiness report without selecting or publishing a version.

**Architecture:** Keep the public library unchanged unless deterministic evidence proves an internal defect. Add a test-only ConPTY probe around the existing `raw-sequence` child protocol, separate pure classification/reporting from native execution, and publish per-framework Windows evidence from CI. Drive the release audit through exact-package verifiers, documentation ledgers, and the existing six-platform consumer matrix.

**Tech Stack:** C# 13; net8.0, net9.0, and net10.0; xUnit; Windows ConPTY; PowerShell 5.1-compatible packaging scripts; GitHub Actions; JSON via `System.Text.Json`; no C, C++, Python, third-party runtime package, or shipped probe executable.

**Spec:** `docs/Release-Stabilization-ConPTY-Design.md`

## Global Constraints

- Preserve the existing public API baseline unless a deterministic test demonstrates an Icod.Pty defect and the correction stays inside the approved compatibility boundary.
- Keep the probe and its report types internal to `Icod.Pty.Tests`; do not add them to the NuGet package, sample command line, or `packaging/PublicApiBaseline.txt`.
- Continue targeting net8.0, net9.0, and net10.0 on Windows, Linux, and macOS x64/ARM64.
- Keep repository tooling compatible with CMD, SH, and Windows PowerShell 5.1; use only C# and PowerShell.
- Bound every probe attempt, repetition count, inter-fragment delay, temporary file, and child lifetime.
- Never serialize terminal payload, environment variables, command history, credentials, or caller input into evidence; fixed pattern names, byte counts, and aggregate outcomes are sufficient.
- Treat `Reproduced`, `NotReproduced`, `Inconclusive`, and `Unavailable` as evidence outcomes. Only a harness failure fails the classification job.
- Retain the existing strict Unix split-query test and ordinary Windows managed-forwarding tests.
- Keep NativeAOT informational. Version selection, release-note finalization, tagging, merging, and publication remain separate user decisions.

## Review Focus

- A timed-out child with a trace containing the fixed control prefix plus a proper query suffix must classify as `PrefixLost`, not as an unqualified timeout; Task 3 pins this.
- A partial trace that is not an exact query suffix must classify as `Mismatch`; Task 3 pins this so unexpected mutation is not mislabeled as the known limitation.
- An existing report file must be replaced deterministically, while a missing parent directory must fail with an actionable path error; Task 3 pins both cases.
- Cancellation during an inter-fragment delay must dispose the PTY and delete its trace file without converting cancellation into native evidence; Task 4 pins cleanup.
- Non-Windows execution or absence of the opt-in environment variable must leave ordinary tests green and produce no misleading report; Tasks 4 and 5 pin skip/unavailable behavior.

---

### Task 1 (RS01): Establish the release-audit ledgers

**Files:**
- Create: `docs/Public-Contract-Audit.md`
- Create: `docs/Release-Readiness-Report.md`
- Modify: `docs/Release-Stabilization-ConPTY-Implementation-Plan.md`

**Interfaces:**
- Consumes: `packaging/PublicApiBaseline.txt`, `Icod.Pty.csproj`, the approved design, and the final PR #8 evidence.
- Produces: one contract-disposition ledger and one readiness ledger used by Tasks 2 and 6–9.

- [x] **Step 1: Capture the unmodified baseline**

Run:

```sh
dotnet test Icod.Pty.sln -c Release -m:1 -nr:false -p:UseSharedCompilation=false
rg -v '^#|^$' packaging/PublicApiBaseline.txt | wc -l
rg '^type ' packaging/PublicApiBaseline.txt | wc -l
```

Expected: 317/317 tests on each target framework, 377 public baseline entries, and 43 exported types. Record the exact commit and counts in both ledgers.

- [x] **Step 2: Create the public-contract audit table**

Group all 43 exported types under process/startup, control/scope, shutdown, session/diagnostics, terminal configuration, recording/replay, and automation. Give every group columns for `Surface`, `Ownership`, `Cancellation`, `Failure semantics`, `Platform variance`, `Disposition`, and `Evidence`. Initial dispositions are only `Reviewing`, `Retain`, `Clarify`, `Correct`, or `Defer`; no row may remain absent.

- [x] **Step 3: Create the readiness report skeleton**

Record current version `0.1.0-alpha.1`, target frameworks, six RIDs, 377-entry/43-type baseline, supported deployment layouts, NativeAOT's informational status, known ConPTY limitation, and separate sections for repository tests, native CI, exact-package consumers, published layouts, and manual Windows acceptance. Mark every unresolved item explicitly `Open`.

- [x] **Step 4: Verify and commit**

Run: `git diff --check && rg -n 'Reviewing|Open' docs/Public-Contract-Audit.md docs/Release-Readiness-Report.md`

Expected: clean diff; every incomplete audit item is visible rather than implied.

```sh
git add docs/Public-Contract-Audit.md docs/Release-Readiness-Report.md docs/Release-Stabilization-ConPTY-Implementation-Plan.md
git commit -m "docs: establish release readiness ledgers"
```

### Task 2 (RS02): Review the public and behavioral contract

**Files:**
- Modify: `docs/Public-Contract-Audit.md`
- Modify: `docs/Release-Readiness-Report.md`
- Preserve: `packaging/PublicApiBaseline.txt`

**Interfaces:**
- Consumes: the complete ledger from Task 1 and the existing public compatibility test.
- Produces: a `Retain`, `Clarify`, `Correct`, or `Defer` disposition for every group. A `Correct` finding is a stop condition that receives an exact TDD amendment before product code changes.

- [x] **Step 1: Audit each contract group against source, README, and tests**

For every ledger row, check construction/mutability, ownership/disposal, cancellation before and after side effects, timeout bounds, exception/result separation, post-completion behavior, capability reporting, platform variance, and payload secrecy. Cite exact tests or documentation; absence is a finding, not evidence.

- [x] **Step 2: Resolve or gate every finding**

Use `Retain` when code, tests, and documentation agree; `Clarify` when documentation alone is incomplete; and `Defer` when the behavior is coherent but belongs to a later option. For `Correct`, record the exact source owner, proposed test file/name/assertion, compatibility impact, and minimal correction, then stop and amend this plan before touching product code. A public signature change, cross-layer buffering policy, or semantic break requires a separate design.

- [x] **Step 3: Close the audit**

Run the full three-framework suite and `PublicApiCompatibilityTests`. Expected: all tests pass and the baseline remains byte-for-byte unchanged unless the ledger contains an explicitly approved compatibility correction.

- [x] **Step 4: Commit**

```sh
git add docs/Public-Contract-Audit.md docs/Release-Readiness-Report.md
git commit -m "test: close public contract audit"
```

Record `No production change required` when all findings are retained, clarified, or deferred.

### Task 3 (RS03): Implement the pure ConPTY evidence model

**Files:**
- Create: `src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbe.cs`
- Create: `src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbeTests.cs`

**Interfaces:**
- Consumes: fixed sequence `Encoding.UTF8.GetBytes("雪\u001b[A\u001b[12;34R")` and `System.Text.Json`.
- Produces:
  - `internal enum ConPtyTrialOutcome { Exact, PrefixLost, Mismatch, TimedOut }`
  - `internal enum ConPtyProbeOutcome { Reproduced, NotReproduced, Inconclusive, Unavailable }`
  - `internal sealed record ConPtyWritePattern(string Name, int[] FragmentLengths, int DelayMilliseconds)`
  - `internal sealed record ConPtyTrialResult(string HostPath, string Pattern, int Attempt, ConPtyTrialOutcome Outcome, int ReceivedByteCount)`
  - `internal sealed record ConPtyProbeReport(string Schema, string OSDescription, string OSVersion, string Architecture, string Framework, int Repetitions, ConPtyProbeOutcome Outcome, IReadOnlyList<ConPtyTrialResult> Trials)`
  - `ConPtyFragmentationProbe.CreatePatterns()`, `Classify(...)`, and `WriteReport(string, ConPtyProbeReport)`.

- [x] **Step 1: Write failing pattern and classifier tests**

Add tests named `Patterns_cover_intact_every_query_split_and_bytewise_delays`, `Any_prefix_loss_or_mismatch_is_reproduced`, `All_exact_trials_are_not_reproduced`, and `Timeout_without_mismatch_is_inconclusive`. Assert ten patterns: intact query, seven two-part query split points, zero-delay bytewise query, and 10 ms bytewise query. All retain the six-byte fragmented Unicode/up-arrow control prefix and sum to the 14-byte fixed sequence.

- [x] **Step 2: Run the tests and require failure**

Run: `dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net10.0 --filter FullyQualifiedName~ConPtyFragmentationProbeTests`

Expected: compilation failure because the evidence model does not exist.

- [x] **Step 3: Implement the minimal model and classifier**

Use schema identifier `icod-pty/conpty-fragmentation/v1`. `Reproduced` wins when any trial is `PrefixLost` or `Mismatch`; otherwise a timeout yields `Inconclusive`; otherwise all exact trials yield `NotReproduced`. `Unavailable` is created by the platform gate, not inferred from an empty trial list. Reject empty or inconsistent reports rather than silently classifying them.

- [x] **Step 4: Write failing report-boundary tests**

Add `Report_overwrites_an_existing_file_without_payload_fields`, `Report_rejects_a_missing_parent_directory`, `Proper_query_suffix_after_control_prefix_is_prefix_loss`, and `Unrelated_partial_trace_is_mismatch`. Assert UTF-8 JSON, exact schema, aggregate counts, absence of raw bytes and a sentinel `secret-marker`, deterministic replacement, and the two trace classifications from the Review Focus section.

- [x] **Step 5: Implement report writing and trace classification**

Serialize names, counts, environment identity, and outcomes only, with enums represented by their invariant names through `JsonStringEnumConverter`. Write a complete temporary sibling file and atomically replace/move it to the requested path; delete the temporary file on failure. Do not create a missing parent directory implicitly.

- [x] **Step 6: Verify and commit**

Run the focused tests on net8.0, net9.0, and net10.0; expect all to pass.

```sh
git add src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbe.cs src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbeTests.cs
git commit -m "test: add ConPTY evidence model"
```

### Task 4 (RS03–RS04): Add the bounded native probe

**Files:**
- Modify: `src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbe.cs`
- Modify: `src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbeTests.cs`
- Modify: `src/Tests/Icod.Pty.Tests/InteractiveSampleTests.cs`
- Reuse unchanged unless a deterministic fixture defect is proven: `src/Tests/Icod.Pty.TestChild/Program.cs`

**Interfaces:**
- Consumes: `PtyTestSupport.Child("raw-sequence", ...)`, `InteractiveSampleTests.Sample(...)`, the ten patterns from Task 3, and environment variables `ICOD_PTY_VERIFY_SPLIT_QUERIES` and `ICOD_PTY_CONPTY_REPORT_PATH`.
- Produces: `ConPtyFragmentationProbe.RunAsync(int repetitions, CancellationToken cancellationToken, ConPtyTrialExecutor? executor = null)` and a Windows-only test `ConPty_fragmentation_probe_writes_classified_report`.
- Test seam: `internal delegate Task<ConPtyTrialResult> ConPtyTrialExecutor(string hostPath, ConPtyWritePattern pattern, int attempt, CancellationToken cancellationToken)`; native execution passes no delegate, while focused tests inject one.

- [x] **Step 1: Write failing runner-boundary tests**

Add an injectable per-trial delegate and tests `Cancellation_during_fragment_delay_propagates_and_deletes_trace`, `Trial_timeout_records_received_count_and_disposes_process`, and `Unavailable_platform_has_no_trials`. Require cancellation to remain cancellation, not `TimedOut` evidence.

- [x] **Step 2: Run focused tests and require failure**

Expected: missing `RunAsync`/trial-runner interfaces.

- [x] **Step 3: Implement bounded direct and nested trials**

Run both `direct` (`PtyTestSupport.Child`) and `nested-sample` (`InteractiveSampleTests.Sample`) paths for every pattern and five attempts. Bound readiness and completion separately at two seconds, use `Task.Delay(delay, cancellationToken)` between fragments, read the partial trace with file sharing after timeout, dispose every PTY, and delete every trace in `finally`. Startup, disposal, and report-write failures propagate as harness failures; exact, prefix-loss, mismatch, and bounded read timeout become trial evidence.

- [x] **Step 4: Replace the Windows opt-in success assertion with classification**

Keep `Native_terminal_preserves_split_query_reply` strict on Unix and always skipped on Windows. Add `WindowsConPtyProbeFactAttribute`, which runs only on Windows when `ICOD_PTY_VERIFY_SPLIT_QUERIES=1`; otherwise it supplies an explicit skip reason. The new fact runs five repetitions, writes the path from `ICOD_PTY_CONPTY_REPORT_PATH`, and asserts only report validity and completeness—not `NotReproduced`.

- [x] **Step 5: Verify deterministic and native-safe behavior**

Run all probe tests normally on Linux: pure tests pass, Unix strict test passes, Windows classification test skips, and no report is created. Then run the full net10.0 suite to confirm the existing forwarding and native Unix coverage remains intact.

- [x] **Step 6: Commit**

```sh
git add src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbe.cs src/Tests/Icod.Pty.Tests/ConPtyFragmentationProbeTests.cs src/Tests/Icod.Pty.Tests/InteractiveSampleTests.cs
git commit -m "test: classify ConPTY fragmented input"
```

### Task 5 (RS04): Publish Windows CI evidence without making it a native success assertion

**Files:**
- Modify: `.github/workflows/pull-request.yaml`
- Modify: `docs/ConPTY-Input-Limitations.md`
- Modify: `docs/Release-Readiness-Report.md`

**Interfaces:**
- Consumes: the opt-in fact and JSON report contract from Task 4.
- Produces: six JSON files per PR head—win-x64/win-arm64 × net8.0/net9.0/net10.0—uploaded as exact-head artifacts.

- [x] **Step 1: Add the Windows classification step**

After the ordinary test step, on Windows matrix entries only, loop over all three frameworks. Set `ICOD_PTY_VERIFY_SPLIT_QUERIES=1` and set `ICOD_PTY_CONPTY_REPORT_PATH` in PowerShell to `"artifacts/conpty-fragmentation-${{ matrix.rid }}-$framework.json"`; run only `ConPty_fragmentation_probe_writes_classified_report` with `--no-build --no-restore`. A completed `Reproduced`, `NotReproduced`, or `Inconclusive` report exits successfully; missing/malformed output fails the job.

- [x] **Step 2: Upload the reports**

Use `actions/upload-artifact@v4` with one artifact per Windows RID, `if: always()`, seven-day retention, and `if-no-files-found: error`. Keep the ordinary `Test` step unchanged so environmental classification cannot hide deterministic failures.

- [x] **Step 3: Verify workflow/tooling syntax**

Run PowerShell parser checks under `pwsh`; on the identified Windows x64 environment also parse every `.ps1`/`.psm1` under Windows PowerShell 5.1. Inspect the workflow diff for Windows-only conditions and unique artifact names.

- [ ] **Step 4: Commit and observe exact-head CI**

```sh
git add .github/workflows/pull-request.yaml docs/ConPTY-Input-Limitations.md docs/Release-Readiness-Report.md
git commit -m "ci: publish ConPTY fragmentation evidence"
```

Do not classify the result in documentation until the Windows x64 and ARM64 artifacts from the same head exist.

### Task 6 (RS05): Classify the ConPTY evidence and enforce the decision gate

**Files:**
- Modify: `docs/ConPTY-Input-Limitations.md`
- Modify: `docs/Release-Readiness-Report.md`
- Modify: `docs/Public-Contract-Audit.md`
- Modify: `docs/Release-Stabilization-ConPTY-Implementation-Plan.md`

**Interfaces:**
- Consumes: exact-head JSON artifacts and logs from both Windows architectures and all three frameworks.
- Produces: one of `Icod.Pty defect`, `ConPTY/native limitation`, or `Inconclusive`, with counts and source links.

- [ ] **Step 1: Validate and summarize all six reports**

Require matching schema, sequence/pattern inventory, five repetitions, both host paths, and no harness omissions. Tabulate exact, prefix-loss, mismatch, and timeout counts by RID/framework/pattern. Do not combine absent evidence with zero observations.

- [ ] **Step 2: Apply the approved outcome rule**

- If deterministic managed tests lose/reorder/rewrite bytes, record the exact failing scenario, owning source path, proposed test name/assertion, and compatibility impact; stop and amend this plan with an exact TDD correction task before editing product code.
- If managed forwarding is exact and native trials reproduce loss/mutation, make no production change; document the native limitation and workarounds without promising arbitrary fragmented delivery.
- If reports do not reproduce consistently or cannot isolate the layer, retain the prior warning and mark the result `Inconclusive`.
- If any correction needs a public API change, general buffering/retry policy, parser, or terminal emulator, stop implementation and request a separate design.

- [ ] **Step 3: Rerun affected deterministic tests**

Run the probe model/runner tests, `InteractiveSampleTests`, and the full suite on every locally available framework. Expected: no environmental outcome is encoded as a universal assertion.

- [ ] **Step 4: Record evidence and commit**

```sh
git add docs/ConPTY-Input-Limitations.md docs/Release-Readiness-Report.md docs/Public-Contract-Audit.md docs/Release-Stabilization-ConPTY-Implementation-Plan.md
git commit -m "docs: classify ConPTY fragmentation evidence"
```

### Task 7 (RS06): Harden package metadata and release materials

**Files:**
- Create: `CHANGELOG.md`
- Create: `packaging/VerifyPackageMetadata.Tests.ps1`
- Modify: `Icod.Pty.csproj`
- Modify: `packaging/RepositoryTools.psm1`
- Modify: `packaging/VerifyPackageArtifact.ps1`
- Modify: `.github/workflows/pull-request.yaml`

**Interfaces:**
- Consumes: the Task 1 readiness ledger and current NuGet metadata.
- Produces: parsed metadata fields `Authors`, `Description`, `ProjectUrl`, `RepositoryUrl`, `RepositoryType`, `LicenseExpression`, `RequireLicenseAcceptance`, `Readme`, `ReleaseNotes`, and `Tags`, plus exact-package assertions.

- [ ] **Step 1: Write the failing metadata-parser self-test**

Create a temporary `.nupkg` with a fixed nuspec and assert every produced property, namespace-independent XML lookup, normalized readme path, Boolean license-acceptance value, and cleanup. Run it and require failure because `Get-PackageMetadata` currently returns only ID, version, and readme.

- [ ] **Step 2: Extend metadata parsing and artifact assertions**

Require ID `Icod.Pty`, author `Timothy J. Bruce`, description `Cross-platform pseudoterminal process hosting for .NET.`, project/repository URL `https://github.com/uniblab/Icod.Pty`, repository type `git`, license `LGPL-3.0-or-later`, license acceptance `true`, readme `README.md`, nonempty release notes, and tags containing `pty`, `pseudoterminal`, `conpty`, `terminal`, `process`, and `cross-platform`.

- [ ] **Step 3: Add curated unreleased notes without selecting a version**

Create `CHANGELOG.md` with an `Unreleased` section covering PRs #1–#8, deployment prerequisites, and known ConPTY/NativeAOT limits. Add it to the package root and set `PackageReleaseNotes` to direct consumers to the packaged changelog. Do not change `<Version>0.1.0-alpha.1</Version>`.

- [ ] **Step 4: Add the self-test to CI and verify PowerShell 5.1 parsing**

Run `VerifyPackageMetadata.Tests.ps1` beside `VerifyPortableConsumer.Tests.ps1` before restore. Parse all packaging scripts in Windows PowerShell 5.1 and run the metadata self-test there on Windows x64.

- [ ] **Step 5: Pack and verify the exact artifact**

Run a Staging build/pack, `VerifyPackageMetadata.Tests.ps1`, and `VerifyPackageArtifact.ps1`. Inspect the `.nupkg` to confirm README, CHANGELOG, LICENSE, all three TFMs, and helper assets occur exactly where asserted.

- [ ] **Step 6: Commit**

```sh
git add CHANGELOG.md Icod.Pty.csproj packaging/RepositoryTools.psm1 packaging/VerifyPackageArtifact.ps1 packaging/VerifyPackageMetadata.Tests.ps1 .github/workflows/pull-request.yaml
git commit -m "build: harden release package metadata"
```

### Task 8 (RS06–RS07): Close documentation, sample, and package-consumer gaps

**Files:**
- Modify: `README.md`
- Modify: `samples/README.md`
- Modify: `packaging/README.md`
- Modify: `docs/ConPTY-Input-Limitations.md`
- Modify: `docs/Public-Contract-Audit.md`
- Modify: `docs/Release-Readiness-Report.md`

**Interfaces:**
- Consumes: closed audit dispositions, exact-package metadata tests, and classified ConPTY evidence.
- Produces: one coherent supported-use narrative and an exact Windows operator checklist.

- [ ] **Step 1: Audit the documented lifecycle path**

Ensure launch, streams, resize, scope ownership, session shutdown/drain, terminal configuration, recording, and automation examples state ownership, disposal, cancellation, timeout, result, and platform limits. Correct documentation where tests already establish behavior. A newly discovered behavioral or package-consumer defect is a stop condition: record it and amend this plan with an exact failing test before changing code.

- [ ] **Step 2: Document package and deployment truth**

State helper/runtime requirements for framework-dependent, self-contained, single-file, and trimmed applications; retain NativeAOT as informational; describe missing/relocated helper outcomes; and align package metadata, README, changelog, and readiness report terminology.

- [ ] **Step 3: Publish the ConPTY operator procedure**

Document the opt-in filtered-test command for net8.0, net9.0, and net10.0, report path, fixed five-attempt pattern set, outcome meanings, and required OS/build/architecture/host recording. State that `NotReproduced` covers only those attempts and that the probe records no caller payload.

- [ ] **Step 4: Verify docs and samples**

Run every noninteractive package smoke against the exact Staging package on all local target frameworks, run `git diff --check`, and verify every repository-relative Markdown link resolves to an existing file.

- [ ] **Step 5: Commit**

```sh
git add README.md samples/README.md packaging/README.md docs/ConPTY-Input-Limitations.md docs/Public-Contract-Audit.md docs/Release-Readiness-Report.md
git commit -m "docs: complete release readiness guidance"
```

### Task 9 (RS08–RS09): Qualify the final head and issue the readiness handoff

**Files:**
- Modify: `docs/Release-Readiness-Report.md`
- Modify: `docs/Release-Stabilization-ConPTY-Implementation-Plan.md`
- Modify: `ROADMAP.md`
- Modify: PR #9 description after repository evidence is committed

**Interfaces:**
- Consumes: every earlier task, exact package artifacts, six-platform CI, and separately reported manual Windows results.
- Produces: the final release-readiness recommendation and remaining blockers without changing version or publishing.

- [ ] **Step 1: Run fresh local qualification**

```sh
dotnet restore Icod.Pty.sln -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build Icod.Pty.sln -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test Icod.Pty.sln -c Release --no-build --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet pack Icod.Pty.sln -c Staging --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -o artifacts
pwsh -NoProfile -File packaging/VerifyPackageMetadata.Tests.ps1
pwsh -NoProfile -File packaging/VerifyPackageArtifact.ps1 -ArtifactDirectory artifacts -Configuration Staging
pwsh -NoProfile -File packaging/VerifyPackageConsumer.ps1 -ArtifactDirectory artifacts
pwsh -NoProfile -File packaging/VerifyPortableConsumer.Tests.ps1
```

Expected: zero warnings/errors, all three 317-or-higher test totals pass, one exact package passes metadata/artifact checks, every package smoke passes, and the portable harness self-test passes. Record fresh counts rather than copying earlier evidence.

- [ ] **Step 2: Require complete exact-head CI**

Require green Linux x64/ARM64, macOS x64/ARM64, and Windows x64/ARM64 jobs, all three frameworks, exact-package verification, ordinary/package/published layouts, single-file, trimmed, self-contained, and the informational NativeAOT probes. Require all six ConPTY JSON reports from the same head and classify any expected test skips explicitly.

- [ ] **Step 3: Prepare manual Windows acceptance**

List Release commands for all existing smoke modes on net8.0/net9.0/net10.0 plus the opt-in ConPTY classification command under Windows PowerShell 5.1. Do not mark them passed until the user reports output from the identified laptop/build and exact checked-out head.

- [ ] **Step 4: Finalize the readiness report and roadmap**

State one recommendation: ready for a separately selected prerelease, ready with named nonblocking limitations, or blocked by listed defects. Record supported behavior, known limitations, audit dispositions, exact commits/runs, manual evidence status, and deferred work. Mark RS01–RS09 complete only where evidence is present.

- [ ] **Step 5: Run the completion verification and commit**

Run `git status --short`, `git diff --check`, focused tests, the full suite, and exact-package checks again after the documentation update.

```sh
git add ROADMAP.md docs/Release-Readiness-Report.md docs/Release-Stabilization-ConPTY-Implementation-Plan.md
git commit -m "docs: finalize release readiness evidence"
```

- [ ] **Step 6: Update PR #9 without changing repository evidence**

Summarize the exact-head local/CI/manual evidence, ConPTY classification, API/package disposition, and readiness recommendation in the PR body. Do not create another documentation-only commit merely to record CI for its own head. Stop for the user's merge/version/publication decision.
