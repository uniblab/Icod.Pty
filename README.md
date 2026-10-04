# Icod.Pty

Icod.Pty hosts child processes in a pseudoterminal. It provides asynchronous startup, raw byte input/output, terminal resizing, Ctrl+C input, controlled shutdown, explicit process-scope ownership, native Unix signals, exit status, and deterministic cleanup.

The library is written in **C# 13**, targets **net8.0, net9.0, and net10.0**, and builds as **AnyCPU**. One NuGet package contains all three library targets and the managed Unix helper. There are no third-party runtime packages or native binaries to build.

Development direction and deferred alternatives are recorded in the [main roadmap](ROADMAP.md). The [interactive hosting design](docs/Interactive-Hosting-Design.md) defines the contracts, with implementation and acceptance evidence in the [development roadmap](docs/Interactive-Hosting-Implementation-Plan.md).

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

**Unix requires an installed .NET 8, 9, or 10 runtime and its `dotnet` host**, including when your application is self-contained. The helper targets .NET 8 and rolls forward to the newest installed major runtime. You can set `PtyStartInfo.DotNetHostPath` explicitly. NuGet copies the `Icod.Pty.Host` directory into application build and publish output; distribute that directory with your application. Do not exclude the package's `buildTransitive` assets. NativeAOT and trimming are not currently validated.

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
Neither confirms all descendants exited. Capability flags describe support, not liveness, and remain readable after disposal.
Native permission failures remain IOExceptions. On macOS a zombie-only group also produces EPERM; the library
checks that group's members before classifying it unavailable, without using enumerated PIDs as control targets.

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

Noninteractive verification switches are `--smoke`, `--lifecycle-smoke`, `--cancel-start-smoke`, `--interrupt-smoke`, and `--scope-smoke`. The package verifier runs each against a fresh package consumer on all three frameworks and published net10.0 output; `--interrupt-child` is the managed counterpart used by the interrupt check.

### Windows laptop acceptance

From CMD on the minimum supported Windows build:

```cmd
build.cmd
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- --smoke
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- cmd.exe
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- powershell.exe -NoLogo -NoProfile
```

Follow the [sample acceptance guide](samples/README.md) for editing/history/Tab/Escape, Ctrl+C, resize, and restoration checks in the original shell. Laptop acceptance of the new interactive host remains separate from CI's nested-PTY fixture coverage.

## Release

The workflows are adapted from `uniblab/.github` for one DLL package. A `v<semver>` tag on the default branch must match `Version` in `Icod.Pty.csproj`. Release validation runs on all six platforms before publication to NuGet.org through trusted publishing in the `Release` environment. The workflow also publishes to GitHub Packages and creates release assets with checksums. The helper, sample, and test programs are not separate release packages or executable archives.

## License

Copyright (c) 2026 Timothy J. Bruce <uniblab@hotmail.com>. Licensed under the GNU Lesser General Public License version 3 or later; see [LICENSE](LICENSE).
