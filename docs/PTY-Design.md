# PTY design

## Approved requirements

C# 13; net8.0, net9.0 and net10.0; AnyCPU assemblies; Windows, Linux and macOS on x64 and ARM64. Windows support starts at 10.0.26200.9457. One NuGet package with no third-party runtime dependencies. OS APIs are invoked from C#. Tooling uses CMD, SH and PowerShell 5.1-compatible scripts.

## API and lifetime

PtyStartInfo supplies executable, separate literal arguments, environment overrides (null removes), working directory, initial dimensions and Unix startup settings. Settings must not be modified concurrently with Start. PtyProcess exposes raw input/output streams, resize, PID, exit status, cancellation-aware waiting and termination. Dimensions are 1..32767 character cells.

Use one reader and one writer concurrently. Read output while the child runs; waiting without draining can block a child on a full terminal buffer. Process exit and output EOF are separate events. Dispose closes streams and forcibly terminates/reaps the primary child. Detached descendants are not owned. Cancellation affects the requested operation; it does not terminate the process. A cancelled write may already have transferred bytes.

## Windows

ConPTY with synchronous native pipe ends and asynchronous managed pipe ends. CreateProcessW receives an explicit executable, CRT-quoted arguments and a Unicode environment block. A dedicated wait task collects exit status and closes ConPTY after exit, allowing output to drain. Supported Windows builds provide nonblocking ClosePseudoConsole.

## Unix

The parent allocates a PTY and starts a fresh AnyCPU C# helper using an installed dotnet host. The helper creates a session, acquires the controlling terminal, redirects standard descriptors, restores signals and execs the target. No managed code runs in a post-fork CLR child. The helper PID becomes the target PID.

Configuration travels through a private pipe. A ready byte and close-on-exec status pipe EOF acknowledge startup; exec errors travel back through that pipe. Startup timeout terminates/reaps the helper. Parent I/O uses nonblocking descriptors and poll, with cancellation/disposal checks every 50 ms while idle. Each pending asynchronous direction occupies a thread-pool worker.

## Distribution

The helper targets net8.0 with LatestMajor roll-forward. It is embedded as managed assets in the same package and copied by buildTransitive targets to build/publish output. Unix hosts require an installed .NET 8, 9 or 10 runtime even for self-contained consumers. Helper assets remain separate in single-file deployments. NativeAOT and trimming are not currently validated.
