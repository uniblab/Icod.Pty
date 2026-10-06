# Recording and replay development roadmap

**Status:** proposed execution plan for the focused option 8 milestone selected on 2026-10-06. Planning PR only; no tranche is complete yet. Read the [design](Recording-Replay-Design.md) and [main roadmap](../ROADMAP.md) first.

## Constraints and baseline

- Base implementation on merged PR #6, `main` at `51d69960dcc860bb84452b483d2621fc2126e387`. Preserve the 252-entry public API baseline except deliberate, reviewed additive signatures. Keep one LGPL-3.0-or-later NuGet package, C# 13, net8.0/net9.0/net10.0 AnyCPU, and Windows/Linux/macOS x64/ARM64.
- Preserve existing raw `PtyProcess`, process/session lifetime, Unix external managed helper and installed `dotnet` prerequisite, terminal configuration, package-consumer gates, and old caller defaults. Windows minimum build remains 10.0.26200.9457. Keep CMD/SH/PowerShell 5.1-compatible tooling, no C/Python, and C# sources under `src/`.
- Work on a feature branch in reviewable tranches. Do not dispatch subagents for this Icod.Pty work. Use actual behavior tests before production changes where applicable, record RED then GREEN with command/output and commit SHA. Discovery or documentation stages can record observations without invented RED. Never commit recorded terminal payload fixtures containing secrets.
- No version bump, tag, merge, package publication, input capture, timed playback, output matching, screen model, or generic observer/exporter in this milestone.

## Review focus

1. A destination accepts one output chunk, then the recorder sink throws. The live output pump and `OutputCompletion` retain their native result; `RecordingCompletion` reports a fault with no payload text. Test in RR03/RR05.
2. Continuous output or resize storms reach `MaxBytes`. At most one truncation marker appears within the cap, the session keeps running, and replay reports a valid captured prefix. Test in RR02/RR03/RR06.
3. An output destination partially writes and throws, or native resize throws. The recorder does not claim an unconfirmed complete chunk or failed resize; the existing session error path remains authoritative. Test in RR03/RR04.
4. An adversarial header or length advertises a huge allocation, goes backward in time, omits a terminator, or trails data. The reader rejects it within its own size limits and does not expose terminal content in error text. Test in RR02/RR06.
5. Session startup fails after options capture, or drain times out while the recording sink is slow. Stream ownership, cancellation, bounded work, and all completion tasks settle according to their documented contracts. Test in RR01/RR05/RR07.

## File and interface map

| Path | Planned responsibility |
| --- | --- |
| `src/PtySessionOptions.cs`, `src/Session/SessionConfiguration.cs` | Optional recording options; snapshot/validation before launch, ownership capture. |
| `src/PtySession.cs`, `src/Session/SessionCoordinator.cs`, `src/Session/SessionPumps.cs` | Integrate successful output and resize observations; independent result/finalization without changing old paths. |
| `src/PtyRecordingOptions.cs`, `src/PtyRecordingResult.cs`, `src/PtyRecordingEvent.cs` (new) | Small public configuration, outcome, and event contracts. Final signature names are frozen in RR01. |
| `src/Recording/RecordingFormat.cs`, `src/Recording/SessionRecorder.cs`, `src/PtyRecordingReader.cs` (new) | Portable bounded format, serialized writer, validating streaming reader/replay. |
| `src/Tests/Icod.Pty.Tests/PtyRecordingTests.cs`, `PtyRecordingReaderTests.cs`, `PtyRecordingIntegrationTests.cs` (new) | Contract, malformed input, fault/limit/lifecycle and native round-trip tests. |
| `packaging/PublicApiBaseline.txt`, `.github/workflows/pull-request.yaml` | Review intentional additive API; retain six jobs/three TFMs and run package-consumer round-trip. |
| `src/Sample/Program.cs`, `src/Sample/RecordingSmokeChecks.cs` (new), `packaging/VerifyPackageConsumer.ps1` | Small opt-in smoke mode run by a consumer installed from the exact packed package; avoid repository-only integration evidence. |
| `README.md`, `samples/README.md`, `ROADMAP.md`, these documents | Usage, format/security/limits, completion semantics, supported matrix, and tranche evidence. |

## Gates and sequence

| Gate | Required evidence |
| --- | --- |
| A | Approved format/API contract, golden fixture, option validation, and focused net8.0 tests. |
| B | Writer/reader unit tests for ordering, caps, corruption, and fault independence across all three TFMs. |
| C | Native session round-trip and lifecycle tests on all six runner targets, with package-consumer smoke from the exact NuGet artifact. |
| D | Final six-platform/three-framework build, test, pack, exact-artifact and published-consumer regression; documentation and self-review at the final head. |

Each tranche is a reviewable commit. In an execution PR, append an evidence table with tranche, head SHA, test command, expected/actual result, platform/TFM, and CI URL. A green earlier head does not qualify a changed final head. Hosted CI is distinct from Windows laptop acceptance.

### RR01: freeze the public contract and format (Gate A)

**Files:** this design/plan, proposed public type skeletons and format fixture/test sources; API baseline only when additive signatures are settled.

- [ ] Inspect merged `PtySession` output/resize/finalization, stream ownership, public API baseline, and sample/package-consumer paths. Confirm the exact format field widths, header initial size, terminal markers, cap minimum/default, timestamp source and overflow behavior, reader limits, exception types, and public method signatures. Document them in the design before production writer code.
- [ ] Create hand-authored golden bytes for empty output plus one output and resize frame, and format tests that decode them. Assert endian, bounds and version handling. A fixture test against the absent reader should be RED; implement only enough reader surface for the next tranche.
- [ ] Add validation tests for null/nonwritable/aliased recording stream, minimum cap, captured option mutation, and failed-start ownership. Run RED against existing code, then add captured options and public result shape; run GREEN.
- [ ] Record the baseline public signature diff as intentional additions only; commit `test: define bounded recording contract`.

### RR02: streaming codec and hostile-input validation (Gates A–B)

**Files:** `src/Recording/RecordingFormat.cs`, `src/PtyRecordingReader.cs`, `src/PtyRecordingEvent.cs`, reader tests.

- [ ] Add fixtures for NUL/invalid UTF-8, empty frames, a 16 KiB boundary, multiple resizes, a valid truncated prefix, and complete EOF. Add malformed fixtures for magic/version/kind, size/length, timestamp regression, missing marker, and trailing bytes; verify each RED independently.
- [ ] Implement exact byte layout and incremental reads with fixed independent frame and total-input quotas. Reject lengths before allocating; make returned payload ownership explicit; distinguish valid truncation from damaged EOF.
- [ ] Add replay-to-`Stream` tests for exact byte order, cancellation, leave-open defaults, and resize visibility through event iteration. Run targeted tests on net8.0/net9.0/net10.0 and commit `feat: add validated recording reader`.

### RR03: bounded session recorder (Gate B)

**Files:** `src/Recording/SessionRecorder.cs`, `src/PtyRecordingOptions.cs`, `src/PtyRecordingResult.cs`, writer/fault tests.

- [ ] Test header/initial size, output split at 16 KiB, byte/count accounting, monotonic timestamps, serialized concurrent output/resize admission, and a complete terminal marker. Test exact-cap boundaries and sustained output/resize after truncation; verify RED before implementing.
- [ ] Implement direct bounded writes to a supplied stream with a gate separate from the session coordinator. Reserve terminal-marker bytes, stop at the cap, and keep the live session path functioning. A sink exception records a fault and disables capture without reclassifying a successful destination write.
- [ ] Test write, flush, and owned-stream disposal failures separately; settle one result and protect the original session failure. Rerun codec fixtures and commit `feat: add bounded session recorder`.

### RR04: output and resize integration (Gate B)

**Files:** `src/PtySession.cs`, `src/Session/SessionCoordinator.cs`, `src/Session/SessionPumps.cs`, integration unit tests.

- [ ] Test that output is recorded after destination write success, failed/partially-written chunks are not claimed, native resize failures are omitted, and concurrent resize/output records preserve admitted order. Verify RED before integration.
- [ ] Wire the recorder into the sole output pump and successful `PtySession.Resize` path. Keep `PtyProcess` raw streams and disabled-session hot path unchanged; do not hold coordinator locks while calling user streams.
- [ ] Compare session result/output status/diagnostics for recording disabled, successful capture, and recorder fault. Run focused session pump/resize tests on three TFMs; commit `feat: integrate session recording`.

### RR05: shutdown, ownership, and completion (Gate B)

**Files:** coordinator/configuration/options and lifecycle tests.

- [ ] Add cases for startup failure before ownership transfer, ordinary EOF, explicit shutdown, output destination failure, sink failure, primary exit with descendant-held output, drain timeout, startup cancellation, and repeated/concurrent disposal. Check all three completion tasks and stream leave-open behavior; verify relevant cases RED.
- [ ] Finalize one complete/truncated/stopped/faulted marker/result under each path, flush and dispose an owned stream once, and keep recorder faults independent of session failure staging. Document how slow/uncooperative caller streams affect drain/disposal.
- [ ] Run existing session ownership, input, completion, and shutdown suites with the new cases; commit `test: cover recording lifecycle and ownership` (or `fix:` for a reproduced defect).

### RR06: portable replay and package consumer (Gate C)

**Files:** `src/Sample/RecordingSmokeChecks.cs`, sample dispatcher, exact-package verifier, native tests.

- [ ] Add an opt-in `--recording-smoke` that starts a session from the installed package, writes binary output and resizes, reads the resulting record, and compares output/event order and final status. Do not print captured content. Include a valid capped-prefix and malformed-file negative check.
- [ ] Execute the sample and native integration tests across all six CI jobs and three TFMs. Retain published deployment modes and external Unix helper prerequisites from PR #6. Record observed failures as RED, fix narrowly, then rerun GREEN; do not infer support from unit tests alone.
- [ ] Commit fixture/harness wiring and native evidence; no fabricated platform skips.

### RR07: compatibility and stress (Gates C–D)

**Files:** public API baseline, CI if needed, test fixtures; product code only for a demonstrated failure.

- [ ] Review exact additive API diff on all three TFMs. Run bounded repeated sessions with continuous output, resize storms, cap exhaustion, sink faults, cancellation, and disposal using watchdogs. Check file cap, no retained payload growth, and no leaked process/helper handles.
- [ ] Run full library tests, package artifact verifier, fresh package consumers and PR #6 published-consumer modes in six-platform CI. Preserve existing expected ConPTY fragmented-query skip only; separate platform flakiness from product defects.
- [ ] Resolve important findings with a failing regression before a fix, rerun the affected matrix and record head/command/results. Commit `test: verify recording compatibility and stress`.

### RR08: documentation, self-review, and acceptance (Gate D)

**Files:** `README.md`, `samples/README.md`, `ROADMAP.md`, design and this plan.

- [ ] Document opt-in usage, supplied-stream ownership, binary format/version, max bytes and reader limits, timestamp/order semantics, fault/truncation interpretation, sensitive output, and the absence of input capture or terminal interpretation. Show package-consumer commands compatible with the existing tooling.
- [ ] Self-review the five Review Focus cases and the API/format diff; inspect thread safety, resource release, cancellation, native resize failure, and error text. Update evidence and any necessary tests/code in reviewable commits.
- [ ] At the final implementation head run the complete six-platform/three-framework Staging matrix, exact NuGet package and published-consumer checks. Record SHA, workflow URLs, expected skips/warnings and any separate Windows laptop observations. Mark RR01–RR08 complete only with evidence; leave merge, versioning and publication for a separate user decision.

## Completion condition

The feature is ready for review when a package consumer can opt in to a bounded record, observe an unambiguous independent recording result, read/replay a validated cross-platform event file, and continue a live session when capture truncates or faults. Old callers must retain the behavior verified in PR #6. Any unverified platform/TFM or unresolved stream-liveness limitation remains explicit, not silently counted as passing.
