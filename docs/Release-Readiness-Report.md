# Release readiness report

**Status:** Feature-complete and exact-package qualified for direct stable `1.0.0`; manual pre-tag Windows
interactive acceptance remains.

RS01–RS09 established prerelease readiness after PR #8. PR #10 completed the selected feature set, and PR #11
completed a candidate-shaped qualification before the user selected direct stable publication. The package
manual, complete feature history, stable version, and publication gates are now aligned with that decision.
`1.0.0` is not tagged or published by this pull request.

This report separates deterministic repository evidence, native hosted evidence, exact-package consumer
evidence, published-layout evidence, and operator-observed acceptance. Evidence in one section does not
substitute for another.

## Release inventory

| Item | Current value | Status |
| --- | --- | --- |
| Audit capture commit | `a7e4f4367b5701eaa5b64e8740f7040eeaa6baf7` | Historical PR #9 audit baseline |
| Timed-playback merge | `210d6346619b3133d58c7c3485d3dcf1cd562e97` | PR #10 merged 2026-10-08 |
| Stable qualification head | `080c45cc12bbfc5f3e6dcced74773aaf7edf4e02` | PR #11 run 139 passed |
| Final evidence head | `94689f3843934318c25e4bc3a68d94a0d309f8c9` | PR #11 run 140 passed |
| Package version | `1.0.0` | Selected for direct stable qualification; not tagged or published here |
| Target frameworks | net8.0, net9.0, net10.0 | Recorded |
| Qualified RIDs | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` | Exact stable package requalified in run 139 |
| Public compatibility baseline | 381 entries; 44 exported types | Post-PR #10 stable boundary |
| Deployment layouts | framework-dependent, self-contained, single-file, trimmed | Exact stable package requalified in run 139 |
| NativeAOT | Informational feasibility probe only | Nonblocking; promotion is outside this milestone |
| Known native limitation | Windows ConPTY may lose a query prefix when the query is fragmented across host writes | Classified as ConPTY/native; documented workaround retained |

## Post-PR #10 release evidence

Pre-conversion head `641d6f710f2fa01a4106076873e39e9ad7270755` passed metadata and all six
Windows/Linux/macOS x64/ARM64 jobs in
[run 137](https://github.com/uniblab/Icod.Pty/actions/runs/37782321473). All three target frameworks passed their
Release tests. Exact package verification and consumers, framework-dependent/self-contained/single-file/trimmed
publishes, relocated and intentionally incomplete helper layouts, Windows PowerShell 5.1 tooling, bounded ConPTY
classification, and informational NativeAOT probes completed in the applicable jobs. This evidence qualified the
unchanged product and release harness.

Exact stable head `080c45cc12bbfc5f3e6dcced74773aaf7edf4e02` then passed
[run 139](https://github.com/uniblab/Icod.Pty/actions/runs/37797487047). Metadata plus all six platform/RID jobs
passed, including net8.0/net9.0/net10.0 Release tests, exact `1.0.0` package verification and consumers, supported
and relocated deployment layouts, intentional incomplete-helper cases, Windows PowerShell 5.1 tooling, bounded
ConPTY classification, and informational NativeAOT probes. Ordinary Release builds completed with zero
warnings/errors. Linux/macOS passed 359 tests with one expected skip per framework; Windows passed 357 with two
expected skips per framework, plus its separately bounded ConPTY classifier cases.

Run 135 exposed three fixture-only timing/readiness failures while qualifying the unchanged product: a Windows
marker could exist before its writer released the file, a session final-output stress case reached its 15-second
grace period under load, and a macOS helper-readiness watchdog expired before the assertion it guarded. The
corrections wait until the Windows marker contains a readable PID and use bounded 30-second readiness/grace
windows for the stress paths. Runs 136 and 137 passed all three previously failing platform/framework combinations. No
production source or public API changed.

PR #10 implementation head `1a02ab33477244f7c15273cae0a4727054bbeec2` passed metadata and the complete
Windows/Linux/macOS x64/ARM64 matrix in
[run 131](https://github.com/uniblab/Icod.Pty/actions/runs/37684672876). Final evidence head
`6d45dbfb69ca9b94de9533e569c6e7ee724efb25` repeated that matrix in
[run 132](https://github.com/uniblab/Icod.Pty/actions/runs/37685894498). Both runs exercised net8.0, net9.0,
and net10.0, exact-package consumers, framework-dependent, self-contained, single-file, and trimmed layouts, and
the informational NativeAOT probes on all six RIDs.

At the implementation source tree, the full Release suite passed 359 tests with one expected interactive ConPTY
classifier skip on each framework. The focused timed-playback/reader suite passed 44/44 and its scheduler suite
passed 8/8 on every framework. The API baseline changed additively from 377 entries/43 types to 381 entries/44
types: one options type, its constructor/property, and `PtyRecordingReader.PlayTimedAsync`. The version-1 format
and immediate replay signature remain unchanged.

## Repository tests

At the audit capture and again at RS02 close, the Release suite passed on Linux x64:

| Framework | Passed | Failed | Skipped | Status |
| --- | ---: | ---: | ---: | --- |
| net8.0 | 317 | 0 | 0 | Baseline passed |
| net9.0 | 317 | 0 | 0 | Baseline passed |
| net10.0 | 317 | 0 | 0 | Baseline passed |

The filtered `PublicApiCompatibilityTests` gate also passed once on each framework. The baseline SHA-256 remained
`2d78f94fc4ef2a535ccf591fa4f98e84c9d09b0fd552c28b0b8d9bf7801e4188`. The RS02 public-contract
reconciliation found no production change required. After the private Windows cleanup correction, the fresh
Release suite passed **332 tests with one expected Windows-only classifier skip and no failures on each of
net8.0, net9.0, and net10.0**. The Release build completed with zero warnings and zero errors.

## Native CI

PR #9 candidate head `ddb5633a38a4a73dce8aa2b582e5ae63916d2351` passed metadata plus all six
platform jobs and all three target frameworks in
[run 128](https://github.com/uniblab/Icod.Pty/actions/runs/37663074586). The jobs exercised repository tests,
exact-package verification, package consumers, all supported published layouts, Windows PowerShell 5.1 tooling,
and the informational NativeAOT probes. The workflow produced both RID-specific artifacts and all six bounded
Windows ConPTY reports.

Run 127 exposed a private cleanup race after ordinary Windows tests had passed: `TerminateProcess` can return
`ERROR_ACCESS_DENIED` while an already-exiting process handle has not yet become signaled. The corrected private
Windows path waits for that documented terminal race only, while preserving every other native termination
failure. Two regression tests cover the accepted race and the retained-error path. No public signature or public
behavior changed.

The first complete cross-architecture classification was exact-head
[run 124](https://github.com/uniblab/Icod.Pty/actions/runs/37655837994) at
`8114ffa4b2cf9e1432a17f69e5037df61f5c19a6`.
Runs 122 and 123 produced no usable classification evidence: run 122 exposed harness-only report-path and loaded
test-deadline defects, while run 123 ended after its successful metadata job without creating the validation
matrix. Both are excluded from the outcome decision.

Run 124 produced six complete 100-trial reports. Five reports reproduced prefix loss; Windows ARM64 on .NET
10.0.12 completed all 100 trials exactly. Across all reports, 572 trials were exact and 28 lost a prefix, with no
mismatch or timeout. Loss occurred on both architectures and through both the direct and nested-sample host paths.
The deterministic managed probe/runner and sample-forwarding tests passed on all three target frameworks, as did
the full local suite (330 passed and the Windows-only fact skipped once per framework). The approved outcome is
therefore **ConPTY/native limitation**. No product or public-API correction is indicated. Run 128 repeated the
six-report inventory after the cleanup correction: 564 trials were exact and 36 lost a prefix, with no mismatch
or timeout. Both host paths and both architectures were represented. This independently confirms the prior
classification without turning an intermittent environmental result into a universal test assertion.

## Exact-package consumers

PR #8 run 116 verified the exact package and its noninteractive smoke modes. RS06 added namespace-independent
nuspec parsing and exact assertions for package identity, author, description, project/repository metadata,
license acceptance, readme, release notes, tags, CHANGELOG, LICENSE, all three TFMs, build target, and helper
assets. A fresh local Staging `0.1.0-alpha.1` package passed those assertions and every noninteractive smoke on
net8.0, net9.0, and net10.0. After the private Windows correction, a fresh package repeated those assertions,
all 30 framework smoke runs, the published net10.0 smoke set, and the portable-consumer harness self-test. Run
128 repeated exact-package verification and package-consumer execution on all six RIDs.

## Published layouts

Framework-dependent, self-contained, single-file, and trimmed layouts passed in PR #9 run 128 across all six
qualified RIDs. Relocated helpers worked and intentionally incomplete helper layouts failed as specified.
Unix applications still require the complete external three-file helper and an installed compatible `dotnet`
host/runtime. NativeAOT remains informational and does not gate readiness.

## Manual Windows acceptance

After PR #8 merged, Windows x64 Release `--automation-smoke` passed on net8.0, net9.0, and net10.0. From exact
final PR #10 head `6d45dbfb69ca9b94de9533e569c6e7ee724efb25`, Windows x64 Release
`--timed-playback-smoke` also passed on net8.0, net9.0, and net10.0. From exact PR #11 pre-conversion head
`641d6f710f2fa01a4106076873e39e9ad7270755`, Windows x64 Release `--terminal-config-smoke` passed on net8.0,
net9.0, and net10.0. These feature checks remain separate from hosted CI.

Interactive Windows acceptance of command editing/history, Ctrl+C, resize forwarding, child-shell exit, and
restoration of the original console remains unreported. It is a pre-tag stable `1.0.0` gate. The consolidated
procedure is in [the sample acceptance guide](../samples/README.md#10-stable-release-acceptance).

From Windows PowerShell 5.1, check out the exact reviewed release commit and record the environment before
running every existing noninteractive sample mode. Do not substitute a moving branch name for the reviewed SHA.

```powershell
git status --short
git rev-parse HEAD
[System.Environment]::OSVersion.Version
$env:PROCESSOR_ARCHITECTURE
$Host.Name
$PSVersionTable.PSVersion

dotnet restore Icod.Pty.sln
dotnet build Icod.Pty.sln -c Release --no-restore
$modes = @(
    '--smoke', '--lifecycle-smoke', '--cancel-start-smoke', '--invalid-host-smoke',
    '--interrupt-smoke', '--scope-smoke', '--session-smoke', '--session-scope-smoke',
    '--terminal-config-smoke', '--recording-smoke', '--automation-smoke',
    '--timed-playback-smoke'
)
foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
    foreach ($mode in $modes) {
        dotnet run --project samples\Icod.Pty.Sample -c Release -f $framework --no-build -- $mode
        if ($LASTEXITCODE -ne 0) { throw "$framework $mode failed with exit code $LASTEXITCODE." }
    }
}
```

Then run the three-framework classifier command in
[ConPTY-Input-Limitations.md](ConPTY-Input-Limitations.md) and retain its JSON reports with the recorded commit,
OS/build, architecture, framework, host, and PowerShell version. Do not interpret `NotReproduced` as proof that
the intermittent limitation is absent.

## Contract and documentation audit

The 43-type PR #9 public inventory and seven `Retain` dispositions are recorded in
[Public-Contract-Audit.md](Public-Contract-Audit.md). The audit requires no public-contract correction and
preserved its 377-entry baseline. PR #10 then made the reviewed additive change to 381 entries/44 types. No
existing entry was removed or changed. RS06–RS07 aligned package metadata, lifecycle guidance, deployment
prerequisites, sample acceptance, and the ConPTY operator procedure; the 1.0 release adds the man-page-style
package manual and complete PR #1–#10 changelog. The only PR #9 production correction was the private Windows
already-exiting cleanup race described above.

## Stable limitation classification

| Item | Stable 1.0 classification | Evidence or gate |
| --- | --- | --- |
| Fragmented ConPTY terminal-query prefix loss | Nonblocking native limitation; use one write when possible and retain the documented classifier | Classified on all six hosted platform jobs |
| External Unix helper and installed `dotnet` runtime | Nonblocking deployment prerequisite | Verified in complete and intentionally incomplete layouts |
| NativeAOT | Unsupported; informational feasibility only | Does not gate stable 1.0 |
| Windows launch-time terminal controls | Nonblocking platform capability difference; explicit unsupported requests fail before launch | Covered by automated and manual smoke checks |
| 32-bit, musl, and unlisted Unix RIDs | Outside the advertised matrix | Do not gate the stated contract |
| Richer signals, terminal mutation, richer recording/automation, persistence, resources, and emulation | Deferred to post-1.0 | No additional feature is required for stable 1.0 |
| Exact `1.0.0` package and matrix | Satisfied at `080c45cc12bbfc5f3e6dcced74773aaf7edf4e02` | Run 139 passed all seven jobs |
| Windows interactive host acceptance | Blocking before tag | Pending editing, Ctrl+C, resize, exit, and restoration observations |
| Fresh public `1.0.0` consumer | Immediate post-publication confirmation | Cannot precede publication when no public prerelease is created |

## Readiness recommendation

**Ready to merge; not yet ready to tag or publish.** No additional product feature is required. The exact stable
package and final evidence head have passed the complete matrix. Before tagging, the
Windows interactive procedure must be recorded without a blocking defect. After merge and an explicit `v1.0.0`
tag, a fresh consumer must immediately install the public stable package and verify a representative PTY
operation. Because the public package does not exist beforehand, that final delivery-path confirmation is
recorded after publication rather than used as an intermediate-RC gate.
