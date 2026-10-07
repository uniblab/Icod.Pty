# Public contract audit

**Status:** RS02 contract review and RS05/RS07 reconciliation complete; no production change required.

This ledger inventories the public contract at commit
`a7e4f4367b5701eaa5b64e8740f7040eeaa6baf7`, before stabilization implementation. The compatibility baseline
contains 377 entries and 43 exported types. The baseline command passed 317 tests on each of net8.0, net9.0,
and net10.0 on Linux x64.

Disposition meanings:

- `Reviewing`: evidence has not yet been reconciled across implementation, tests, and documentation.
- `Retain`: the current behavior is coherent and adequately supported.
- `Clarify`: behavior is coherent, but documentation needs correction or completion.
- `Correct`: a product defect needs an approved test-first correction before product code changes.
- `Defer`: behavior is coherent, but a broader change belongs to a later milestone.

## Contract groups

All 43 exported types are assigned exactly once below.

| Group | Surface | Ownership | Cancellation | Failure semantics | Platform variance | Disposition | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Process and startup (5) | Mutable `PtyStartInfo` is captured before launch; `PtySize` validates an immutable 1–32767 range; exit, streams, resizing, and capabilities remain distinct. | `PtyProcess` owns the backend after successful startup; failed/cancelled startup cleans partial resources; repeated disposal is harmless. | Pre-cancellation creates no child; cancellation during startup waits for cleanup; I/O/wait cancellation has no lifetime effect. | Validation, unsupported platform, startup I/O, running exit-code access, and disposed access remain distinct; diagnostics contain no payload. | Windows uses ConPTY; Linux/macOS use the managed helper and installed `dotnet`; only the latter report terminal capabilities. | Retain | `PtyStartupTests` (`Precancelled_start_does_not_invoke_factory`, `Cancellation_after_creation_disposes_before_completion`, `Successful_start_transfers_ownership`); `PtyTests` (`Cancelled_wait_and_read_leave_process_usable`, `Dispose_unblocks_pending_read_and_reaps_live_child`); README “Basic use” and “Operational contract”. |
| Control and scope (4) | Target, ownership, capability flags, native signal, ETX interrupt, and dispatch outcome are separate contracts. | Primary ownership is the default; `PlatformScope` must be selected before launch; disposal remains the cleanup owner. | Synchronous native requests have no cancellation seam; ETX cancellation can follow delivery and does not terminate the child. | `Requested`, `TargetUnavailable`, and `DispatchUnconfirmed` do not claim exit; invalid target, unsupported platform, missing opt-in, and native I/O are exceptions. | Windows jobs and Unix initial process groups have deliberately different capabilities; native named signals are Unix-only. | Retain | `PtyOwnershipTests`; `PtyProcessControlTests`; `PrimaryControlTests`; README “Process ownership and explicit control”. |
| Shutdown (3) | Mutable options and request bytes are snapshotted; finite positive grace/escalation deadlines and one active shutdown are enforced. | Shutdown neither closes streams nor disposes the process/session; caller retains lifetime ownership. | Cancellation never initiates escalation and cannot retract delivered input or an issued termination request. | Result separates primary exit, timeout, whether force was dispatched, and the dispatch result; write/native failures propagate unless exit was already collected. | Selected escalation target uses the Windows job or Unix initial group contract; completion still reports primary exit only. | Retain | `PtyShutdownTests` (`Caller_cancellation_never_initiates_force`, `Blocked_request_obeys_grace_budget`, `Write_failure_does_not_escalate`); `PtyScopeShutdownTests`; README “Controlled shutdown”. |
| Session and diagnostics (11) | Options are captured before launch; one ordered writer, one output pump, independent completion tasks, and detached bounded diagnostics expose lifecycle without raw content. | Session exclusively owns its `PtyProcess`; caller streams transfer only after successful startup according to leave-open flags; finalization runs once. | Pre-start cancellation leaves streams with the caller; admitted writes define partial-delivery semantics; cancelling a completion wait does not affect lifetime. | End reason, output status, staged failures, shutdown result, and cleanup exceptions remain distinct; diagnostics omit commands, environment, payload, and exception messages. | Session semantics are common above the backend; ownership and native terminal behavior retain the documented platform variance. | Retain | `PtySessionStartTests`; `PtySessionInputTests`; `PtySessionPumpTests`; `PtySessionCompletionTests`; `PtySessionDiagnosticsTests`; README “Session orchestration”. |
| Terminal configuration (3) | Mutable launch options are captured and validated; null/all-default is a no-op; no live mutation API is implied. | Configuration applies to the newly owned child terminal and creates no independent caller-owned resource. | No separate cancellation contract; invalid/unsupported settings fail before child allocation, while startup cancellation follows process rules. | Invalid combinations/ranges, unavailable capabilities, and native apply/verify failures are distinguished without leaking terminal data. | Linux/macOS x64/ARM64 expose the supported flags; Windows reports `None` and rejects explicit requests before launch. | Retain | `TerminalConfigurationTests` (`Capture_is_detached`, `Invalid_settings_never_reach_factory`, `Windows_explicit_options_fail_before_launch`); terminal integration tests; README “Launch-time terminal configuration”. |
| Recording and replay (9) | Output/resize-only versioned frames are bounded; reader validates structure and limits before allocation; replay preserves event order without timing. | Recording destination transfers only after successful session startup; `LeaveOpen` controls disposal; reader independently owns or leaves its source. | Cancellation of reader/replay propagates; recorder sink cancellation/failure is an independent recording outcome and does not replace session results. | Complete/truncated/stopped/faulted are separate; malformed files throw `PtyRecordingFormatException`; structural errors omit payload. | Pure managed format is portable; captured terminal bytes can reflect platform behavior but the format contract does not vary. | Retain | `PtyRecordingContractTests`; `PtyRecordingWriterTests` (`Sink_failure_is_reported_without_terminal_content`); `PtyRecordingReaderTests`; `PtyRecordingIntegrationTests`; README “Recording and replay”. |
| Automation (8) | Opt-in raw-byte matching is bounded; step factories copy bytes; one expectation and one runner are permitted; no text/screen semantics are implied. | Runner never owns or disposes the caller's session; live output, recording, and process lifetime continue after matcher limit outcomes. | Cancellation propagates, releases waiter/runner gates, preserves the match cursor, and never terminates the session. | Match/timeout/output terminal states/buffer exhaustion are results; invalid use, concurrent use, and input-write failures remain exceptions; results expose no payload. | Managed behavior is common across platforms; native child/terminal behavior remains outside the matcher guarantee. | Retain | `PtyAutomationContractTests`; `PtyAutomationMatcherTests`; `PtyAutomationSessionTests`; `PtyScriptRunnerTests`; README “Live matching and scripted interaction”. |

## RS02 conclusion

No production change required. Source, deterministic tests, XML documentation, design records, and the README
agree for all seven groups. No `Correct`, `Clarify`, or `Defer` disposition was required. Later tasks may improve
release guidance without changing these contracts. `packaging/PublicApiBaseline.txt` remains unchanged.

## RS05 native evidence classification

PR #9 exact-head [run 124](https://github.com/uniblab/Icod.Pty/actions/runs/37655837994) produced six complete
Windows ConPTY classifier reports: 600 bounded trials across win-x64/win-arm64 and net8.0/net9.0/net10.0. The
reports recorded 572 exact deliveries and 28 prefix losses, with no mismatch or timeout; loss appeared on both
architectures and through both direct and nested-sample host paths. Deterministic managed forwarding remained
exact on all three target frameworks.

The approved decision rule therefore classifies the observation as a **ConPTY/native limitation**. The Automation
contract row remains `Retain`: Icod.Pty promises ordered raw-byte forwarding at the managed layer, not arbitrary
fragment preservation after Windows console-input interpretation. No product correction, public-API change, or
compatibility-baseline update is warranted.

## RS07 documentation and package reconciliation

The README lifecycle path now states ownership, disposal, cancellation, deadlines, result boundaries, and
platform variance for process launch/streams/resize, scoped control, shutdown/drain, terminal configuration,
recording/replay, and automation. The sample guide distinguishes bounded noninteractive checks from interactive
host-restoration acceptance. Package guidance matches the exact metadata and asset verifier, complete relocated
helper layout, installed Unix runtime/host requirement, incomplete-layout observations, and informational-only
NativeAOT probe. No documented behavior contradicted source or deterministic tests, so all seven dispositions
remain `Retain` and no product correction was opened.

## Compatibility baseline

- Baseline file: `packaging/PublicApiBaseline.txt`
- Baseline header source: PR #8 runtime head `8c1983cc254745f255fe01a27232d237ef781873`
- Audit capture commit: `a7e4f4367b5701eaa5b64e8740f7040eeaa6baf7`
- Public entries: 377
- Exported types: 43
- Required disposition: satisfied; no row remains `Reviewing` after RS02.
- Change gate: a `Correct` disposition must identify the owning source, failing test, compatibility impact, and
  minimal correction before product code changes.
