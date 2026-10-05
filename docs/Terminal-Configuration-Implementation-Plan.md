# Terminal Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for native execution, or superpowers:subagent-driven-development only if the user selects that method. Steps use checkbox syntax for tracking. Read the spec before implementation; no runtime task is approved by this planning PR alone.

**Goal:** launch children with explicit, verified terminal settings and truthful prelaunch capability discovery, while preserving existing defaults.

**Architecture:** capture settings in the common launch path; apply/read back Unix slave settings before starting either helper path. Expose optional backend capabilities separately from existing process-control capabilities; reject unsupported Windows requests before native launch. Do not add live terminal mutation or change the helper protocol.

**Tech Stack:** C# 13, .NET 8/9/10, existing P/Invoke and managed Unix helper, xUnit, CMD/SH/Windows PowerShell 5.1 tooling.

**Spec:** [Terminal-Configuration-Design.md](Terminal-Configuration-Design.md).

**Status:** TC01-TC09 implementation and hosted acceptance passed on 2026-10-05. Manual Windows laptop observation remains pending. Base: merged PR #4, `2bfeb1f7c183f6b528d45162907ee9260bba60e1`.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.
- Preserve both ownership policies, input/output byte transport, startup cancellation, shutdown, and session completion contracts.
- No new runtime package dependencies, version bump, tag, merge, or publication in this planning PR.

## Review focus

1. A child changes modes immediately: initial configuration must not be described as ongoing enforcement (TC06).
2. Native set succeeds partially: semantic readback must reject an unhonored request before any child runs (TC03-TC04).
3. Raw and custom controls conflict: validation must be deterministic, and ETX must retain its existing API meaning (TC02, TC06).
4. A cancellation or configuration failure occurs while terminal descriptors exist: close all new resources without transferring supplied session streams (TC04, TC06).
5. An unsupported/default request is made on Windows: default must remain a no-op and explicit requests must not touch the parent console (TC05).

## File and interface map

| Files | Responsibility |
| --- | --- |
| New `src/PtyTerminalOptions.cs`, `src/PtyTerminalProfile.cs`, `src/PtyTerminalCapabilities.cs` | Public options, profiles, capability flags from the spec. |
| Modify `src/PtyStartInfo.cs`, `src/PtyProcess.cs` | Add TerminalOptions and static GetTerminalCapabilities. No new live-mode methods. |
| New `src/TerminalConfiguration.cs`; modify `src/LaunchConfiguration.cs` | Immutable parent-only snapshot, scalar/range/combination validation, required-capability calculation. |
| New `src/Unix/UnixTerminalConfiguration.cs`, `src/Unix/UnixTerminalNative.cs` | Read/transform/apply/verify with platform-specific termios layouts; no public native masks. |
| Modify `src/Unix/UnixBackend.cs` | Configure the owned slave inside its cleanup region, before both child-launch paths. |
| New `src/Tests/Icod.Pty.Tests/TerminalConfigurationTests.cs`, `UnixTerminalConfigurationTests.cs`, `TerminalConfigurationIntegrationTests.cs` | Managed validation, fault injection, native behavior, startup/session acceptance. |
| Modify `src/Tests/Icod.Pty.TestChild/Program.cs`; new `src/Tests/Icod.Pty.TestChild/TerminalConfigurationProbe.cs` | Native first-state report and byte-level child probes, independent of production transformation code. |
| New `src/Sample/TerminalConfigurationSmokeChecks.cs`; modify `src/Sample/Program.cs` | New public verification mode and internal native-reading child mode. |
| Modify `src/Tests/Icod.Pty.Tests/PackageSmokeTests.cs`, `packaging/VerifyPackageConsumer.ps1` | Include new mode in test matrix and both package-consumer lists. |
| Modify `README.md`, `samples/README.md`, `ROADMAP.md`, these two planning documents | Document actual support, examples, evidence, and deferred portions. |

The library captures terminal options under `#if !PTY_HELPER` and does not serialize them into helper JSON. Confirm `tools/Icod.Pty.Host/Icod.Pty.Host.csproj` still compiles its shared LaunchConfiguration without acquiring a dependency on the new parent-only types. Do not change sample host-console adapters as part of this feature.

## Execution rules and commands

For each task: add the named regression first, run it and record RED, implement the smallest change, run GREEN plus affected regressions, inspect the diff, then commit. A planning checklist is not test evidence. TC01 is an exploratory gate with explicit pass/fail observations, not a production implementation step.

Use the following filtered command, replacing FILTER with the named test class/method; repeat for net8.0, net9.0, and net10.0:

```text
dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net10.0 --filter FILTER
```

Expected RED is the specified assertion failure (or absent proposed API for initial surface tests), not an unrelated tooling error. Expected GREEN is zero failed tests and no new skips. Unix-specific behavioral assertions have explicit Windows capability/rejection counterparts.

## Task 1: TC01 native feasibility and support matrix

**Files:** new test-child probe and UnixTerminalConfigurationTests; this spec's evidence section.
**Interfaces:** native probe modes report initial semantic fields and raw bytes; do not publish public API yet.

- [x] Add a C# probe that allocates a fresh PTY, reads/modifies/applies/readbacks slave state before launch, and reports the fields seen by a managed child before that child changes modes.
- [x] Verify Linux/Darwin structs, native constants, cc indices, disabled value lookup, and cfmakeraw transformation on Unix x64 and ARM64; record exact layouts and authoritative platform references in the design.
- [x] Prove initial noncanonical/no-echo input, Raw VMIN=1/VTIME=0, configured VINTR/VEOF/VERASE, and no-op baseline preservation through both ownership policies. Configured ownership-path integration remains explicitly covered by TC04/TC06 because TC01 has no production request surface.
- [x] Confirm Windows has no termios endpoint in the existing ConPTY contract and exercise the unchanged default path without AttachConsole/host mutation. The frozen explicit-request rejection is executable in TC02/TC05 after request types exist.
- [x] Record the capability matrix and evidence links. The gate passed on all six OS/architecture jobs in [workflow run 53](https://github.com/uniblab/Icod.Pty/actions/runs/37350262099); freeze the spec's public names/values.
- [x] Commit `test: prove initial terminal configuration feasibility` (probe checkpoint `7e2381484bb052ac497cd67376d1d1f936aef885`; evidence follow-up records the final gate result).

## Task 2: TC02 options, capture, and prelaunch capabilities

**Files:** public types, TerminalConfiguration, PtyStartInfo, PtyProcess, LaunchConfiguration, TerminalConfigurationTests.
**Interfaces:** `PtyProcess.GetTerminalCapabilities()` and public members exactly as in the spec. Internal `TerminalConfiguration? Capture(PtyTerminalOptions? options, PtyTerminalCapabilities capabilities)` returns an immutable snapshot or null for a no-op; `RequiredCapabilities` reports the requested flags.

- [x] Add tests named `Default_options_are_noop`, `Capture_is_detached`, `Invalid_settings_never_reach_factory`, and `Capabilities_are_side_effect_free`. Pin enum numeric values and DisabledCharacter=-1.

```csharp
Assert.Null(TerminalConfiguration.Capture(null, PtyTerminalCapabilities.None));
Assert.Null(TerminalConfiguration.Capture(new(), PtyTerminalCapabilities.None));
Assert.Throws<ArgumentException>(() => TerminalConfiguration.Capture(
    new() { Profile = PtyTerminalProfile.Raw, Echo = false }, all));
Assert.Throws<ArgumentException>(() => TerminalConfiguration.Capture(
    new() { MinimumReadBytes = 1 }, all));
Assert.Throws<PlatformNotSupportedException>(() => TerminalConfiguration.Capture(
    new() { Echo = false }, PtyTerminalCapabilities.None));
```

Here `all` is the union of the six individually defined capability flags. Cover character bounds -2/-1/0/255/256, timing -1/0/255/256, undefined profiles, every Raw/override pairing, CanonicalInput=true with timing, and mutation after capture. Platform-disabled-byte rejection is additionally covered in TC03.

- [x] Run the `TerminalConfigurationTests` filter and retain RED evidence. Run 55 failed on all TFMs only for the intentionally absent public/internal contract types.
- [x] Implement capture and validation; add prelaunch capability discovery for the verified OS/architecture matrix. Process and session startup reuse `LaunchConfiguration.Capture`; no duplicate session property exists.
- [x] Run the contract and affected regressions on all TFMs; compile the helper and solution with zero warnings/errors and unchanged helper JSON. [Run 56](https://github.com/uniblab/Icod.Pty/actions/runs/37352587915) passed all six jobs after a Windows ARM64 rerun isolated two different pre-existing net10 timing flakes; the final attempt passed tests and package consumers.
- [x] Commit `feat: define initial terminal options and capabilities` (`f373c91fb2769fa612c98dfe8a4f759b3e68cbcc`).

## Task 3: TC03 native transformation and verified application

**Files:** UnixTerminalConfiguration, UnixTerminalNative, UnixTerminalConfigurationTests.
**Interfaces:** `UnixTerminalConfiguration.Apply(int slaveFd, TerminalConfiguration configuration) : void`; an internal test seam substitutes native get/set/readback operations, never a public hook. Define explicit Linux/Darwin termios structs from TC01.

- [x] Add failing `Preserve_changes_only_requested_fields`, `Echo_off_clears_newline_echo`, `Raw_sets_verified_masks_and_read_timing`, `Disabled_character_is_not_a_literal_byte`, and `Partial_native_success_fails_readback` tests. Assert unrelated fields/speeds/cc entries survive Preserve; ignore ABI padding in semantic comparisons.
- [x] Run `UnixTerminalConfigurationTests` and record RED. [Run 58](https://github.com/uniblab/Icod.Pty/actions/runs/37354423945) failed because the Unix adapter contract did not exist.
- [x] Implement read-modify-TCSANOW-readback using the original native state. Resolve native disabled-character encoding; reject literal bytes colliding with it. Raw uses native cfmakeraw plus VMIN=1/VTIME=0; compare all fields defined by that transformation.
- [x] Inject get, set, and readback native errors independently; assert IOException identifies the failed operation and preserves the native error where present. Assert a simulated successful-but-unapplied field raises IOException, without returning success or writing terminal-content data.
- [x] Run tests on all four Unix platforms and all TFMs; confirm no production syscall occurs for null/default requests. [Run 60](https://github.com/uniblab/Icod.Pty/actions/runs/37355421340) passed all six jobs, package checks, and all target frameworks after correcting fixed-buffer marshalling diagnosed by run 59.
- [x] Commit `feat: apply and verify Unix terminal configuration` (`631cca0c4fb162b6a8e3434d884f297f6a494f47`, fix `3453686d01005dc5c4b056ab458139706ce6bd2c`).

## Task 4: TC04 startup integration and resource rollback

**Files:** UnixBackend, TerminalConfigurationIntegrationTests, existing UnixStartupTests/UnixLifetimeFaultTests as needed.
**Interfaces:** consume the captured configuration and TC03 Apply on the existing owned slave descriptor, before either launch path.

- [x] Add `Configuration_precedes_both_launch_paths`, `Configuration_failure_never_launches_child`, `Cancelled_configuration_releases_descriptors`, and `Session_configuration_failure_leaves_streams_open`. Use startup factory/operation counters and a child marker to prove order, not a delay-based inference.
- [x] Run the new tests to RED.
- [x] Insert Apply inside the existing cleanup boundary, add cancellation checks surrounding configuration, and preserve startup exception/cleanup handling. Do not extend slave lifetime or the helper wire format.
- [x] Inject failure before get, at set, after set/readback, and immediately before launch. Verify descriptors return to the warmed baseline across repeated attempts, no new helper/child survives, and supplied session streams remain open.
- [x] Run the new tests plus existing startup, ownership, and session-start filters on every TFM; verify no changes to PrimaryProcess/PlatformScope defaults.
- [x] Commit `feat: configure child terminals before launch`. Expected RED is [run 61](https://github.com/uniblab/Icod.Pty/actions/runs/37356263091); startup integration, rollback, platform behavior, and package consumers passed in [run 67](https://github.com/uniblab/Icod.Pty/actions/runs/37358836220).

## Task 5: TC05 Windows rejection and unchanged default behavior

**Files:** TerminalConfigurationTests, TerminalConfigurationIntegrationTests; common validation only if a defect is exposed.
**Interfaces:** None capabilities on Windows; null/all-default configuration is legal.

- [x] Add `Windows_explicit_options_fail_before_launch` for each capability family, `Windows_default_options_preserve_launch`, and `Rejected_options_do_not_change_host_console`. Assert no child marker/factory call for rejection and identical host mode/code-page snapshots before/after in the existing terminal fixture.
- [x] Run tests to RED before adding missing rejection checks; if TC02 already supplies the behavior, record these as additional coverage rather than claiming a new defect.
- [x] Verify `PtyProcess.Start`, `StartAsync`, and `PtySession.StartAsync`; verify both ownership policies and caller-owned session streams after rejection.
- [x] Run on Windows x64 and ARM64 for all TFMs, with no skips substituting for unsupported-request assertions. Keep the existing ConPTY fragmented-query exclusion unchanged.
- [x] Commit `test: verify Windows terminal option boundaries`. Run 67 passed Windows x64/ARM64 on net8.0/net9.0/net10.0, including Windows PowerShell 5.1 package verification on x64.

## Task 6: TC06 native behavioral and compatibility acceptance

**Files:** TerminalConfigurationIntegrationTests; test-child Program and TerminalConfigurationProbe.
**Interfaces:** child modes report initial semantic state, acknowledge receipt of byte sequences, and deliberately change their own modes when directed. Native reads avoid managed console line buffering.

- [x] Add `Child_first_state_matches_request`, `Canonical_waits_for_delimiter`, `Noncanonical_reads_without_delimiter`, `Echo_off_emits_no_input_echo`, `Custom_control_characters_take_effect`, `Raw_ETX_is_data`, and `Child_may_change_initial_configuration`.
- [x] Include VMIN/VTIME combinations (0,0), (0,1), (1,0), and (2,1), using synchronization and broad watchdog bounds rather than exact scheduler timing. Separate terminal zero-length reads from transport EOF expectations.
- [x] Add an ETX regression: with custom VINTR or Raw, SendInterruptAsync still sends byte 3; native SendSignal remains independent. Verify canonical EOF is not a promised portable half-close.
- [x] Run RED for each missing behavior; implement only spec-conforming fixes. Repeat through both ownership policies and session/process entry points, with child-side first-state reporting before any self-configuration.
- [x] Run all new native cases and existing session shutdown/output/drain tests on six platforms/three TFMs. Record any timing corrections as test changes, not silent product-default changes.
- [x] Commit `test: verify terminal modes and lifecycle compatibility`. Run 65 exposed prefix-only report reads; the synchronized complete-report correction and serialized descriptor accounting passed in run 67.

## Task 7: TC07 example and exact-package consumers

**Files:** TerminalConfigurationSmokeChecks, sample Program, PackageSmokeTests, VerifyPackageConsumer.ps1.
**Interfaces:** `--terminal-config-smoke` invokes `TerminalConfigurationSmokeChecks.RunAsync() : Task<int>` and prints `PTY terminal configuration smoke check passed.`; an internal `--terminal-config-child` mode supplies the native-reading fixture. Public interactive sample defaults are unchanged.

- [x] Add `--terminal-config-smoke` to PackageSmokeTests and both verifier mode lists, run it to RED before implementing the switch.
- [x] Implement the Unix check with a child native state/byte acknowledgement for no-echo noncanonical input, then graceful completion. Bound the parent wait and ensure cleanup even on failed assertions.
- [x] Implement the Windows check as capability=None, explicit-request rejection before child creation, and a successful null/default session launch. Print success only after these assertions, not after a skip.
- [x] Verify fresh package consumers on net8.0/net9.0/net10.0 plus published net10.0: eight modes, 32 invocations per platform. Windows x64 artifact and consumer verification must run under Windows PowerShell 5.1.
- [x] Commit `feat: demonstrate initial terminal configuration`. Run 67 passed eight modes across each fresh net8.0/net9.0/net10.0 consumer and published net10.0 output: 32 invocations on every platform job.

## Task 8: TC08 documentation and public API closure

**Files:** README, samples README, public XML documentation, ROADMAP, this design and plan.

- [x] Add a self-contained capability-gated example and a platform table; distinguish Preserve from host inheritance, noncanonical from Raw, and initial from live state. Show explicit Windows unsupported handling.
- [x] Document exclusive Raw rules, disabled/literal character semantics, VMIN/VTIME units, fixed ETX behavior, verified readback, failed-start ownership, and the retained Unix helper/runtime requirement.
- [x] Add CMD/SH/PowerShell 5.1 commands for the new mode. Keep interactive host restoration and prior laptop observations separate; do not infer success on untested frameworks from net10.0 evidence.
- [x] Compile documentation examples as temporary package consumers on all TFMs and run appropriate platform branches. Check XML docs build without warnings, local links resolve, and the menu records completed versus remaining portions of options 4 and 7 truthfully.
- [x] Commit `docs: explain terminal configuration capabilities and limits`.

## Task 9: TC09 final verification and review

**Files:** implementation evidence in this plan and status in ROADMAP/design; no unrelated workflow changes.

- [x] Run full Release build/test/pack and exact-package checks below; record exact head SHA, OS/architecture/framework matrix, counts, failure fixes, warning counts, and workflow URLs.
- [x] Review public API compatibility, null/default native paths, every required capability bit, Raw masks, helper/package layout, both ownership paths, fault cleanup, and the five review-focus cases. Resolve important findings with RED/GREEN regressions.
- [x] Verify all six CI jobs pass on the final runtime head, with 32 package invocations per job and no new skip hiding an unsupported/failed test. Retain the existing native ConPTY limitation documentation.
- [x] Request manual Windows laptop results for the new smoke mode and preserve still-unreported interactive observations as pending. Never promote hosted CI to laptop evidence.
- [x] Mark only evidenced tasks complete; report merge readiness without merging, selecting a version, tagging, or publishing automatically.
- [x] Commit `docs: record terminal configuration acceptance`.

Final runtime head `e9c81ea7b789918f5f1f6e0057d977d3deb17545` passed [workflow run 68](https://github.com/uniblab/Icod.Pty/actions/runs/37359837005) on Windows x64/ARM64, Linux x64/ARM64, and macOS x64/ARM64. Each job built with zero warnings/errors, ran 229 tests on each of net8.0, net9.0, and net10.0, verified the exact package, and completed eight modes across three fresh framework consumers plus published net10.0 output (32 invocations). The first Windows ARM64 attempt timed out only in the pre-existing `--scope-smoke`; its failed-job rerun passed tests and package verification without a product or timeout change. Runs 62-66 diagnosed and corrected test-fixture syntax, prefix-only synchronization, and parallel descriptor-accounting defects before this final run.

## Final commands

```text
dotnet build Icod.Pty.sln -c Release
dotnet test Icod.Pty.sln -c Release --no-build
dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --terminal-config-smoke
```

From PowerShell (Windows PowerShell 5.1 for the Windows x64 acceptance host):

```powershell
./packaging/VerifyPackageArtifact.ps1 -ArtifactDirectory artifacts -Configuration Release
./packaging/VerifyPackageConsumer.ps1 -ArtifactDirectory artifacts
```

## Evidence and deferred work

Planning baseline: PR #4 merged on 2026-10-05. Its final head `17abff97b1d1a534f443d33bc108ff6e1b3b941e` passed [six-platform run 51](https://github.com/uniblab/Icod.Pty/actions/runs/37342768776). The user reported Windows x64 Release net10.0 success for both session smoke modes. This is baseline evidence, not evidence for the proposed terminal configuration feature.

TC01 passed in [six-platform workflow run 53](https://github.com/uniblab/Icod.Pty/actions/runs/37350262099). The pure-C# probe verified Linux/Darwin layouts and constants, disabled-byte values, Preserve/custom/Raw transformations, semantic readback, managed-child first state, and unchanged default ownership paths on net8.0/net9.0/net10.0. Windows confirmed the no-termios/default boundary; explicit request rejection remains an executable TC02/TC05 acceptance item.

No production terminal-configuration implementation, new public API, or terminal-config smoke mode exists at the end of TC01. Live read/update/restoration, serial-port controls, native Windows child shims, general tracing/exporters, transcript recording, foreground retargeting, and the separate ConPTY investigation remain deferred.
