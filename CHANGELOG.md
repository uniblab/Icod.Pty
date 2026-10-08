# Changelog

All notable Icod.Pty changes are recorded here. The history follows the implemented pull requests in chronological
order; version selection, tagging, and publication remain separate release actions.

## Unreleased

No changes.

## 1.0.0-rc.1 - 2026-10-08

This is the feature-complete release candidate for the stable 1.0 contract. It freezes the post-PR #10 public API
baseline and the version-1 recording format while the public package and final interactive acceptance are
evaluated. The package has no third-party runtime package dependency or checked-in native helper binary.

### Feature history

- **PR #1 — PTY foundation (2026-10-04):** added cross-platform pseudoterminal process launch, literal argument
  handling, environment and working-directory settings, raw byte input/output, resize, exit status,
  cancellation-aware waiting, primary-process termination, deterministic disposal, and exact-package checks.
- **PR #2 — interactive hosting and controlled shutdown (2026-10-04):** added immediate input forwarding, host
  resize forwarding, Ctrl+C input, cancellable startup, application-directed shutdown, final output draining,
  and restoration of console modes, encodings, and Windows code pages in the sample host.
- **PR #3 — process-scope ownership (2026-10-04):** added opt-in Windows job and Unix initial-process-group
  ownership, descendant cleanup, named Unix signals, explicit control outcomes, targeted shutdown, and scoped
  disposal while preserving primary-process defaults.
- **PR #4 — reusable sessions and lifecycle diagnostics (2026-10-05):** added `PtySession` orchestration for
  serialized input, output forwarding, ordered shutdown, drain deadlines, cleanup, staged completion results,
  counters, current phase, and a bounded payload-free lifecycle history.
- **PR #5 — launch-time terminal configuration (2026-10-06):** added side-effect-free capability discovery and
  Unix launch-time echo, canonical/noncanonical input, signal-processing, control-character, read-timing, and
  Raw settings. Windows explicitly reports no supported configuration fields and rejects requested fields before
  allocating a child.
- **PR #6 — deployment portability (2026-10-06):** qualified framework-dependent, self-contained, single-file,
  and trimmed applications across Windows, Linux, and macOS on x64 and ARM64; pinned the public compatibility
  baseline; and retained NativeAOT as an informational feasibility result.
- **PR #7 — bounded recording and replay (2026-10-06):** added opt-in version-1 recording of accepted output
  bytes and successful resizes, explicit complete/truncated/stopped/faulted results, validated streaming reads,
  finite reader limits, and immediate deterministic output replay without launching a process.
- **PR #8 — focused live automation (2026-10-07):** added opt-in binary-safe bounded output matching and ordered
  send/expect scripts with explicit match, timeout, output-completion, output-failure, cancellation, and buffer-cap
  behavior. Input remains unrecorded.
- **PR #9 — release stabilization and ConPTY classification (2026-10-07):** audited the public contract,
  documentation, sample, package metadata, exact consumers, and published layouts; added a reproducible pure-C#
  ConPTY fragmentation classifier; and classified fragmented query-prefix loss as a native ConPTY limitation
  rather than a managed-forwarding defect.
- **PR #10 — timed recording playback (2026-10-08):** added `PlayTimedAsync` for validated output and resize
  events, absolute monotonic scheduling from the recording origin, first-event pacing, configurable elapsed-time
  limits, cancellation, callback propagation, and one-operation reader ownership while preserving the version-1
  format and immediate `ReplayAsync` behavior.

### Corrected

- Corrected an already-exiting Windows process cleanup race in which `TerminateProcess` could report
  `ERROR_ACCESS_DENIED` before the process handle became signaled. Other native termination failures remain
  visible.
- Hardened startup failure cleanup, output draining, process collection, helper-layout validation, and package
  consumer fixtures as the supported matrix expanded.

### Deployment prerequisites

- Supported targets are net8.0, net9.0, and net10.0 on Windows, Linux, and macOS, each on x64 and ARM64.
- Windows support starts at build `10.0.26200.9457` and uses the operating-system ConPTY implementation.
- Linux and macOS consumers must distribute the packaged `Icod.Pty.Host.dll`, `.deps.json`, and
  `.runtimeconfig.json` beside the application and provide a compatible installed .NET runtime and `dotnet` host.
  This helper requirement also applies to self-contained, single-file, and trimmed consumers.
- Framework-dependent, self-contained, single-file, and trimmed application layouts are qualified. NativeAOT is
  not a supported deployment promise.

### Known limitations

- Windows ConPTY can intermittently lose a terminal-query prefix when one logical reply is fragmented across
  separate host writes. Sending the complete reply in one write reduces exposure but is not a universal native
  transport guarantee. See `docs/ConPTY-Input-Limitations.md`.
- Windows does not expose the Unix launch-time terminal controls; explicit requests fail before launch.
- NativeAOT promotion, 32-bit processes, musl and unlisted Unix RIDs, live terminal-state mutation, generic
  tracing/exporters, input recording, playback speed/pause/seek, regex or screen-aware matching, branching
  automation, resource controls, persistent sessions, and terminal emulation remain outside the 1.0 contract.
