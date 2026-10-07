# Native ConPTY input limitation

Icod.Pty forwards bytes to Windows ConPTY without parsing VT sequences. ConPTY interprets input into console events; it is not an opaque POSIX terminal byte transport. During this milestone, repeated tests found that fragmenting a CSI cursor-position reply across separate input writes can lose its prefix on hosted Windows x64 and ARM64. The sample cannot recover bytes discarded by the native console path.

## Evidence

[CI run 20](https://github.com/uniblab/Icod.Pty/actions/runs/37191999568) at `9d391c8ba91c09c1685dccd1f1c8e251cca87801` reproduced the loss both directly through `PtyProcess` and through the interactive sample. [Run 21](https://github.com/uniblab/Icod.Pty/actions/runs/37192324484) at `ab421e5409ad2cc0d76e6b10d27613117e312a46` reproduced it with native child read buffers of both 1 and 4096 bytes, across both Windows architectures and all three .NET targets. It is intermittent, not specific to .NET 8 or the sample.

The Windows ARM64 image was `20260924.168.1`; its [published image manifest](https://github.com/actions/runner-images/blob/win11-vs2026-arm64/20260924.168/images/windows/Windows11-VS2026-Arm64-Readme.md) identifies OS version **10.0.26200.9457**, the requested minimum build. The x64 image was Windows Server 2025 (`windows-latest`). This is not solely evidence from an older Windows baseline.

| Input | Expected hex | Observed hex on failing attempts |
| --- | --- | --- |
| `雪`, Up arrow, then `ESC [ 12 ; 34 R`, each byte written separately | `E99BAA1B5B411B5B31323B333452` | `E99BAA1B5B4152` |

The child recorded received bytes in a shared file, independently of screen rendering. Unicode and arrow input survived; the query reply arrived as `R`. Changing rendered acknowledgements and native read-buffer size did not remove the loss. This localizes the observation below the sample's forwarding loop; it does not identify an upstream fix or promise that other fragmented sequences are unaffected.

PR #9 exact-head [run 124](https://github.com/uniblab/Icod.Pty/actions/runs/37655837994) at
`8114ffa4b2cf9e1432a17f69e5037df61f5c19a6` repeated the bounded classifier on Windows x64 and ARM64. Both
RID-specific artifacts contained the required net8.0, net9.0, and net10.0 reports; every report used schema
`icod-pty/conpty-fragmentation/v1`, both host paths, ten patterns, and five attempts per host/pattern pair.

| RID | Framework runtime | Exact | Prefix lost | Mismatch | Timed out | Report outcome |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| win-x64 | .NET 8.0.31 | 92 | 8 | 0 | 0 | Reproduced |
| win-x64 | .NET 9.0.20 | 85 | 15 | 0 | 0 | Reproduced |
| win-x64 | .NET 10.0.12 | 97 | 3 | 0 | 0 | Reproduced |
| win-arm64 | .NET 8.0.31 | 99 | 1 | 0 | 0 | Reproduced |
| win-arm64 | .NET 9.0.20 | 99 | 1 | 0 | 0 | Reproduced |
| win-arm64 | .NET 10.0.12 | 100 | 0 | 0 | 0 | NotReproduced |
| **Total** | **Six reports** | **572** | **28** | **0** | **0** | **Reproduced** |

The following are the only RID/framework/host/pattern cells with prefix loss. Each cell contains five trials, so
its exact count is `5 - Prefix lost`; every unlisted cell was 5 exact, 0 prefix lost, 0 mismatch, and 0 timeout.

| RID | Framework | Host path | Pattern | Prefix lost |
| --- | --- | --- | --- | ---: |
| win-x64 | net8.0 | direct | query-bytewise-0ms | 4 |
| win-x64 | net8.0 | direct | query-split-3 | 1 |
| win-x64 | net8.0 | nested-sample | query-split-6 | 1 |
| win-x64 | net8.0 | nested-sample | query-split-7 | 2 |
| win-x64 | net9.0 | direct | query-bytewise-0ms | 1 |
| win-x64 | net9.0 | direct | query-split-3 | 3 |
| win-x64 | net9.0 | direct | query-split-4 | 2 |
| win-x64 | net9.0 | direct | query-split-5 | 3 |
| win-x64 | net9.0 | direct | query-split-6 | 2 |
| win-x64 | net9.0 | direct | query-split-7 | 1 |
| win-x64 | net9.0 | nested-sample | query-bytewise-0ms | 2 |
| win-x64 | net9.0 | nested-sample | query-split-7 | 1 |
| win-x64 | net10.0 | direct | query-bytewise-0ms | 2 |
| win-x64 | net10.0 | direct | query-split-3 | 1 |
| win-arm64 | net8.0 | direct | query-split-6 | 1 |
| win-arm64 | net9.0 | nested-sample | query-split-5 | 1 |

The 28 prefix losses occurred in zero-delay split or bytewise-query patterns: 21 on the direct path and 7 on the
nested-sample path. No intact-query or 10 ms bytewise trial lost data. One bounded report that did not reproduce
the intermittent behavior does not negate the observations in the other reports.

The matching deterministic probe/runner and interactive-sample tests passed on net8.0, net9.0, and net10.0, and
the full suite passed 330 tests with only the Windows-only classifier skipped on the Linux verifier for each
framework. Managed forwarding therefore remained exact while native trials lost prefixes. Under the approved
decision rule, the result is classified as a **ConPTY/native limitation**, not an Icod.Pty defect. No production
or public-API change is justified by this evidence.

## Contract and coverage

- The sample forwards every byte it receives, including fragmented Unicode, VT, and query replies. A controlled fragmented-host fixture verifies the actual forwarding loop on all six platforms.
- Real direct and nested PTY tests verify fragmented Unicode/arrow input and a complete query reply on every platform. Send a complete terminal reply in one write when possible; this reduces exposure but is not a universal native transport guarantee.
- The strict native test that fragments the query itself remains enabled on Unix. On Windows it is always skipped because the native behavior above prevents a portable success assertion. A separate opt-in Windows classifier runs a fixed ten-pattern matrix over direct and nested-sample paths, five attempts per pattern, and reports `Reproduced`, `NotReproduced`, or `Inconclusive` without converting an environmental outcome into a test failure.
- PR validation creates one privacy-safe JSON report for each Windows RID and target framework and uploads the three reports under a RID-specific artifact. Missing, malformed, unavailable, or incomplete output fails the harness; a completed native classification does not. Exact-head results remain unclassified until both Windows artifacts are available.
- Icod.Pty does not add a VT parser, delay lone Escape keys, synthesize handshake input, or bundle a replacement native ConPTY DLL. Those would require a separate design. Applications that require arbitrary fragmented reply delivery must account for this Windows limitation.

From Windows PowerShell 5.1, run the bounded classifier for all three target frameworks on a specific Windows
build. Run from the repository root after a Release build:

```powershell
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
    $reportPath = Join-Path $PWD "artifacts/conpty-fragmentation-local-$framework.json"
    $env:ICOD_PTY_VERIFY_SPLIT_QUERIES = '1'
    $env:ICOD_PTY_CONPTY_REPORT_PATH = $reportPath
    try {
        dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f $framework --no-build --no-restore --filter FullyQualifiedName=Icod.Pty.Tests.InteractiveSampleTests.ConPty_fragmentation_probe_writes_classified_report
        if ($LASTEXITCODE -ne 0) { throw "Classifier failed for $framework with exit code $LASTEXITCODE." }
    } finally {
        Remove-Item Env:ICOD_PTY_VERIFY_SPLIT_QUERIES -ErrorAction SilentlyContinue
        Remove-Item Env:ICOD_PTY_CONPTY_REPORT_PATH -ErrorAction SilentlyContinue
    }
}
```

Each framework report runs five attempts for each combination of the `direct` and `nested-sample` host paths with
`intact-query`, `query-split-1` through `query-split-7`, `query-bytewise-0ms`, and `query-bytewise-10ms`: 100
trials per report. Record the checked-out commit, `[System.Environment]::OSVersion.Version`,
`$env:PROCESSOR_ARCHITECTURE`, `$Host.Name`, `$PSVersionTable.PSVersion`, framework, and report path.

`Reproduced` means at least one native trial lost or mismatched bytes. `NotReproduced` means all 100 bounded trials
in that report were exact; it does not establish absence of the intermittent limitation. `Inconclusive` means the
run observed timeouts without loss/mutation, and `Unavailable` means the Windows-only native probe could not run.
The diagnostic environment variables affect tests only. Reports contain fixed pattern names, received byte counts,
and aggregate outcomes; they never contain the fixed terminal payload, arbitrary caller input, commands, or
environment-variable values.

Microsoft describes the [pseudoconsole's input translation responsibilities](https://learn.microsoft.com/en-us/windows/console/pseudoconsoles). Its [input state machine](https://github.com/microsoft/terminal/blob/main/src/terminal/parser/stateMachine.cpp) also documents the ambiguity between fragmented escape sequences and individual Escape/Alt keystrokes. These explain why native interpretation is part of the transport; the concrete loss above is established by this repository's recorded tests.
