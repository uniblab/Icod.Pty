# Interactive hosting and controlled shutdown design

Status: selected scope approved for planning on 2026-10-04; API and implementation design proposed for review. This document and the [development roadmap](Interactive-Hosting-Implementation-Plan.md) accompany the next development PR.

## Intent and success criteria

Make the existing PTY library convenient and predictable for a consumer hosting a shell or interactive console application. The user selected immediate-input sample behavior, terminal interrupts, controlled shutdown, cancellable startup, and expanded interactive verification. The main [roadmap](../ROADMAP.md) preserves the alternatives considered.

Success means an application can start a session without blocking its calling thread, send input and an interrupt request, resize during activity, request a normal exit, and explicitly choose whether to force termination after a deadline. The sample demonstrates this through an existing terminal and restores the host's console settings after success, failure, or cancellation. It does not promise recovery after an uncatchable process kill or host crash.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.

No new third-party runtime dependency is required. Existing synchronous startup, raw streams, resize, wait, terminate, and disposal remain available. Primary-child ownership remains unchanged. The public API does not acquire terminal rendering, semantic keyboard encoding, arbitrary Unix signals, process-tree containment, or a portable raw-mode switch in this milestone.

## Architecture and file responsibilities

The shared `PtyProcess` layer owns operation contracts and delegates creation to the existing platform backends. Unix startup awaits the existing helper handshake asynchronously. Windows creation runs on a worker because its native process-creation call is synchronous. Both paths transfer backend ownership to a returned `PtyProcess` only after the final cancellation check; otherwise they dispose and reap the partially started child.

Shutdown is a separate shared coordinator. It writes an optional request and waits for the primary child; it never takes over the output reader. The interactive console adapter is sample-only code, with separate Windows and Unix host-console implementations.

| Files | Responsibility |
| --- | --- |
| `src/PtyProcess.cs`, `src/PtyStartInfo.cs` | Public entry points, snapshots, operation guards, and contract documentation. |
| `src/Unix/UnixBackend.cs`, `src/Windows/WindowsBackend.cs` | Platform creation, cancellation cleanup, and existing terminal/process ownership. |
| `src/PtyShutdownOptions.cs`, `src/PtyShutdownResult.cs` | Shutdown request, deadlines, and explicit result values. |
| `src/ShutdownCoordinator.cs` | Request/write/wait/escalation state machine without output consumption. |
| `src/Sample/Program.cs`, `src/Sample/InteractiveSession.cs` | CLI dispatch, sample pumps, resize monitoring, exit/drain coordination. |
| `src/Sample/HostConsole.cs`, `src/Sample/WindowsHostConsole.cs`, `src/Sample/UnixHostConsole.cs` | Sample host mode capture, raw input transport, size queries, and restoration. |
| `src/Tests/Icod.Pty.Tests/`, `src/Tests/Icod.Pty.TestChild/` | Deterministic operation tests, real PTY fixtures, and nested sample acceptance. |
| `packaging/VerifyPackageConsumer.ps1` | Fresh package consumers that compile the complete sample and exercise new public operations. |

## Proposed public contracts

```csharp
public static Task<PtyProcess> StartAsync(
    PtyStartInfo startInfo, CancellationToken cancellationToken = default);

public ValueTask SendInterruptAsync(CancellationToken cancellationToken = default);

public Task<PtyShutdownResult> ShutdownAsync(
    PtyShutdownOptions options, CancellationToken cancellationToken = default);

public sealed class PtyShutdownOptions {
    public ReadOnlyMemory<byte> Request { get; set; } = ReadOnlyMemory<byte>.Empty;
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(5);
    public bool ForceTermination { get; set; } = false;
    public TimeSpan TerminationTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

public enum PtyShutdownStatus { Exited, TimedOut }

public readonly record struct PtyShutdownResult(
    PtyShutdownStatus Status, int? ExitCode, bool ForcedTerminationRequested);
```

These are additive APIs. The names and defaults are the proposed design for review, not documentation of already shipped members.

### Asynchronous startup

- Capture and validate `PtyStartInfo` before starting asynchronous work. Callers must not mutate it during capture. Arguments, environment, directory, size, and host path are then independent snapshots.
- An already-cancelled token starts no helper or child. Cancellation observed after creation cleans up owned streams, terminal handles, and the primary child before the operation completes with `OperationCanceledException`.
- A successful return transfers ownership to the caller. Cancellation after that transfer has no effect on the running session.
- `PtyStartInfo.StartTimeout` retains its existing meaning: a finite positive Unix helper-handshake timeout, default 15 seconds. It is not a total startup deadline and is not applied to Windows. Timeout throws `TimeoutException`; caller cancellation throws `OperationCanceledException` carrying the caller token.
- Native process creation, filesystem resolution, and final cleanup are not interruptible hard-deadline operations. `StartAsync` must not abandon a running creation task just to return cancellation sooner.
- Preserve `Start` behavior through the shared creation/ownership path. Every internal await reachable from synchronous `Start` uses `ConfigureAwait(false)`.
- Factor a small internal startup ownership seam for tests: `StartCoreAsync(LaunchConfiguration launch, CancellationToken cancellationToken, Func<LaunchConfiguration, CancellationToken, Task<IPtyBackend>> backendFactory)`. It returns `Task<PtyProcess>`. Internal test access is assembly-scoped; there are no public or environment-variable test switches.

### Terminal interrupt

`SendInterruptAsync` sends exactly one byte, `0x03`, through the existing input stream. It means terminal Ctrl+C input, and follows the same cancellation and single-writer contract as `Input.WriteAsync`. It does not call the host process's console-control APIs or forcibly terminate a process.

The child and terminal settings decide what the byte does. With the conventional Unix interrupt character and signal processing enabled it requests interruption of the terminal foreground job; with other settings it can be ordinary input. Windows behavior depends on the child's console mode. It is not a guarantee of termination, a general POSIX-signal API, or a promise to honor a remapped Unix interrupt character. A cancelled write may already have delivered the byte.

Calling after disposal throws `ObjectDisposedException`. Calling after exit status is already collected throws `InvalidOperationException`; a concurrent exit may still cause the underlying stream's I/O error. Callers coordinate this method with other writers and shutdown requests.

### Shutdown state machine

`ShutdownAsync` is an optional process-control convenience over the existing streams and exit task. It does not dispose the session or close input/output. The caller retains exactly one output reader throughout and drains remaining output after process exit.

1. Validate and snapshot options, including copying request bytes. Both durations must be positive, finite, and no greater than `TimeSpan.FromMilliseconds(int.MaxValue)`. Validate even if the child has already exited.
2. Honor caller cancellation. Reject a second concurrent shutdown with `InvalidOperationException` so requests cannot be duplicated or interleaved. Acquire no monitor across an await.
3. If exit status is already collected, return `Exited` and the code without writing a request.
4. Start the grace deadline. Write the request if nonempty, then wait for exit. The same grace budget includes time blocked writing the request; an empty request means wait-only.
5. If the grace deadline expires, recheck collected exit status. With `ForceTermination == false`, return `TimedOut` and leave the session available. With `true`, recheck caller cancellation and exit status, then invoke existing primary-child termination and wait up to `TerminationTimeout` for exit collection.
6. Return the result; release the operation guard in `finally`. A timed-out operation can be retried sequentially with an explicit new request.

| Observed result | Status | ExitCode | ForcedTerminationRequested |
| --- | --- | --- | --- |
| Exited before escalation | `Exited` | Collected code | `false` |
| Grace expired; escalation disabled | `TimedOut` | `null` | `false` |
| Termination invoked and exit collected | `Exited` | Collected code | `true` |
| Termination invoked; collection deadline expired | `TimedOut` | `null` | `true` |

`ForcedTerminationRequested` records invocation of termination, not proof of the cause of death: natural exit can race with escalation. Do not infer it from platform exit-code conventions.

Caller cancellation stops the shutdown operation and never initiates escalation because of cancellation. It cannot retract request bytes or undo termination already invoked. If cancellation is observed alongside a timeout, caller cancellation wins. Request-write failures propagate unless exit status has already been collected, in which case return that result. Disposing the session concurrently can abort the operation with `ObjectDisposedException` or an underlying closed-stream error; resources remain owned by disposal. Failed request writes do not trigger automatic escalation.

There is no default `exit` command, EOF byte, close-input operation, or arbitrary callback. The caller supplies already-encoded request bytes appropriate to the application. Request callbacks and command encoders can be considered separately if a concrete consumer needs them.

## Interactive sample

The existing executable-and-arguments invocation selects interactive forwarding when host input and output are terminals. Add `--interactive -- <executable> [arguments]` and `--line -- <executable> [arguments]`; preserve `--smoke` and default-shell behavior. Consume one optional `--` before the executable. Redirected input/output require explicit line mode or smoke mode, with a clear diagnostic otherwise.

- Capture the host's terminal modes and encodings before mutation. Disable host line buffering and echo for the forwarding session. Capture Ctrl+C as input to the child instead of killing the sample. Restore captured state in a `finally` path, including partial setup failure.
- On Windows, use host console VT input/output modes and UTF-8 transport. On Unix, apply a raw host mode with platform-correct termios layouts and preserve the original structure for restoration. Do not alter the child's terminal modes from the sample.
- Forward bytes as they arrive. Avoid `Console.ReadLine` and `Console.ReadKey` in interactive mode; they can buffer or reinterpret protocol responses. Preserve VT sequences and terminal-query replies across chunk boundaries. Use one input writer and one output reader.
- Forward host output to its existing terminal; do not add an emulator or advertise unsupported capabilities. On Unix preserve an existing `TERM`; the library's existing fallback remains unchanged.
- Read host dimensions at launch. Check for changes at most every 100 ms and call `Resize` only when a positive, representable size actually changes. Ignore transient zero sizes; report other resize failures unless the child is already exiting.
- Reserve no ordinary key chord for the sample; child applications receive input including Ctrl+C. Natural child exit stops input/resize pumps. Input EOF requests bounded shutdown: wait up to 5 seconds with an empty request, then allow primary-child termination with a 5-second collection timeout.
- After exit, drain output for up to 5 seconds. If a descendant holds the terminal open or the drain stalls, stop the pump, restore host state, report that output draining timed out, and return sample exit code 1. Successful draining returns the child's exit code. Document that a drain timeout can truncate output; do not report it as clean completion.
- Stopping a blocked host-input read must not prevent restoration. Unix host reads use readiness polling; Windows host reads need a cancellable wait/read strategy. No fire-and-forget input task may outlive the restored console session.

Line mode remains an explicit demonstration with line input. Sample-only interop and host-console changes must not enter the public library's API surface. The package consumer verifier currently copies only `src/Sample/Program.cs`; it must copy the complete sample source tree once the sample is split, and enable any required unsafe compilation consistently with the sample project.

Add documented, noninteractive package verification modes alongside the existing `--smoke`: `--lifecycle-smoke` verifies asynchronous shell startup, an explicit exit request, and complete output draining; `--cancel-start-smoke` verifies precancelled startup; `--interrupt-smoke` starts the same managed sample in `--interrupt-child` verification mode, waits for readiness, calls `SendInterruptAsync`, and requires an acknowledgement before requesting exit. The verification child registers its own interrupt handler and does not depend on external fixture binaries. Run all four modes on every framework and in published output.

## Verification and risks

Use C# fixtures for raw input, interrupt handling, ignored shutdown requests, large final output, and host-mode inspection. Use synchronization markers and bounded waits rather than sleeps that guess whether a child is ready. Test normal and raw-mode Ctrl+C separately: the former proves application handling; the latter proves delivery of byte `0x03` without promising a signal.

Exercise the sample inside an outer PTY for repeatable input, resize, and terminal-restoration checks. Verify restoration using a supervising fixture that retains the same terminal after the sample exits. Keep external editor/game checks manual and record exact versions; do not install additional full-screen applications as mandatory CI dependencies.

| Risk | Required mitigation |
| --- | --- |
| Cancellation between child creation and ownership transfer | Inject a per-call internal backend factory in tests; prove backend disposal before cancellation completes, then verify real platform cancellation paths. |
| Request write blocks on a full input buffer | Include the write in the grace deadline; prove timeout and caller cancellation release the operation guard without consuming output. |
| Ctrl+C reaches the hosting process | Test the outer host stays alive and the child sees the request; restore host processed-input settings. |
| Darwin/Linux termios layout or ABI mismatch | Separate platform layouts, retain existing Darwin ARM64 ioctl conventions, and verify on both architectures. |
| Child exits while a read, resize, or shutdown is active | Coordinate completion, preserve already emitted output, and test disposal and deadline races. |

Existing tests remain mandatory, including backpressure/disposal, arguments/environment, resize, and final-output preservation. Run every library fixture on net8.0/net9.0/net10.0 on all six CI targets, and retain real Windows PowerShell 5.1 package verification. The Windows laptop acceptance adds immediate editing keys, tab completion, resize, interruption of a running command, and restored behavior after exit in CMD and Windows PowerShell 5.1.

## Reference material

- [Current foundation design](PTY-Design.md).
- [Microsoft pseudoconsole responsibilities](https://learn.microsoft.com/en-us/windows/console/pseudoconsoles).
- [Microsoft console input/output modes](https://learn.microsoft.com/en-us/windows/console/setconsolemode).
- [Microsoft introduction to ConPTY, including Ctrl+C input](https://devblogs.microsoft.com/commandline/windows-command-line-introducing-the-windows-pseudo-console-conpty/).
- [Microsoft synchronous I/O cancellation and completion requirements](https://learn.microsoft.com/en-us/windows/win32/fileio/canceling-pending-i-o-operations).
- [POSIX general terminal interface](https://pubs.opengroup.org/onlinepubs/9799919799/basedefs/V1_chap11.html).
