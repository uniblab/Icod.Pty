# Interactive sample acceptance

The default sample uses `PtySession` to forward terminal bytes immediately, including escape sequences and Ctrl+C. It copies the host's initial size and checks for size changes every 100 ms. It does not parse or render terminal output; the host terminal does that. Run it from a real terminal. Use `--line` for deliberately line-oriented or redirected input/output, `--session-smoke` for the reusable-owner package check, `--recording-smoke` for bounded output/resize recording and replay, and `--automation-smoke` for live matching and ordered scripts.

Build once from the repository root:

```sh
dotnet build Icod.Pty.sln -c Release
```

## Windows CMD

Use Windows build **10.0.26200.9457 or later**. From CMD:

```bat
ver
dotnet --list-runtimes
dotnet run --project samples\Icod.Pty.Sample -c Release -f net10.0 --no-build -- cmd.exe
```

Inside the child shell, type `echo hello`, use Up/Down to recall it, edit with Left/Right/Home/End, try Tab completion, and use Escape to clear a partially entered command. Each key should work immediately and typed text should appear once. Run `ping -t 127.0.0.1`, resize the terminal while it produces output, then press Ctrl+C. The child shell should remain usable. Run `ver`, then `exit`. In the original CMD shell, verify normal echo, editing, and history still work.

## Windows PowerShell 5.1

From Windows PowerShell (not PowerShell 7):

```powershell
$PSVersionTable.PSVersion
[System.Environment]::OSVersion.Version
dotnet run --project samples\Icod.Pty.Sample -c Release -f net10.0 --no-build -- powershell.exe -NoLogo -NoProfile
```

Inside the child, check editing, history, Tab completion, and Escape. Run `while ($true) { Get-Date; Start-Sleep -Seconds 1 }`, resize during output, and press Ctrl+C. Run `$PSVersionTable.PSVersion` to confirm the shell is still usable, then `exit`. Check editing and echo again in the original shell. These commands are compatible with PowerShell 5.1.

## Linux and macOS

From an SH-compatible shell:

```sh
uname -sm
dotnet --list-runtimes
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- /bin/sh
```

Run `printf 'hello\n'`, then `while :; do date; sleep 1; done`. Resize the terminal, interrupt with Ctrl+C, and check that another command works. Run `exit` and verify normal echo and editing in the original shell. Plain `/bin/sh` may not provide history or Tab completion; to check those, explicitly launch an installed interactive shell that supports them.

Optionally run an already installed full-screen editor, resize it, enter and leave its editing modes, and quit back through the child shell. Record the editor and version separately; CI does not require an external editor.

## Results to record

Record OS build/architecture, host terminal, child shell/version, .NET runtime, and observations for immediate keys, no extra echo, Ctrl+C, resize, exit, and restored host state. Repeat with `-f net8.0` or `-f net9.0` when checking those runtimes.

The earlier Windows laptop smoke/CMD/PowerShell checks cover the foundation. Acceptance of this interactive milestone on that laptop is **pending**; automated nested-PTY fixtures do not substitute for these manual observations.

## Lifetimes and limits

On host-input EOF the session permanently seals ordinary input, waits five seconds for the primary child, then requests forced primary termination and waits up to five more seconds. It sends no guessed shell command. After primary exit the session gives output five seconds to reach EOF and flush. A descendant retaining the terminal or a blocked host output can trigger a drain timeout; incomplete output is reported as failure. Native output writes are cancellable and finish before host restoration. If the host cannot accept the error message within 250 ms, the sample still returns failure. Host modes and Windows code pages are restored during normal disposal, including handled failures. Force-killing the sample itself cannot run restoration code.

Descendant lifetime follows the native backend: Linux may keep the terminal open, while macOS terminal revocation and Windows ConPTY teardown can yield EOF at primary exit. The library does not own detached descendants.

Windows ConPTY may discard the prefix of a terminal-query reply fragmented across native input writes. The sample preserves the bytes it receives, but cannot repair native input loss. See the [evidence and opt-in reproducer](../docs/ConPTY-Input-Limitations.md).

`--line` uses line input and is not an interactive terminal host. The default executable is `%COMSPEC%` (falling back to `cmd.exe`) on Windows and `/bin/sh` on Unix. `--interactive` is optional; `--` ends sample options before the executable. Arguments are passed as individual arguments, without shell expansion.

## Process-scope acceptance

From CMD or Windows PowerShell 5.1, on Windows 10.0.26200.9457 or newer:

```text
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --scope-smoke
```

The same command works in SH on Linux/macOS. Repeat with net8.0 and net9.0. Expected output is
`PTY process-scope smoke check passed.` The check launches a managed descendant, observes primary exit 37
while that descendant remains alive, requests owned-scope termination, independently observes descendant exit,
and disposes the session. It does not rely on request success alone.

Afterward, run `ver` in CMD or `$PSVersionTable.PSVersion` in PowerShell, type and edit a command, and confirm
normal echo and history. Also run `--lifecycle-smoke` to check cooperative primary exit. Record OS/architecture,
framework, command output, and host restoration observations. **Reported 2026-10-04:** the user checked out `feature/process-group-cleanup-roadmap` and ran the Release net10.0 command without `--no-build` on the previously identified Windows x64 laptop; output was `PTY process-scope smoke check passed.` Other-framework, lifecycle, and explicit console-restoration observations remain unreported.

The regular interactive sample still uses primary-only ownership; `--scope-smoke` explicitly opts in.
The library README describes Windows job coverage and Unix initial-group/host-reaper limits.

## Session-owner acceptance

Run these checks on each target framework:

```text
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --session-smoke
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --session-scope-smoke
```

The expected messages are `PTY session smoke check passed.` and `PTY session scope smoke check passed.` The first check exercises owned output forwarding, primary exit, EOF/flush, and the final result. The second opts into platform-scope ownership, lets a descendant retain the terminal after primary exit 37, and verifies that session finalization stops the descendant. Linux observes drain expiry because the descendant retains the terminal; Windows ConPTY and macOS terminal revocation report EOF at primary exit. Repeat from CMD and Windows PowerShell 5.1 on the minimum supported Windows build and record host restoration separately.

**Reported 2026-10-05:** both commands passed in a Release net10.0 run on the identified Windows x64 laptop. Interactive CMD/Windows PowerShell 5.1 checks for Ctrl+C, resize, and restored host editing/history remain separate pending observations.

## Terminal-configuration acceptance

The terminal-configuration smoke check is noninteractive. On Unix it launches a child with echo disabled and noncanonical one-byte reads, verifies that the child observes those modes before managed console initialization, sends one byte, and requires an un-echoed native acknowledgement. On Windows it verifies `PtyTerminalCapabilities.None`, rejects an explicit request before child creation, and completes a default launch.

From CMD:

```bat
dotnet run --project samples\Icod.Pty.Sample -c Release -f net10.0 --no-build -- --terminal-config-smoke
```

From SH:

```sh
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --terminal-config-smoke
```

From Windows PowerShell 5.1:

```powershell
dotnet run --project samples\Icod.Pty.Sample -c Release -f net10.0 --no-build -- --terminal-config-smoke
```

Expected output is `PTY terminal configuration smoke check passed.` Repeat for net8.0 and net9.0 when those runtimes are installed. The check does not mutate the interactive host console. Record manual host restoration separately from this redirected smoke result.

## Deployment-portability acceptance

`--invalid-host-smoke` is the focused startup-failure check used by the published-consumer matrix. On Unix it supplies a nonexistent `DotNetHostPath` to `PtyProcess` and `PtySession` under both ownership policies, requires a bounded failure, and verifies that failed session startup leaves caller streams open. On Windows it verifies that the Unix-only host override does not affect either ConPTY ownership path.

```text
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --invalid-host-smoke
```

Expected output is `PTY invalid-host cleanup smoke check passed.` The package harness also runs this check from framework-dependent, self-contained, single-file, and trimmed final apphosts. It moves complete publish trees and mutates copied helper layouts; it does not execute the sample or library from repository build output. See the [published application support table](../README.md#published-application-support) for verified RIDs, frameworks, external helper files, and runtime prerequisites.

## Recording and replay acceptance

The recording check launches two managed children through `PtySession`. The first records raw binary output plus a successful resize, validates the file, and requires replayed output to match the bytes accepted by the session destination. The second uses the 32-byte minimum cap, requires a valid `Truncated` prefix while live output still completes, and verifies that removing one byte makes the reader reject the file. Captured terminal bytes are never printed.

From CMD or Windows PowerShell 5.1:

```text
dotnet run --project samples\Icod.Pty.Sample -c Release -f net10.0 --no-build -- --recording-smoke
```

From SH:

```sh
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --recording-smoke
```

Expected output is `PTY recording smoke check passed.` Repeat for net8.0 and net9.0 when those runtimes are installed. This noninteractive check does not capture input or mutate the host console. Record Windows laptop results separately from hosted CI.

**Reported 2026-10-06:** the user ran the CMD command above without `--no-build`, in Release/net10.0
on the previously identified Windows x64 laptop (Windows 10.0.26200.9457), and reported
`PTY recording smoke check passed.` The checked-out commit was not shown. Manual net8.0/net9.0
runs remain unreported; this noninteractive result does not establish interactive host restoration.

## Live automation acceptance

The automation check starts a managed child through `PtySession`, captures an early prompt, sends a line, matches one byte pattern spanning two child writes, consumes two consecutive suffix matches, verifies a timed-out step and retry without moving the cursor, then sends the child's exit request. It explicitly disposes its session and prints no child payload bytes. Transport-independent tests separately cover NUL, invalid UTF-8, and every byte boundary; the native Windows fixture stays in the portable ASCII range because console output code pages can translate non-ASCII writes before ConPTY emits terminal data.

From CMD or Windows PowerShell 5.1:

```text
dotnet run --project samples\Icod.Pty.Sample -c Release -f net10.0 --no-build -- --automation-smoke
```

From SH:

```sh
dotnet run --project samples/Icod.Pty.Sample -c Release -f net10.0 --no-build -- --automation-smoke
```

Expected output is `PTY automation smoke check passed.` Repeat for net8.0 and net9.0 when those runtimes are installed. The check does not change the host console's code pages or record/send payloads to its output. Hosted CI passed this mode from exact-package and published consumers on six platform/RID jobs at runtime head `8c1983cc254745f255fe01a27232d237ef781873` in [run 110](https://github.com/uniblab/Icod.Pty/actions/runs/37533280800). On 2026-10-07, the user reported that exact success message from a Windows x64 laptop Release/net10.0 run. The checked-out commit was not shown; manual net8.0/net9.0 runs remain unreported.
