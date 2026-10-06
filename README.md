# Icod.Pty

Icod.Pty hosts child processes in a pseudoterminal. It provides asynchronous startup, raw byte input/output, launch-time terminal configuration, a reusable session owner, bounded output/resize recording and replay, terminal resizing, Ctrl+C input, controlled shutdown, explicit process-scope ownership, native Unix signals, exit status, bounded lifecycle diagnostics, and deterministic cleanup.

The library is written in **C# 13**, targets **net8.0, net9.0, and net10.0**, and builds as **AnyCPU**. One NuGet package contains all three library targets and the managed Unix helper. There are no third-party runtime packages or native binaries to build.

Development direction and deferred alternatives are recorded in the [main roadmap](ROADMAP.md). Initial terminal configuration is specified by the [terminal configuration design](docs/Terminal-Configuration-Design.md). Recording format and lifecycle semantics are specified by the [recording/replay design](docs/Recording-Replay-Design.md).

## Platforms

| Operating system | Architectures | Backend |
| --- | --- | --- |
| Windows, starting at `10.0.26200.9457` | x64, ARM64 | Windows ConPTY |
| Linux | x64, ARM64 | OS PTY APIs and managed helper |
| macOS | x64, ARM64 | OS PTY APIs and managed helper |

CI exercises all six OS/architecture combinations and all three target frameworks, including real PTY integration tests and NuGet consumer checks. AnyCPU lets the same assemblies run on either supported architecture; native API calling conventions are selected at runtime. 32-bit processes are outside the support contract.

| CI architecture | Windows | Linux | macOS |
| --- | --- | --- | --- |
| x64 | `windows-latest` | `ubuntu-latest` | `macos-26-intel` |
| ARM64 | `windows-11-arm` | `ubuntu-24.04-arm` | `macos-latest` |

GitHub uses explicit labels for Windows/Linux ARM64 and macOS Intel; the selected labels match the current latest images (Ubuntu 24.04 and macOS 26).

**Unix requires an installed .NET 8, 9, or 10 runtime and its `dotnet` host**, including when your application is self-contained. The helper targets .NET 8 and rolls forward to the newest installed major runtime. You can set `PtyStartInfo.DotNetHostPath` explicitly. NuGet copies the `Icod.Pty.Host` directory into application build and publish output; distribute that directory with your application. Do not exclude the package's `buildTransitive` assets.

### Published application support

The following forms are verified from a fresh consumer of the packed NuGet artifact. CI publishes for the listed RID, moves selected complete output trees, invokes the final apphost directly, and exercises process, session, recording/replay, cancellation, interrupt, owned-scope, terminal-configuration, invalid-host, output-drain, and cleanup behavior.

| OS / architecture | RIDs | TFMs | Framework-dependent | Self-contained | Single-file self-contained | Trimmed self-contained |
| --- | --- | --- | --- | --- | --- | --- |
| Windows x64 / ARM64 | `win-x64`, `win-arm64` | net8.0, net9.0, net10.0 | Verified | Verified | Verified | Verified |
| Linux x64 / ARM64 | `linux-x64`, `linux-arm64` | net8.0, net9.0, net10.0 | Verified | Verified | Verified | Verified |
| macOS x64 / ARM64 | `osx-x64`, `osx-arm64` | net8.0, net9.0, net10.0 | Verified | Verified | Verified | Verified |

Framework-dependent applications need their target runtime. On Unix, every form also needs a usable `dotnet` host and a compatible installed runtime for the external net8.0 helper. A consumer's bundled runtime is not used to launch that helper. Windows ConPTY does not launch the helper, and tests verify that missing Unix helper assets and an invalid `DotNetHostPath` do not affect it.

Single-file publishing bundles the consumer while leaving `Icod.Pty.Host/Icod.Pty.Host.dll`, `.deps.json`, and `.runtimeconfig.json` beside the executable as external files. Copy the complete publish directory. The package verifier rejects an incomplete helper directory before making a support claim. Runtime behavior with missing metadata can vary by installed `dotnet`; only a complete three-file layout is supported.

Trimmed support describes the tested Icod.Pty paths and published sample surface on these TFMs and RIDs. It does not imply that unrelated consumer code is trim-safe. NativeAOT remains a feasibility result recorded in the [deployment design](docs/Deployment-Portability-Design.md), and musl or other unlisted Unix RIDs remain untested.

Run the same exact-package check from SH or PowerShell 7:

```sh
pwsh -File ./packaging/VerifyPortableConsumer.ps1 -ArtifactDirectory artifacts -Framework net10.0 -RuntimeIdentifier linux-x64 -Mode SingleFile -Scenario Relocated
```

From CMD or Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -File .\packaging\VerifyPortableConsumer.ps1 -ArtifactDirectory artifacts -Framework net10.0 -RuntimeIdentifier win-x64 -Mode SingleFile -Scenario Relocated
```

## Usage

```csharp
using Icod.Pty;

var start = new PtyStartInfo(
    OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") {
    Size = new PtySize(100, 30)
};
start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
start.ArgumentList.Add("echo Hello from the PTY");

await using var process = await PtyProcess.StartAsync(start);
Task output = process.Output.CopyToAsync(Console.OpenStandardOutput());
int exitCode = await process.WaitForExitAsync();
await output;
```

For a long-lived process, write bytes to `Input` and call `Resize(new PtySize(columns, rows))`. For example, terminal Enter is normally carriage return (`\r`) on Windows and line feed (`\n`) with the default Unix terminal settings.

- Arguments are separate literal values; Icod.Pty performs no shell expansion. Use an explicit shell/interpreter for scripts or shell syntax.
- `WorkingDirectory` defaults to the current directory. Environment variables are inherited; entries in `Environment` override them, and a null value removes one. Unix defaults `TERM` to `xterm-256color` only when absent.
- Use one reader and one writer concurrently. The output combines standard output and standard error and may contain VT escape sequences, echo, and terminal line-ending conversions. Icod.Pty does not render or parse terminal output.
- Coordinate direct `Input` writes, `SendInterruptAsync`, and the request-writing portion of `ShutdownAsync` as a single writer. The library does not choose ordering between competing callers.
- Windows ConPTY can discard a fragmented terminal-query reply's prefix even when bypassing the sample. Send complete replies in one write when possible; see the [recorded native limitation and reproducer](docs/ConPTY-Input-Limitations.md). The sample forwards bytes without parsing or repairing native terminal input.
- Drain output while the process runs. A child can block when terminal buffers fill. Process exit does not mean all output has been read.
- Cancelling I/O or `WaitForExitAsync` cancels that operation only. A cancelled write may already have sent some bytes. Unix pending I/O uses a thread-pool worker per direction and checks cancellation approximately every 50 ms while idle.
- macOS reads output ahead into a bounded queue (16 blocks of 4 KiB, plus the active reader/writer blocks). This preserves final output across native terminal close without allowing unlimited buffering. Continue draining larger output concurrently; output can still backpressure the child.
- `Terminate` forcibly stops the primary child. `Dispose` closes the streams, terminates a live primary child, and collects its exit status. Disposal is idempotent; PID and collected exit status remain available. The default owns only the primary; opt-in scope coverage is described below.
- Disposing the input stream is not a portable half-close or an EOF command. Send the application's normal exit command, or terminate/dispose the session.
- `StartAsync` snapshots launch settings before yielding. A token cancelled before launch creates no child; cancellation during startup waits for cleanup before reporting cancellation. Cancelling that token after successful startup does not stop the child. Do not mutate launch settings concurrently with the initial call.
- Unix `StartTimeout` bounds the helper handshake (15 seconds by default), independently of caller cancellation. It does not bound native creation or cleanup and is unused on Windows. Startup reports helper/exec failures to the caller. Synchronous `Start` uses the same startup path.

### Initial terminal configuration

Query support before launch, then attach options to `PtyStartInfo`:

```csharp
PtyTerminalCapabilities capabilities = PtyProcess.GetTerminalCapabilities();
PtyTerminalCapabilities required =
    PtyTerminalCapabilities.Echo |
    PtyTerminalCapabilities.CanonicalInput |
    PtyTerminalCapabilities.ReadTiming;

if ((capabilities & required) == required) {
    var start = new PtyStartInfo("/bin/sh") {
        TerminalOptions = new PtyTerminalOptions {
            Echo = false,
            CanonicalInput = false,
            MinimumReadBytes = 1,
            ReadTimeoutDeciseconds = 0
        }
    };
    await using PtyProcess process = await PtyProcess.StartAsync(start);
} else {
    // Windows currently reports None. An explicit request would throw
    // PlatformNotSupportedException before allocating a PTY or child.
}
```

| Platform | Reported launch-time controls |
| --- | --- |
| Windows x64/ARM64 | `None`; null or all-default options preserve ConPTY launch behavior |
| Linux x64/ARM64 | Raw, echo, canonical input, signal processing, control characters, read timing |
| macOS x64/ARM64 | Raw, echo, canonical input, signal processing, control characters, read timing |

`Preserve` means retain the newly allocated PTY's baseline except for requested fields; it does not copy the host terminal. Setting `CanonicalInput = false` selects noncanonical delivery without applying the other changes made by `Raw`. `Raw` uses the platform's native raw transformation and sets one-byte blocking reads (`VMIN=1`, `VTIME=0`); it is exclusive and cannot be combined with individual overrides.

Control-character properties accept byte values from 0 through 255. Use `PtyTerminalOptions.DisabledCharacter` instead of spelling the platform's disabled byte as a literal; a colliding literal is rejected. `MinimumReadBytes` is native `VMIN`, and `ReadTimeoutDeciseconds` is native `VTIME` in tenths of a second. Both require `CanonicalInput = false`.

These settings establish the child's initial state. The child may change them later; the library does not expose live query, update, or restoration. Unix applies the request to the owned slave before either launch path, reads the requested fields back, and fails startup with `IOException` if native calls fail or the request is not honored. Failed startup releases its descriptors and child resources; `PtySession.StartAsync` leaves supplied streams open. Unix still requires the packaged managed helper and an installed `dotnet` runtime.

`SendInterruptAsync` always writes byte `0x03`. A custom interrupt byte or Raw mode can make that byte ordinary input. `SendSignal` remains a separate native Unix operation. Canonical EOF is terminal input behavior and is not a portable stream half-close.

### Reusable session owner

Use `PtySession` when one component should own the process, serialize all input, forward output, drain final bytes, and clean the selected process scope:

```csharp
var sessionStart = new PtyStartInfo(
    OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh");
string lineEnding = OperatingSystem.IsWindows() ? "\r" : "\n";
string exitRequest = "exit" + lineEnding;

using var output = new MemoryStream();
await using var session = await PtySession.StartAsync(sessionStart, new(output) {
    DrainTimeout = TimeSpan.FromSeconds(5)
});

await session.WriteAsync(System.Text.Encoding.UTF8.GetBytes("echo status" + lineEnding));
await session.ShutdownAsync(new() {
    Request = System.Text.Encoding.UTF8.GetBytes(exitRequest),
    GracePeriod = TimeSpan.FromSeconds(5),
    ForceTermination = true
});
PtySessionResult result = await session.Completion;
Console.WriteLine($"Exit={result.ExitCode}; output={result.OutputStatus}");
```

`Input` is optional; its EOF stops that forwarding pump while explicit `WriteAsync` calls remain available. All PTY writes, including `SendInterruptAsync` and shutdown request bytes, share one writer. Each admitted write remains contiguous. Cancellation before admission sends no bytes; cancellation during a stream write can follow a partial native write and is never retried automatically. Keep caller memory unchanged until the returned `ValueTask` completes.

Primary exit seals ordinary input and starts `DrainTimeout`. `Completion` waits for output EOF and flush, or reports `TimedOut` when the drain interval expires. A descendant can retain a terminal after the primary exits. Session disposal always releases the selected ownership scope, even after graceful primary exit. `OutputCompletion` reports output independently, so early output EOF does not imply process exit.

Operational input, output, process-observation, and cleanup failures are retained in `PtySessionResult.Failures`; `Completion` itself returns that shared result. `DisposeAsync` joins the same finalizer and throws `AggregateException` when failures were retained. Caller cancellation of `Completion.WaitAsync` cancels only that wait. Startup cancellation owns cleanup only until `StartAsync` successfully returns.

`GetDiagnostics()` returns a detached snapshot: the current phase, input-seal state, completed byte counts, and the latest 32 lifecycle events. It contains no command, environment, input, output, transcript, or exception-message data. `DroppedEvents` reports ring-buffer eviction. Use `PtyProcess` when the caller deliberately wants direct streams and manual lifetime coordination.

### Bounded recording and replay

Attach recording to `PtySessionOptions`; existing sessions keep recording disabled:

```csharp
using var output = new MemoryStream();
using var recording = new MemoryStream();
await using var session = await PtySession.StartAsync(start, new(output) {
    Recording = new PtyRecordingOptions(recording) {
        MaxBytes = 16 * 1024 * 1024,
        LeaveOpen = true
    }
});

PtySessionResult sessionResult = await session.Completion;
PtyRecordingResult recordingResult = await session.RecordingCompletion;

recording.Position = 0;
await using var reader = await PtyRecordingReader.OpenAsync(recording);
while (await reader.ReadAsync() is { } item) {
    if (item.Kind == PtyRecordingEventKind.Output)
        await Console.OpenStandardOutput().WriteAsync(item.Output);
    else
        Console.WriteLine($"resize: {item.Size}");
}
Console.WriteLine($"recording: {reader.FinalStatus}");
```

The version 1 binary file stores the initial size, output byte frames of at most 16 KiB, successful resizes, nondecreasing elapsed `TimeSpan` ticks, and one terminal marker. Output is admitted after the configured output destination accepts the chunk; resize is admitted after the native resize succeeds. Their recorded order is the order in which those completed operations enter the recorder. It does not claim when the child observed bytes relative to a resize already in flight.

`MaxBytes` includes the file and frame headers and defaults to 16 MiB; 32 bytes is the minimum valid file. The writer reserves room for a terminal marker. Reaching the cap produces a valid `Truncated` prefix and leaves the live session running. `Complete`, `Truncated`, `Stopped`, and `Faulted` are reported through `RecordingCompletion` independently of `Completion` and `OutputCompletion`. A slow or cancellation-uncooperative recording stream can delay output, synchronous `Resize`, and finalization. `LeaveOpen = false` transfers disposal to the successfully started session; failed startup leaves the supplied stream open.

The reader validates magic, version, reserved fields, sizes, lengths, timestamp order, terminal marker, and trailing data before accepting a complete file. Its independent defaults are 64 MiB total input and 16 KiB per output frame. `ReplayAsync` copies output bytes in event order without timing delays or process launch; event iteration also exposes resize records. Input bytes are never captured. The file can contain passwords or other sensitive terminal output, so protect it as application data. Format errors and session diagnostics do not include payload bytes.

### Interrupt and controlled shutdown

For an already-running shell, with an output reader already active:

```csharp
// The child terminal's modes decide whether 0x03 interrupts a job or is input.
await process.SendInterruptAsync(cancellationToken);

// Choose the exit request for the application you launched.
var result = await process.ShutdownAsync(new PtyShutdownOptions {
    Request = System.Text.Encoding.UTF8.GetBytes(
        OperatingSystem.IsWindows() ? "exit\r" : "exit\n"),
    GracePeriod = TimeSpan.FromSeconds(5),
    ForceTermination = true,
    TerminationTimeout = TimeSpan.FromSeconds(5)
}, cancellationToken);

if (result.Status == PtyShutdownStatus.Exited) {
    // Process exit and output EOF are separate events. Keep this await bounded
    // if a descendant might retain the terminal.
    await output.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    Console.WriteLine($"Exit: {result.ExitCode}");
} else {
    // The session remains owned by the caller; choose whether to retry or dispose.
    Console.WriteLine($"Timed out; force requested: {result.ForcedTerminationRequested}");
}
```

`SendInterruptAsync` writes exactly one ETX byte; it is neither a general signal API nor proof that a job stopped. Cancellation can occur after delivery. Already-exited sessions reject interrupts.

Shutdown snapshots the options and request bytes. The grace period includes writing the request and waiting for exit; an empty request waits without writing anything. Both deadlines must be positive and at most `Int32.MaxValue` milliseconds. Force is opt-in and defaults to the primary child; set `TerminationTarget = PtyProcessTarget.OwnedScope` on an owned session for scoped escalation. The result distinguishes collected exit status (`Exited`, non-null exit code) from a deadline (`TimedOut`, null exit code). `ForcedTerminationRequested` reports whether this operation invoked termination; natural exit can race with it.

Shutdown leaves streams open and does not consume output or dispose the session. Only one shutdown may run at a time; a later retry is allowed. Caller cancellation never initiates escalation, but cannot undo delivered input or a termination request already made. Input-write failures propagate unless exit was already collected. Dispose the session when finished, including after failure or cancellation. A cancelled startup or shutdown does not promise an interruptible native cleanup deadline.

### Process-group control and descendant cleanup

Select ownership **before launch**; existing callers retain primary-only behavior:

```csharp
var start = new PtyStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") {
    Ownership = PtyProcessOwnership.PlatformScope
};
await using var process = await PtyProcess.StartAsync(start);
Task output = process.Output.CopyToAsync(Console.OpenStandardOutput());

var result = await process.ShutdownAsync(new PtyShutdownOptions {
    Request = System.Text.Encoding.UTF8.GetBytes(
        OperatingSystem.IsWindows() ? "exit\r" : "exit\n"),
    ForceTermination = true,
    TerminationTarget = PtyProcessTarget.OwnedScope
});

// A graceful primary exit does not automatically clean the remaining scope.
if (process.Capabilities.HasFlag(PtyProcessCapabilities.TerminateOwnedScope)) {
    PtyControlResult cleanup = process.RequestTermination(PtyProcessTarget.OwnedScope);
    Console.WriteLine($"Scope request: {cleanup.Status}");
}
await output.WaitAsync(TimeSpan.FromSeconds(5));
```

| Operation | Default session | PlatformScope session |
| --- | --- | --- |
| `Terminate()` / primary termination request | Primary only | Primary only |
| `RequestTermination(OwnedScope)` | Rejected | Windows job / initial Unix process group |
| `SendSignal(signal, target)` | Rejected | Unix only; both targets require anchored ownership |
| `Dispose()` / `DisposeAsync()` | Primary cleanup | Scope cleanup, primary collection, resource release |

On Windows the child is created suspended, assigned to a non-inheritable kill-on-close job, then resumed.
The job remains owned after primary exit; assignment/resume failures never fall back to an unowned launch.
Nested host-job restrictions can reject launch. No job breakaway is enabled.

On Unix the scope is the **initial process group**, not every descendant or the shell's current foreground job.
Job-control pipelines, background jobs in other groups, and detached sessions can escape that group. Terminal
closure may separately affect them. No arbitrary PID/PGID target, foreground retargeting, or process-tree sweep
is provided. Owned Linux launch requires **glibc 2.34 or newer**; the default launch path is unchanged.

Unix ownership uses native spawn of the managed helper and retains the primary's wait record until disposal.
This reserves its PID/group identity even after `WaitForExitAsync` reports exit. Each exited, undisposed owned
session retains one zombie record: **always dispose it**. The host must grant exclusive wait ownership for
these children: no competing global child reaper, PID-1 hosting, ignored SIGCHLD, or SA_NOCLDWAIT. Do not change
those settings or reap these children later. Ordinary .NET Process children can coexist. Detected loss of wait
ownership fails closed; arbitrary external reapers cannot be made race-free by a library lock.

`SendSignal(PtySignal.Interrupt, PtyProcessTarget.OwnedScope)` sends native SIGINT independently of terminal modes;
check `SignalOwnedScope` in `Capabilities` first. The other named signals are Hangup, Terminate, and Kill.
Windows rejects this API. `SendInterruptAsync` still writes one ETX byte and works according to the child's terminal modes.

`Requested` means native dispatch succeeded; `TargetUnavailable` means no target was available at observation.
Default Unix primary requests use the existing managed Process backend: a successful void Kill call returns
`DispatchUnconfirmed`, because .NET may silently skip native dispatch during an exit race. Windows and opted-in
Unix requests report native acceptance directly. None of these statuses confirms all descendants exited.
Capability flags describe support, not liveness, and remain readable after disposal.
Native permission failures remain IOExceptions. On macOS a group containing only exiting or zombie members
can also produce EPERM; the library checks that group's members before classifying delivery as unavailable,
without using enumerated PIDs as control targets or claiming every exit has completed.

Shutdown still completes on primary exit. `TerminationResult` is null without escalation; otherwise it records
its target and outcome. The original three-field result constructor/deconstruction remains valid. Cancellation
of a wait/shutdown never initiates force. Disposal independently attempts scope cleanup even after natural exit,
releases resources despite failures, and can report cleanup exceptions. For explicit control diagnostics, request
termination before disposal. If Unix permissions deny primary termination, disposal reports failure and leaves a
retained observer to reap that child on natural exit; it cannot promise to kill an inaccessible child.
Abrupt force/disposal does not guarantee lossless output or bounded native termination latency.

See the [design](docs/Process-Group-Cleanup-Design.md) and [implementation evidence](docs/Process-Group-Cleanup-Implementation-Plan.md).

## Build and verify

Install the .NET 10 SDK and .NET 8/9 runtimes. Run `build.cmd` from CMD on Windows, or `./build.sh` from SH on Unix with PowerShell installed. Tooling is compatible with Windows PowerShell 5.1. No C or Python source or build step is required.

The root contains `Icod.Pty.sln` and `Icod.Pty.csproj`. All C# sources are under the root `src/` tree, including helper sources in `src/Host/`, tests in `src/Tests/`, and the sample in `src/Sample/`. Supporting projects link their sources from these directories.

Direct commands:

```sh
dotnet build Icod.Pty.sln -c Release
dotnet test Icod.Pty.sln -c Release --no-build
dotnet pack Icod.Pty.csproj -c Release --no-build -o artifacts
dotnet run --project samples/Icod.Pty.Sample -f net10.0 -- --smoke
```

The sample accepts an executable followed by arguments and forwards input immediately. With no arguments it opens the platform shell. It copies terminal dimensions, forwards resize changes, and restores host modes and Windows code pages on normal exit and handled failures. Use `--line` for line input or redirected streams, `--interactive` to state the default explicitly, or `--` before the executable. The host terminal renders output.

Noninteractive verification switches are `--smoke`, `--lifecycle-smoke`, `--cancel-start-smoke`, `--interrupt-smoke`, `--scope-smoke`, `--session-smoke`, `--session-scope-smoke`, `--terminal-config-smoke`, and `--recording-smoke`. The package verifier runs each against a fresh package consumer on all three frameworks and published output; internal child switches support the behavioral checks.

### Windows laptop acceptance

From CMD on the minimum supported Windows build:

```cmd
build.cmd
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- --smoke
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- --session-smoke
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- --session-scope-smoke
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- --terminal-config-smoke
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- cmd.exe
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- powershell.exe -NoLogo -NoProfile
```

Follow the [sample acceptance guide](samples/README.md) for editing/history/Tab/Escape, Ctrl+C, resize, and restoration checks in the original shell. Laptop acceptance of the new interactive host remains separate from CI's nested-PTY fixture coverage.

## Release

The workflows are adapted from `uniblab/.github` for one DLL package. A `v<semver>` tag on the default branch must match `Version` in `Icod.Pty.csproj`. Release validation runs on all six platforms before publication to NuGet.org through trusted publishing in the `Release` environment. The workflow also publishes to GitHub Packages and creates release assets with checksums. The helper, sample, and test programs are not separate release packages or executable archives.

## License

Copyright (c) 2026 Timothy J. Bruce <uniblab@hotmail.com>. Licensed under the GNU Lesser General Public License version 3 or later; see [LICENSE](LICENSE).
