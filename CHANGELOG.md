# Changelog

All notable changes to Icod.Pty are recorded here. Version selection, tagging, and publication are separate
release decisions; the entries below describe the current unreleased repository state.

## Unreleased

### Added

- PR #1 established cross-platform pseudoterminal process launch, byte streams, resize, exit, cancellation,
  disposal, and package verification.
- PR #2 added the interactive sample, console input and resize forwarding, host restoration, controlled shutdown,
  cancellable startup, and the initial ConPTY fragmented-input reproducer.
- PR #3 added opt-in Windows job and Unix initial-process-group ownership, explicit control outcomes, named Unix
  signals, targeted shutdown, and descendant cleanup.
- PR #4 added reusable `PtySession` orchestration with ordered input, output forwarding, bounded lifecycle
  diagnostics, drain handling, and staged completion results.
- PR #5 added launch-time Unix terminal configuration and side-effect-free capability discovery.
- PR #6 qualified framework-dependent, self-contained, single-file, and trimmed consumers across the supported
  platform/RID matrix and pinned the public API compatibility baseline.
- PR #7 added bounded, versioned output/resize recording plus validated streaming read and deterministic replay.
- PR #8 added opt-in binary-safe live matching and short ordered send/expect scripts.

### Deployment prerequisites

- Supported targets are net8.0, net9.0, and net10.0 on Windows, Linux, and macOS, each on x64 and ARM64.
- Windows requires build 10.0.26200.9457 or later and uses the operating-system ConPTY implementation.
- Linux and macOS consumers must deploy the packaged `Icod.Pty.Host` files beside the application and have a
  compatible installed .NET runtime and `dotnet` host available to launch the helper.
- The helper remains external for single-file and trimmed applications. NativeAOT is an informational feasibility
  result, not a supported deployment mode.

### Known limitations

- Windows ConPTY can intermittently lose a query prefix when one logical terminal reply is fragmented across
  separate host writes. Send complete terminal replies in one write when possible; this reduces exposure but is
  not a universal native transport guarantee. See `docs/ConPTY-Input-Limitations.md` for bounded evidence.
- NativeAOT promotion, terminal emulation, text or regex matching, branching automation, input recording, and
  timed replay remain outside the supported contract.
