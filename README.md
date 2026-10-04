# Icod.Pty

Icod.Pty hosts child processes in a pseudoterminal. It provides raw byte input/output, terminal resizing, exit status, cancellation-aware waiting, termination, and deterministic cleanup.

The library is written in **C# 13**, targets **net8.0, net9.0, and net10.0**, and builds as **AnyCPU**. One NuGet package contains all three library targets and the managed Unix helper. There are no third-party runtime packages or native binaries to build.

## Platforms

| Operating system | Architectures | Backend |
| --- | --- | --- |
| Windows, starting at `10.0.26200.9457` | x64, ARM64 | Windows ConPTY |
| Linux | x64, ARM64 | OS PTY APIs and managed helper |
| macOS | x64, ARM64 | OS PTY APIs and managed helper |

CI exercises all six OS/architecture combinations and all three target frameworks, including real PTY integration tests and NuGet consumer checks. Linux CI uses Ubuntu 24.04. AnyCPU lets the same assemblies run on either supported architecture; native API calling conventions are selected at runtime. 32-bit processes are outside the support contract.

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

await using var process = PtyProcess.Start(start);
Task output = process.Output.CopyToAsync(Console.OpenStandardOutput());
int exitCode = await process.WaitForExitAsync();
await output;
```

For a long-lived process, write bytes to `Input` and call `Resize(new PtySize(columns, rows))`. For example, terminal Enter is normally carriage return (`\r`) on Windows and line feed (`\n`) with the default Unix terminal settings.

- Arguments are separate literal values; Icod.Pty performs no shell expansion. Use an explicit shell/interpreter for scripts or shell syntax.
- `WorkingDirectory` defaults to the current directory. Environment variables are inherited; entries in `Environment` override them, and a null value removes one. Unix defaults `TERM` to `xterm-256color` only when absent.
- Use one reader and one writer concurrently. The output combines standard output and standard error and may contain VT escape sequences, echo, and terminal line-ending conversions. Icod.Pty does not render or parse terminal output.
- Drain output while the process runs. A child can block when terminal buffers fill. Process exit does not mean all output has been read.
- Cancelling I/O or `WaitForExitAsync` cancels that operation only. A cancelled write may already have sent some bytes. Unix pending I/O uses a thread-pool worker per direction and checks cancellation approximately every 50 ms while idle.
- `Terminate` forcibly stops the primary child. `Dispose` closes the streams, terminates a live primary child, and collects its exit status. Disposal is idempotent; PID and collected exit status remain available. Detached descendants are not owned.
- Disposing the input stream is not a portable half-close or an EOF command. Send the application's normal exit command, or terminate/dispose the session.
- Unix `StartTimeout` bounds the helper handshake (15 seconds by default). Startup reports helper/exec failures to the caller.

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

The sample accepts an executable followed by arguments and forwards lines to it. With no arguments it opens the platform shell. It is a line-oriented demonstration, not a terminal emulator.

### Windows laptop acceptance

From CMD on the minimum supported Windows build:

```cmd
build.cmd
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- --smoke
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- cmd.exe
dotnet run --project samples\Icod.Pty.Sample -f net10.0 -- powershell.exe -NoLogo -NoProfile
```

Try a command in each shell, then `exit`. The integration suite separately verifies resize, Unicode arguments/I/O, cancellation, final output, and cleanup.

## Release

The workflows are adapted from `uniblab/.github` for one DLL package. A `v<semver>` tag on the default branch must match `Version` in `Icod.Pty.csproj`. Release validation runs on all six platforms before publication to NuGet.org through trusted publishing in the `Release` environment. The workflow also publishes to GitHub Packages and creates release assets with checksums. The helper, sample, and test programs are not separate release packages or executable archives.

## License

Copyright (c) 2026 Timothy J. Bruce <uniblab@hotmail.com>. Licensed under the GNU Lesser General Public License version 3 or later; see [LICENSE](LICENSE).
