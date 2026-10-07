# Focused timed playback of recordings

**Status:** approved and implemented on PR #10. Local tests and exact-package consumers pass; complete six-platform acceptance is pending at the implementation head.

## Intent and constraints

A consumer should be able to reproduce the pacing and event order of a saved session without starting a PTY process. The consumer decides how to apply the output bytes and terminal resizes, so the library does not take ownership of a host console or interpret terminal content. A successful playback is useful for demonstrations and diagnostics; its timing is best effort under operating-system scheduling and a slow consumer.

This increment uses the v1 format and the existing `PtyRecordingReader`. It does not record input, alter the writer, reinterpret events, or change the immediate `ReplayAsync(Stream)` behavior. Keep C# 13, net8.0/net9.0/net10.0 AnyCPU, Windows/Linux/macOS x64/ARM64, one LGPL-3.0-or-later package, and PowerShell 5.1/CMD/SH tooling. Preserve the Unix external helper and installed `dotnet` runtime requirement. No version, tag, merge, or publication is selected here.

## Considered approaches

1. **Add an opt-in timed event dispatcher to the reader (selected).** Stream each validated event to a caller callback at its recorded elapsed time. This reuses the existing reader's byte limits and validation and exposes output and resize through one ordered path. A callback is allowed to apply events to any output or display abstraction, but a slow callback delays playback.
2. Add timing to `ReplayAsync(Stream)` itself. This would silently change its immediate behavior and still omit resize delivery; even an overload with a flag would obscure the distinction.
3. Create a new replay engine with its own parser and virtual terminal. That duplicates format validation and expands option 12 into a small pacing feature.

## Public contract

Add `PtyRecordingTimedPlaybackOptions` with `MaxEventElapsed` (default one hour), a positive finite `TimeSpan` limit on the timestamp of an event this call will dispatch. Callers with longer recordings may set a larger value explicitly. The option is captured and validated at call entry; a later mutation cannot change an active playback. Do not add speed control, pause/resume, seeking, input events, or a global clock setting in this increment.

Add to `PtyRecordingReader`:

```csharp
Task<PtyRecordingReplayResult> PlayTimedAsync(
    Func<PtyRecordingEvent, CancellationToken, ValueTask> onEvent,
    PtyRecordingTimedPlaybackOptions? options = null,
    CancellationToken cancellationToken = default);
```

The callback receives the reader's owned `PtyRecordingEvent`, including binary output and resizes. The header's `InitialSize` is available before playback; it is not synthesized as a resize event. The existing `PtyRecordingReplayResult` reports the validated terminal status, output bytes successfully dispatched, and number of successfully dispatched events. A valid `Truncated` or `Stopped` file dispatches its recorded prefix and returns that status. `ReplayAsync(Stream)` remains immediate and output-only.

One reader supports one consumer operation at a time: `ReadAsync`, `ReplayAsync`, or `PlayTimedAsync`. Concurrent or reentrant use fails with `InvalidOperationException`; no two operations may interleave frames. Once consumed, playback continues from the current reader position. A call after a validated terminal marker returns that terminal status with zero newly dispatched events. Source ownership is unchanged; the callback and any streams it uses belong to the caller.

## Timing and failure semantics

Create one monotonic playback origin when `PlayTimedAsync` starts. Before dispatching an event at elapsed time `t`, wait until at least `origin + t`. This includes the first event's delay from recording start. Equal timestamps dispatch in file order without an intentional gap. Anchor all waits to the same origin: source reads and callbacks that take time reduce later waits; do not add each delta after the prior callback. If already late, dispatch the event promptly rather than dropping it. Wall-clock changes do not affect scheduling. The caller is responsible for a callback that cooperates with cancellation and makes progress.

Reject an event whose `Elapsed > MaxEventElapsed` with a payload-free `InvalidOperationException` **before** invoking its callback or waiting for its target time. `MaxEventElapsed` limits event scheduling, not the reader's independent byte/frame limits or the timestamp of the terminal marker. A malformed recording continues to throw `PtyRecordingFormatException`; any already delivered prefix is not rolled back. A callback exception propagates unchanged. Cancellation during reading, waiting, or callback propagates as cancellation; a callback that partially acts before failing or cancellation may have visible side effects. Neither failure implies that the reader is rewindable or resumable. No completion result is returned on exception or cancellation.

Timing is best effort. No promise of precise sub-millisecond delivery or bounded callback/stream execution is made. The scheduler waits in cancellable, bounded intervals against a monotonic clock and rechecks the target after each interval, avoiding overflow for large caller-selected limits. Tests inject an internal clock and delay function; no public timing dependency or external package is added.

## Acceptance

- A golden recording with output and resize events dispatches both at or after their recorded times, in order, and returns the same complete/truncated/stopped status and counts as event iteration. NUL and invalid UTF-8 bytes remain unmodified.
- First-event delay, equal timestamps, late callbacks, clock adjustments through the monotonic seam, and a timestamp exceeding the configured cap have deterministic tests. Real elapsed-time smoke uses a generous tolerance only for integration, never as the sole proof of timing.
- Cancellation during a pending delay does not invoke the pending callback; cancellation and callback/read/format errors propagate without payload disclosure. A second operation cannot interleave on the reader. Default source ownership remains unchanged.
- The exact NuGet package's noninteractive sample smoke demonstrates the feature on all three target frameworks and all six CI platforms. Existing immediate replay, recording, session, and published-consumer modes remain green. Manual Windows laptop observations are recorded separately from hosted CI.

The [implementation plan](Timed-Playback-Implementation-Plan.md) maps this contract to test-first tranches and final evidence. The [main roadmap](../ROADMAP.md) retains the complete option menu.
