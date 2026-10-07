# Release readiness report

**Status:** Open — RS01–RS05 complete; RS06–RS09 remain open.

This report separates deterministic repository evidence, native hosted evidence, exact-package consumer
evidence, published-layout evidence, and operator-observed acceptance. Evidence in one section does not
substitute for another.

## Candidate inventory

| Item | Current value | Status |
| --- | --- | --- |
| Audit capture commit | `a7e4f4367b5701eaa5b64e8740f7040eeaa6baf7` | Recorded |
| Package version | `0.1.0-alpha.1` | Retained; version selection is outside this milestone |
| Target frameworks | net8.0, net9.0, net10.0 | Recorded |
| Qualified RIDs | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` | Requalification Open |
| Public compatibility baseline | 377 entries; 43 exported types | RS02 complete; unchanged and retained |
| Deployment layouts | framework-dependent, self-contained, single-file, trimmed | Exact-head requalification Open |
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
reconciliation found no production change required. Final exact-head reruns are **Open**.

## Native CI

PR #8 final head `590b621fcf7632ba15911326c97972039e7ccb36` passed all six platform jobs and
all three target frameworks in [run 116](https://github.com/uniblab/Icod.Pty/actions/runs/37622622960).
Equivalent evidence for the final PR #9 head is **Open**. The PR workflow now requests six bounded Windows ConPTY
classification reports—two RIDs by three frameworks—and uploads one artifact per RID. The reports and their
cross-architecture classification completed on exact-head [run 124](https://github.com/uniblab/Icod.Pty/actions/runs/37655837994)
at `8114ffa4b2cf9e1432a17f69e5037df61f5c19a6`.
Runs 122 and 123 produced no usable classification evidence: run 122 exposed harness-only report-path and loaded
test-deadline defects, while run 123 ended after its successful metadata job without creating the validation
matrix. Both are excluded from the outcome decision.

Run 124 produced six complete 100-trial reports. Five reports reproduced prefix loss; Windows ARM64 on .NET
10.0.12 completed all 100 trials exactly. Across all reports, 572 trials were exact and 28 lost a prefix, with no
mismatch or timeout. Loss occurred on both architectures and through both the direct and nested-sample host paths.
The deterministic managed probe/runner and sample-forwarding tests passed on all three target frameworks, as did
the full local suite (330 passed and the Windows-only fact skipped once per framework). The approved outcome is
therefore **ConPTY/native limitation**. No product or public-API correction is indicated. Complete exact-head
six-platform qualification remains **Open** for RS08.

## Exact-package consumers

PR #8 run 116 verified the exact package and its noninteractive smoke modes. Metadata hardening, a fresh Staging
package, and exact-head consumer verification for PR #9 are **Open**.

## Published layouts

Framework-dependent, self-contained, single-file, and trimmed layouts passed in PR #8 run 116 across the six
qualified RIDs. Fresh PR #9 evidence is **Open**. NativeAOT remains informational and does not gate readiness.

## Manual Windows acceptance

After PR #8 merged, Windows x64 Release `--automation-smoke` passed on net8.0, net9.0, and net10.0. Manual
acceptance for the exact PR #9 head, including the bounded ConPTY classification command, is **Open** and must
identify the checked-out commit, OS/build, architecture, framework, and host.

## Contract and documentation audit

The 43-type public inventory and seven `Retain` dispositions are recorded in
[Public-Contract-Audit.md](Public-Contract-Audit.md). The audit requires no product correction and preserves the
377-entry baseline. Documentation reconciliation, package metadata, release materials, and supported-use guidance
remain **Open**.

## Readiness recommendation

**Open.** No release version, tag, merge, or publication decision is made by this report. RS09 will recommend one
of: ready for a separately selected prerelease; ready with named nonblocking limitations; or blocked by listed
defects.
