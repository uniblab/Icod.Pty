# Reusable Session Orchestration and Lifecycle Diagnostics

**Status:** feature selection approved 2026-10-04; proposed contracts for review before implementation.
**Selection:** roadmap option 3 plus a focused subset of option 4.
**Companion:** [development roadmap](Session-Orchestration-Implementation-Plan.md).

## Intent

Consumers currently coordinate input writers, output forwarding, process exit, shutdown, draining, and
cleanup themselves. Provide one optional session owner that performs that coordination, with inspectable
outcomes. Preserve PtyProcess for callers that want direct byte-stream and lifetime control.

Success means a consumer can forward bytes, request application-directed shutdown, await an honest
completion result, and release the selected ownership scope without writing its own competing pump loops.
The existing interactive sample must retain console restoration and immediate input behavior when adapted.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.
- Preserve existing PtyProcess, shutdown, ownership, native signal, and package contracts.
- No third-party runtime dependency, version bump, merge, tag, or publication in this milestone.

## Scope and alternatives

Add a byte-oriented PtySession, serialized input, one output pump, optional input-stream forwarding,
automatic finalization, a completion result, and bounded lifecycle diagnostics. Reuse the proven native
backends and ownership policy; do not introduce a second process implementation.

A sample-only coordinator would leave other consumers copying lifecycle code. Expanding PtyProcess itself
would change its caller-owned streams and disposal model. An additive session owner keeps that choice explicit.
Start with newly launched sessions only; adopting an already-used PtyProcess would require proving that no
other reader/writer/owner remains and is deferred.

Broader tracing, metrics exporters, startup-stage tracing inside native backends, transcript recording,
encoding/line helpers, output matching, terminal emulation, reconnect brokers, and foreground retargeting
are deferred. Host-console modes, code pages, and resize detection remain sample/host responsibilities.

## Proposed public surface

All new types are in namespace Icod.Pty. This document proposes signatures, not an implemented API.

```csharp
public sealed class PtySessionOptions {
    public PtySessionOptions(Stream output);
    public Stream Output { get; }
    public Stream? Input { get; set; }                 // default null
    public bool LeaveInputOpen { get; set; }           // default true
    public bool LeaveOutputOpen { get; set; }          // default true
    public TimeSpan DrainTimeout { get; set; }        // default 5 seconds
}

public sealed class PtySession : IDisposable, IAsyncDisposable {
    public static Task<PtySession> StartAsync(PtyStartInfo startInfo,
        PtySessionOptions options, CancellationToken cancellationToken = default);
    public int ProcessId { get; }
    public PtySize Size { get; }
    public bool HasExited { get; }
    public PtyProcessOwnership Ownership { get; }
    public PtyProcessCapabilities Capabilities { get; }
    public Task<PtySessionResult> Completion { get; }
    public Task<PtySessionOutputStatus> OutputCompletion { get; }
    public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default);
    public ValueTask SendInterruptAsync(CancellationToken cancellationToken = default);
    public Task<PtyShutdownResult> ShutdownAsync(PtyShutdownOptions options,
        CancellationToken cancellationToken = default);
    public void Resize(PtySize size);
    public PtyControlResult RequestTermination(PtyProcessTarget target);
    public PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target);
    public PtySessionDiagnostics GetDiagnostics();
    public void Dispose();
    public ValueTask DisposeAsync();
}

public enum PtySessionEndReason {
    PrimaryExited, Disposed, InputFailed, OutputFailed, ProcessFailed
}
public enum PtySessionOutputStatus { EndOfStream, TimedOut, Stopped, Faulted }
public enum PtySessionFailureStage { Input, Output, Process, Cleanup }
public sealed record PtySessionFailure(PtySessionFailureStage Stage, Exception Exception);
public sealed record PtySessionResult(
    PtySessionEndReason Reason, int? ExitCode, PtySessionOutputStatus OutputStatus,
    PtyShutdownResult? LastShutdownResult, IReadOnlyList<PtySessionFailure> Failures);

public enum PtySessionPhase { Running, Draining, Releasing, Completed }
public enum PtySessionEventKind {
    Started, InputEnded, InputSealed, ShutdownStarted, ShutdownCompleted,
    ShutdownCancelled, ShutdownFailed, PrimaryExited, OutputEnded, DrainTimedOut,
    InputFailed, OutputFailed, ProcessFailed, ReleaseStarted, CleanupFailed, Completed
}
public sealed record PtySessionEvent(long Sequence, TimeSpan Elapsed, PtySessionEventKind Kind);
public sealed record PtySessionDiagnostics(
    PtySessionPhase Phase, bool InputSealed, long BytesWrittenToPty,
    long BytesReadFromPty, long BytesWrittenToOutput, long DroppedEvents,
    IReadOnlyList<PtySessionEvent> Events);
```

Enum values start at zero in the listed order. Collection properties are detached, read-only snapshots;
callers cannot mutate session state by casting them back to mutable collections. Result records describe
observations; callers constructing or copying records do not change the session.

## Ownership and startup

Capture launch settings and options synchronously before the first yield. Validate a non-null writable
Output, an optional readable Input, distinct Input/Output objects, and a positive DrainTimeout no larger
than Int32.MaxValue milliseconds. Never mutate caller streams or launch a child after validation failure.
Streams must not be concurrently disposed or independently read/written by their caller while in use here.

PtySession creates and exclusively owns a PtyProcess. It does not expose the process or its raw streams.
It exposes metadata and coordinated operations only. Ownership still defaults to PrimaryProcess;
PlatformScope is explicitly selected through PtyStartInfo before launch.

Transfer caller-stream ownership only on successful StartAsync return. Failed/cancelled startup cleans
any created child but leaves supplied streams open, even if LeaveInputOpen/LeaveOutputOpen were false.
Start pumps at successful handoff and observe every task. A child exiting immediately is a normal case.
If internal setup fails after child creation, preserve the original error and any cleanup errors.
Startup exceptions retain existing error types unless simultaneous cleanup failure requires aggregation.

The startup token affects startup only. Completion.WaitAsync(token) cancels that wait only. There is no
hidden lifetime cancellation token that requests termination when a consumer stops awaiting a task.

## Input ordering and shutdown

All normal writes, optional input-pump writes, ETX, and shutdown request bytes use one internal writer gate.
Each WriteAsync call is indivisible relative to other session writes. Concurrent invocations are ordered
by gate admission; no FIFO invocation-order or scheduler fairness promise is made. Callers needing a
specific order await their writes in that order. No byte copying or unbounded internal transcript queue
is introduced: the caller keeps WriteAsync memory valid and unchanged until its ValueTask completes.

Write cancellation before admission sends no bytes. Cancellation during an admitted native write may
leave a prefix delivered, as with the existing Input stream. SendInterruptAsync remains exactly one ETX
byte. Native SendSignal is a distinct out-of-band operation and does not participate in byte ordering.
SendInterruptAsync rejects an already observed primary exit, as PtyProcess does. Resize and native control
retain the existing process validation and exceptions. A failed explicit control request is reported to
its caller; it does not itself authorize an additional session shutdown or invent a completion failure.
One input pump uses a 16 KiB reusable buffer. Input EOF records InputEnded and ends that pump only; it
never closes PTY input, sends an exit command, or interprets EOF as primary exit. Explicit WriteAsync
remains available after source EOF. Input-read/write faults begin InputFailed finalization; caller or
session-directed cancellation is not an input failure.

ShutdownAsync validates and captures options before changing state. A pre-cancelled or invalid request
leaves input usable. The first accepted shutdown permanently seals normal input, cancels the source read
and normal pending/active writes, and quiesces the writer before writing the captured shutdown request.
Subsequent ordinary writes/ETX fail with InvalidOperationException while sealed, ObjectDisposedException
after release starts. Concurrent shutdown is rejected; sequential retries remain supported while running.
Input stays sealed even if shutdown times out, fails, or is cancelled. This deliberate session-level rule
prevents user input after an exit request; callers needing to resume dialogue use PtyProcess instead.

The grace budget includes waiting for writer quiescence, the request write, and primary exit. Reuse
ShutdownCoordinator through a narrow internal request-writer hook, preserving the public PtyProcess path.
Do not acquire the writer before starting the grace deadline. Forced escalation remains opt-in and obeys
the captured PrimaryProcess/OwnedScope target. Caller cancellation never starts escalation or implicitly
disposes the session. Already-issued bytes/control cannot be undone. Record LastShutdownResult only when
an invocation returns a result; a later cancelled/failed invocation does not invent or erase a prior result.

ShutdownAsync returns the existing primary-exit/deadline result. Await Completion separately for output
and cleanup. Register in-flight shutdown work so finalization observes its result/error and releases the
process only after it settles. Never hold a lifecycle lock while waiting for a writer, pump, or shutdown.

## Output and completion

One output pump copies raw bytes to Output with a 16 KiB reusable buffer and backpressure. It does not
parse VT, change encoding/newlines, or accumulate an unbounded buffer. Successful EOF includes FlushAsync
of the destination; a flush failure is OutputFailed. OutputCompletion reports its terminal status after
that pump settles. EOF before primary exit does not terminate the child or complete the whole session.

Normal primary exit seals input and starts the DrainTimeout budget. Output reading/writing/flushing
continues until EOF or that deadline. If EOF and flush have already completed, there is no extra wait.
A deadline cancels the pump, records TimedOut, and begins resource release. This does not assert lossless
output. Surviving descendants can hold the terminal open; native scope cleanup remains the existing policy.
In PlatformScope, final disposal requests scoped cleanup even after a graceful primary exit. This differs
from a caller merely waiting on PtyProcess and is intentional automatic session ownership.

Dispose/DisposeAsync and a pump/process-observation fault begin release immediately; they do not spend a
new graceful drain interval. Preserve a previously observed output EOF/fault/timeout; otherwise output is
Stopped. Cancel pumps, release the process using its established native teardown ordering, observe all
pump and admitted-operation tasks, and dispose owned external streams exactly once. Leave-open streams
remain open. Do not hold locks across asynchronous waits or potentially blocking native cleanup.
Recognize cancellation caused by the session's own stop token and PTY read/write errors caused by its
native close as expected teardown. Do not suppress a previously observed fault, an unrelated cancellation,
or a destination/source failure merely because another operation concurrently requested release.

The first accepted terminal trigger determines Reason; concurrent secondary failures are still retained.
A normal primary exit followed by cleanup failure retains PrimaryExited plus Cleanup failures. ExitCode
is populated only if primary status was actually observed. No result claims all descendants have exited.

After successful startup, Completion resolves to one shared PtySessionResult, including operational
failures with their original exceptions and stage. It does not fault/cancel for those failures. Invalid
method calls still throw normally. DisposeAsync joins the same finalizer and throws AggregateException
if the final result contains failures; synchronous Dispose uses that same path. Repeated disposal performs
no second cleanup and observes the same result/errors. Metadata, diagnostics, and Completion remain usable.

### Limits of deadlines and supplied streams

Input.ReadAsync, Output.WriteAsync, and Output.FlushAsync must honor cancellation promptly. Arbitrary
Stream implementations and native process cleanup cannot be given a hard wall-clock deadline. Keep the
host's cancellation-capable console adapter in the sample; do not substitute Console.ReadLine or assume
Console.OpenStandardInput is promptly cancellable on every platform.

Completion means no pump remains using a supplied stream. Do not return early and leave abandoned writes
or reads behind to fake a timeout. If a stream ignores cancellation, finalization may remain pending until
it unblocks; demonstrate that limitation with a controlled stream that is released by the test afterward.
Output's drain deadline bounds the requested drain interval, not an uninterruptible native cleanup call.
Default leave-open behavior does not authorize closing a caller's stream as a cancellation workaround.

## Focused diagnostics

GetDiagnostics returns a coherent detached snapshot, available after completion/disposal. Keep the last
32 lifecycle events in a ring; retain monotonically increasing sequences and DroppedEvents for evictions.
Use monotonic elapsed time rather than wall-clock ordering. No per-chunk events, observer callbacks, or
background exporter tasks. Shutdown attempts can fill the history without growing memory indefinitely.

Counters record completed PTY reads, completed PTY writes, and completed destination writes respectively;
partially delivered writes that fail/cancel are not claimed as confirmed bytes. Counters are diagnostic,
not transactional delivery receipts. Update snapshots under a short private synchronization boundary.

Events contain kinds, sequences, and elapsed durations only: no terminal bytes, command lines, arguments,
environment values, paths, or automatic exception-message logging. Failures are explicitly available to
the owning consumer in Completion. Native operation/error context is preserved in those exceptions.
Defer subscribers/callbacks, EventSource/OpenTelemetry, generalized capability expansion, and transcripts.

## Integration and acceptance

Adapt InteractiveSession to session-owned output and ordered writes, retaining HostConsole ownership,
resize monitoring, console restoration, and the existing explicit EOF shutdown policy. The host still
reads console input through HostConsole.ReadAsync and calls session.WriteAsync. It may observe
OutputCompletion to preserve the current early-EOF shutdown behavior. Join host input/resize tasks before
restoring console state. Keep --line and all existing low-level smoke modes compatible.

Add --session-smoke exercising normal output drain, explicit application shutdown, completion diagnostics,
and leave-open behavior. Add --session-scope-smoke covering PlatformScope with a known surviving descendant,
independent descendant exit observation, and automatic session cleanup. Verify both from the actual packed
NuGet artifact on all frameworks and from published net10.0 output, on all six OS/architecture jobs.

Use deterministic controlled streams/backends for ordering, competing triggers, cancellation, failures,
and ignored-cancellation cases; use real PTYs for native teardown and descendant outcomes. Preserve all
legacy tests and the documented ConPTY exclusion. Laptop checks supplement, rather than replace, native CI.
