# Release readiness report

**Status:** Ready with named nonblocking limitations — RS01–RS09 complete.

This report separates deterministic repository evidence, native hosted evidence, exact-package consumer
evidence, published-layout evidence, and operator-observed acceptance. Evidence in one section does not
substitute for another.

## Candidate inventory

| Item | Current value | Status |
| --- | --- | --- |
| Audit capture commit | `a7e4f4367b5701eaa5b64e8740f7040eeaa6baf7` | Recorded |
| Package version | `0.1.0-alpha.1` | Retained; version selection is outside this milestone |
| Target frameworks | net8.0, net9.0, net10.0 | Recorded |
| Qualified RIDs | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` | Requalified in run 128 |
| Public compatibility baseline | 377 entries; 43 exported types | RS02 complete; unchanged and retained |
| Deployment layouts | framework-dependent, self-contained, single-file, trimmed | Requalified in run 128 |
| NativeAOT | Informational feasibility probe only | Nonblocking; promotion is outside this milestone |
| Known native limitation | Windows ConPTY may lose a query prefix when the query is fragmented across host writes | Classified as ConPTY/native; documented workaround retained |

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

After PR #8 merged, Windows x64 Release `--automation-smoke` passed on net8.0, net9.0, and net10.0. Manual
acceptance for the exact PR #9 head, including the bounded ConPTY classification command, remains **unreported**.
This is a visible evidence gap, not a hosted-CI or package failure, and does not block the prerelease-readiness
recommendation.

From Windows PowerShell 5.1, fast-forward the PR branch and record the environment before running every existing
noninteractive sample mode:

```powershell
git switch feature/release-stabilization-conpty-roadmap
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
    '--terminal-config-smoke', '--recording-smoke', '--automation-smoke'
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

The 43-type public inventory and seven `Retain` dispositions are recorded in
[Public-Contract-Audit.md](Public-Contract-Audit.md). The audit requires no public-contract correction and
preserves the 377-entry baseline. RS06–RS07 aligned package metadata, the packaged unreleased changelog, lifecycle
guidance, deployment prerequisites, sample acceptance, and the ConPTY operator procedure. The only production
correction in this milestone is the private Windows already-exiting cleanup race described above; the public API
baseline remains byte-for-byte unchanged.

## Named nonblocking limitations and deferred decisions

- Windows ConPTY can intermittently lose a terminal-query prefix when the query is fragmented across host writes;
  send complete replies in one write when possible, but do not treat that as a universal transport guarantee.
- Unix published applications require the complete external helper layout and an installed compatible `dotnet`
  host/runtime. NativeAOT remains an informational feasibility result, not a supported deployment promise.
- Exact-head Windows laptop noninteractive and interactive-host observations remain unreported and separate from
  the successful hosted Windows x64/ARM64 jobs.
- Wider Unix environments, general tracing/exporters, broader terminal controls, richer automation, and other
  roadmap options remain deferred. Merge, version selection, tagging, and publication require separate decisions.

## Readiness recommendation

**Ready with named nonblocking limitations.** The audited public contract, exact package, six supported RIDs,
three target frameworks, and supported published layouts are suitable for a separately selected prerelease.
The verified package still identifies itself as `0.1.0-alpha.1`; this report does not select that version or any
replacement. No known blocking defect remains. Merge, version selection, tagging, and publication stay separate
user decisions.
