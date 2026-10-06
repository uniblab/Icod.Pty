# Deployment portability and focused compatibility hardening

**Status:** accepted and implemented in [PR #6](https://github.com/uniblab/Icod.Pty/pull/6). Framework-dependent, self-contained, single-file self-contained, and trimmed self-contained consumers are qualified below. NativeAOT remains a feasibility result.

**Decision, 2026-10-06:** qualify real deployment forms for the existing PTY package, correct demonstrated packaging/runtime-discovery defects, and pin a narrow compatibility baseline. Keep version selection and release work separate.

## Intent and boundary

An application developer should know which published forms of an Icod.Pty consumer can launch, exchange bytes, resize, shut down, and clean up a child on each supported OS/architecture, and exactly which external helper assets and runtime are needed. A published application must be tested from its final output directory, rather than inferred from a successful build or a test-project reference.

Current baseline: the one NuGet package contains net8.0/net9.0/net10.0 AnyCPU libraries and a framework-dependent net8.0 managed Unix helper with LatestMajor roll-forward. `buildTransitive/Icod.Pty.targets` copies its DLL, deps.json, and runtimeconfig.json to `Icod.Pty.Host/` and excludes those files from the consumer's single-file bundle. Unix launches the helper with a `dotnet` host; an installed compatible .NET runtime is therefore required even if the consumer itself is self-contained. Windows ConPTY does not launch this helper. The existing ordinary package-consumer and published net10.0 checks remain the baseline.

Considered approaches:

1. **Evidence-first package qualification (selected).** Publish an actual fresh NuGet consumer in each deployment form, run the packaged smoke modes, test failure boundaries, and fix only proven defects. This preserves the existing helper architecture and produces an honest support matrix.
2. **Immediately ship a RID-specific self-contained helper.** Could remove a Unix runtime prerequisite, but introduces per-RID assets, selection and rollback, package size, and a new process-launch contract. Investigate only if evidence and a separate reviewed design justify it.
3. **Run post-fork managed code or emulate PTY behavior in the consumer.** Changes safety and native lifetime assumptions; outside this milestone.

## Scope and contracts

- The initial publication forms under test are ordinary framework-dependent, self-contained, single-file self-contained, and trimmed self-contained. Use a fresh consumer that references only the newly packed local Icod.Pty NuGet package; publish for its actual OS/architecture RID and run from the output directory. Exercise net8.0, net9.0, and net10.0, each across Windows/Linux/macOS x64 and ARM64, subject to explicit feasibility gates for the more expensive publish modes. Do not call a mode supported merely because compilation succeeds.
- The acceptance path for each qualified form performs an ordinary process/session smoke, both primary-only and owned-scope lifecycle checks where supported, startup cancellation, terminal-configuration smoke, exit observation, and cleanup. Reuse the eight existing sample modes as the first executable surface; add a focused fixture only for a behavior those modes cannot observe.
- On Unix, published output retains the external `Icod.Pty.Host/` directory with the three helper files. Verify that both ownership launch paths find it from a final published output tree. Validate a working `dotnet` host and a compatible installed runtime separately from the consumer's bundled runtime. The current helper/host contract remains until a distinct feasibility result and review authorize a change.
- A missing helper, missing host, incompatible helper runtime, or broken explicit `DotNetHostPath` must produce a bounded, identifiable startup failure, leave no child or owned PTY resource, and not write terminal content into diagnostics. Preserve the explicit host-path override and its current semantics; do not silently substitute the consumer's apphost or modify its console.
- For a single-file consumer, the library may be bundled while helper assets remain external. Test launching directly from the published executable after moving/copying the complete output directory, not just `dotnet <consumer.dll>` from the build directory.
- For trimming, treat warnings and observed behavior as evidence. Preserve necessary runtime dependencies only where a regression proves they are needed; do not blanket-disable analysis or declare the package trim-safe based on a warning suppression. A failed trim gate is recorded as unsupported with a concrete reason until a targeted fix passes.
- NativeAOT is a bounded feasibility investigation on all six platform jobs where toolchains permit it. Record toolchain availability, build diagnostics, and actual process/session startup, including whether the managed Unix helper still needs `dotnet`. Passing a compile alone does not establish NativeAOT support; an unproven mode stays outside the support claim. Broader Unix distributions such as musl remain an investigation, not an automatic expansion of the Linux support contract.
- This milestone has no new public API by default. If an evidenced defect requires one, revise this design before implementing it. Preserve byte transport, launch snapshotting, scope ownership, exit/output separation, and caller-stream ownership.

## Focused option 13

Pin the public API at merged PR #5 across all three target frameworks with a deterministic, reviewed snapshot or equivalent metadata comparison. Reject unintentional removals/signature changes while allowing explicitly reviewed additive changes. Run targeted repeated lifecycle acceptance on published artifacts: cancellation before/during startup, missing helper/host, primary exit with descendant still present, output drain, and disposal. Keep repetition bounded with per-case watchdogs and explicit failure reporting; never turn a timeout into a skip.

Maintain a support table with separate columns for build, publish layout, runtime start, and full behavior. Mark each OS/architecture/framework/deployment combination supported only after its published consumer passes. Preserve known Windows ConPTY fragmented-query exclusion. Record runner images and runtime/toolchain prerequisites, and separate automated CI from the user's Windows 10.0.26200.9457 laptop evidence.

## Qualification result

Each `Verified` entry applies to every cross-product of the three TFMs and four deployment forms in that row. The verifier creates a fresh project, restores only the exact locally packed NuGet artifact from an isolated package cache, publishes for the target RID, and invokes the final apphost. Full behavior covers process and session startup, primary-only and owned-scope paths, cancellation, interrupt, terminal configuration, invalid-host behavior, output drain, exit observation, and cleanup.

| Runner label / RID | TFMs | Deployment forms | Build | Published layout | Runtime start | Full behavior | Runtime prerequisite |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `windows-latest` / `win-x64` | net8.0, net9.0, net10.0 | Framework-dependent, self-contained, single-file, trimmed | Verified | Verified | Verified | Verified | Target runtime for framework-dependent applications |
| `windows-11-arm` / `win-arm64` | net8.0, net9.0, net10.0 | Framework-dependent, self-contained, single-file, trimmed | Verified | Verified | Verified | Verified | Target runtime for framework-dependent applications |
| `ubuntu-latest` / `linux-x64` | net8.0, net9.0, net10.0 | Framework-dependent, self-contained, single-file, trimmed | Verified | Verified | Verified | Verified | Usable `dotnet` host and compatible installed runtime for the external net8.0 helper |
| `ubuntu-24.04-arm` / `linux-arm64` | net8.0, net9.0, net10.0 | Framework-dependent, self-contained, single-file, trimmed | Verified | Verified | Verified | Verified | Usable `dotnet` host and compatible installed runtime for the external net8.0 helper |
| `macos-26-intel` / `osx-x64` | net8.0, net9.0, net10.0 | Framework-dependent, self-contained, single-file, trimmed | Verified | Verified | Verified | Verified | Usable `dotnet` host and compatible installed runtime for the external net8.0 helper |
| `macos-latest` / `osx-arm64` | net8.0, net9.0, net10.0 | Framework-dependent, self-contained, single-file, trimmed | Verified | Verified | Verified | Verified | Usable `dotnet` host and compatible installed runtime for the external net8.0 helper |

On Unix, the supported published layout includes `Icod.Pty.Host.dll`, `Icod.Pty.Host.deps.json`, and `Icod.Pty.Host.runtimeconfig.json` under `Icod.Pty.Host/`. Relocating the complete output is verified. Removing the helper DLL produces a bounded failure without leaked child or PTY resources. Removing either metadata file can still run when the installed host supplies enough context, so that runtime observation does not expand the contract: the verifier requires all three files before recording a supported layout. Windows does not use the helper, and its positive paths remain successful with missing helper assets or an invalid `DotNetHostPath`.

No product or public API change was required. The option 13 gate pins 252 public entries from merged PR #5 for each target framework and repeats the lifecycle cases above on published artifacts. Trimmed publishing emitted no warnings that required suppression or product annotations. The only expected test skips are the existing fragmented ConPTY terminal-query reproducer, once per TFM in each Windows job.

The net10.0 NativeAOT probe built and executed the same nine smoke modes on all six target RIDs. The Windows x64 probe emitted eight IL3000 `Assembly.Location` warning lines from four unique sample call sites, once during analysis and once during native compilation; these warnings were retained. NativeAOT is not promoted into the support table because the probe is limited to net10.0 and the current hosted runner images. The Unix helper still requires the external three-file layout, `dotnet`, and a compatible installed runtime. Musl and other unlisted Unix environments remain untested.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU library assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64; Windows floor 10.0.26200.9457.
- One LGPL-3.0-or-later NuGet library package; no new runtime package dependencies or checked-in native helper binary. NativeAOT consumer builds are feasibility probes.
- CMD/SH/Windows PowerShell 5.1-compatible tooling; C# for fixtures, no C or Python.
- Root solution/library project; C# sources under `src/`; retain existing package and helper boundaries.
- No version bump, tag, merge, or publication in this milestone. Support claims require the evidence recorded in the implementation plan.

## Acceptance and stopping rules

Gate A establishes the current exact-package and API baseline on all six platforms. Gate B proves ordinary and self-contained final published output. Gate C proves single-file layout and startup with external helper assets. Gate D probes trimming, applies only targeted fixes, and records supported/unsupported outcomes by platform. Gate E records NativeAOT and wider-Unix feasibility without claiming unsupported forms. Gate F closes compatibility, stress, documentation, and review.

A gate may report a concrete limitation instead of a code change. Do not erase failed evidence, relax cleanup contracts, disable warnings indiscriminately, or describe an unrun combination as supported. Preserve successful ordinary consumers while extending coverage.
