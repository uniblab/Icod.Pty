# Interactive sample acceptance

The default sample uses `PtySession` to forward terminal bytes immediately, including escape sequences and Ctrl+C. It copies the host's initial size and checks for size changes every 100 ms. It does not parse or render terminal output; the host terminal does that. Run it from a real terminal. Use `--line` for deliberately line-oriented or redirected input, and `--session-smoke` for the reusable-owner package check.

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

The expected messages are `PTY session smoke check passed.` and `PTY session scope smoke check passed.` The first check exercises owned output forwarding, primary exit, EOF/flush, and the final result. The second opts into platform-scope ownership, lets a descendant retain the terminal after primary exit 37, observes drain expiry, and verifies that session finalization stops the descendant. Repeat from CMD and Windows PowerShell 5.1 on the minimum supported Windows build and record host restoration separately.
