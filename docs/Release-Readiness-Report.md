# Release readiness report

**Status:** Feature-complete, qualified, and ready to publish `1.0.0-rc.1` with named nonblocking limitations.

RS01–RS09 established prerelease readiness after PR #8. PR #10 subsequently completed the selected feature set,
and the 1.0 release-candidate milestone updates the package manual, complete feature history, version, and stable
promotion gates. Stable `1.0.0` is not yet selected or published.

This report separates deterministic repository evidence, native hosted evidence, exact-package consumer
evidence, published-layout evidence, and operator-observed acceptance. Evidence in one section does not
substitute for another.

## Candidate inventory

| Item | Current value | Status |
| --- | --- | --- |
| Audit capture commit | `a7e4f4367b5701eaa5b64e8740f7040eeaa6baf7` | Historical PR #9 audit baseline |
| Timed-playback merge | `210d6346619b3133d58c7c3485d3dcf1cd562e97` | PR #10 merged 2026-10-08 |
| Candidate qualification head | `371df2c986d710e8d47975884bbbe52e6b812e97` | PR #11 run 136 passed |
| Package version | `1.0.0-rc.1` | Selected for candidate qualification; not tagged or published here |
| Target frameworks | net8.0, net9.0, net10.0 | Recorded |
| Qualified RIDs | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` | Requalified in run 136 |
| Public compatibility baseline | 381 entries; 44 exported types | Post-PR #10 candidate boundary |
| Deployment layouts | framework-dependent, self-contained, single-file, trimmed | Requalified in run 136 |
| NativeAOT | Informational feasibility probe only | Nonblocking; promotion is outside this milestone |
| Known native limitation | Windows ConPTY may lose a query prefix when the query is fragmented across host writes | Classified as ConPTY/native; documented workaround retained |

## Post-PR #10 candidate evidence

Exact `1.0.0-rc.1` candidate head `371df2c986d710e8d47975884bbbe52e6b812e97` passed metadata and all six
Windows/Linux/macOS x64/ARM64 jobs in
[run 136](https://github.com/uniblab/Icod.Pty/actions/runs/37780272079). All three target frameworks passed their
Release tests. Exact package verification and consumers, framework-dependent/self-contained/single-file/trimmed
publishes, relocated and intentionally incomplete helper layouts, Windows PowerShell 5.1 tooling, bounded ConPTY
classification, and informational NativeAOT probes completed in the applicable jobs.

Run 135 exposed three fixture-only timing/readiness failures while qualifying the unchanged product: a Windows
marker could exist before its writer released the file, a session final-output stress case reached its 15-second
grace period under load, and a macOS helper-readiness watchdog expired before the assertion it guarded. The
corrections wait until the Windows marker contains a readable PID and use bounded 30-second readiness/grace
windows for the stress paths. Run 136 passed all three previously failing platform/framework combinations. No
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
`--timed-playback-smoke` also passed on net8.0, net9.0, and net10.0. These close the timed-playback laptop gate and
remain separate from hosted CI.

Interactive Windows acceptance of command editing/history, Ctrl+C, resize forwarding, child-shell exit, and
restoration of the original console remains unreported. This does not block publishing the release candidate,
but it is a stable `1.0.0` promotion gate. The consolidated procedure is in
[the sample acceptance guide](../samples/README.md#10-stable-promotion-acceptance).

From Windows PowerShell 5.1, fast-forward the PR branch and record the environment before running every existing
noninteractive sample mode:

```powershell
git switch release/1.0.0-rc.1
git pull --ff-only
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
prerequisites, sample acceptance, and the ConPTY operator procedure; the 1.0 candidate adds the man-page-style
package manual and complete PR #1–#10 changelog. The only PR #9 production correction was the private Windows
already-exiting cleanup race described above.

## Candidate limitation classification

| Item | Candidate classification | Stable classification |
| --- | --- | --- |
| Fragmented ConPTY terminal-query prefix loss | Nonblocking native limitation; use one write when possible and retain the documented classifier | Nonblocking when documentation remains accurate |
| External Unix helper and installed `dotnet` runtime | Nonblocking deployment prerequisite verified in complete and intentionally incomplete layouts | Nonblocking support boundary |
| NativeAOT | Nonblocking informational feasibility only | Remains unsupported unless separately promoted after 1.0 |
| Windows launch-time terminal controls | Nonblocking platform capability difference; explicit unsupported requests fail before launch | Nonblocking support boundary |
| 32-bit, musl, and unlisted Unix RIDs | Nonblocking because they are outside the advertised matrix | Remain outside the 1.0 contract |
| Richer signals, terminal mutation, automation, recording, persistence, resources, and emulation | Nonblocking deferred features | Post-1.0 roadmap work |
| Exact `1.0.0-rc.1` package/matrix result | Satisfied at `371df2c986d710e8d47975884bbbe52e6b812e97` in run 136 | Must remain green at the promoted commit |
| Public RC installation from a fresh consumer | Does not block building the RC | Stable blocker until recorded |
| Windows interactive host acceptance | Does not block publishing the RC | Stable blocker until editing, Ctrl+C, resize, exit, and restoration pass |

## Readiness recommendation

**Ready to publish `1.0.0-rc.1` after PR #11 is reviewed and merged; not yet ready to tag stable `1.0.0`.** No
additional product feature is required. The exact candidate version and packaged documents have passed the
complete matrix. After candidate publication, a fresh consumer must install the public package and the remaining
Windows interactive acceptance must be recorded. If both succeed without a blocking defect, stable promotion
should change only the version, changelog/readiness status, and tag; it should not add or alter the feature/API
contract.
