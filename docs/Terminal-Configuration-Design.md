# Terminal configuration and capability discovery

**Status:** implementation and hosted acceptance are complete in PR #5; manual Windows laptop observation is pending.

**Decision:** option 7 plus a focused portion of option 4. This milestone adds explicit child-terminal configuration and truthful discovery of the controls the backend implements. It does not add general tracing or terminal emulation.

## Intent and first-increment boundary

A consumer should be able to launch a child with deliberate echo, canonical/noncanonical input, terminal-generated signals, control characters, and read timing, without changing the hosting application's console modes. Existing callers must retain the current defaults.

The proposed first increment is **launch-time configuration**. Configure the newly allocated Unix slave before starting either helper launch path, verify the requested settings, then allow child application code to run. Both `PtyProcess` and `PtySession` use the same captured launch settings. The child remains free to change its terminal afterward; a successful launch is not a lifetime enforcement guarantee.

Alternatives considered:

- **Launch-time configuration (selected proposal):** an observable initial condition with no child-versus-host mutation race; works with both existing ownership policies.
- **Live query/update and restoration scopes (deferred):** useful later, but requires contracts for competing child writes, stale snapshots, partial native updates, terminal disappearance, and disposal races.
- **Portable emulation of unsupported controls (rejected):** rewriting input/output or changing the parent console cannot faithfully reproduce another backend's line discipline.

Approval of this design explicitly approves this first-increment boundary. Broader live controls remain under option 7 in the main menu.

## Global constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.
- Preserve both ownership policies, input/output byte transport, startup cancellation, shutdown, and session completion contracts.
- No new runtime package dependencies, version bump, tag, merge, or publication in this planning PR.

## Proposed public surface

TC01 passed the native feasibility gate in [workflow run 53](https://github.com/uniblab/Icod.Pty/actions/runs/37350262099). The following additive shape and numeric values are frozen for this increment. Any later evidence-driven change must be documented and reviewed before dependent implementation.

| Member | Contract |
| --- | --- |
| `PtyStartInfo.TerminalOptions : PtyTerminalOptions?` | Default `null`; copied during synchronous launch capture before the first asynchronous yield. |
| `PtyTerminalOptions.Profile : PtyTerminalProfile` | `Preserve = 0` (default), `Raw = 1`. Preserve means preserve the newly allocated PTY baseline, not inherit the host's terminal. |
| `Echo : bool?` | Null preserves; false disables normal and newline echo; true enables normal echo. |
| `CanonicalInput : bool?` | Null preserves; false selects noncanonical input, not an implicit raw profile. |
| `SignalProcessing : bool?` | Controls terminal-generated signals; does not change native `SendSignal`. |
| `InterruptCharacter`, `EndOfFileCharacter`, `EraseCharacter : int?` | Null preserves; 0 through 255 requests one byte; `PtyTerminalOptions.DisabledCharacter = -1` requests the native disabled value. These are bytes, not Unicode characters. |
| `MinimumReadBytes`, `ReadTimeoutDeciseconds : int?` | Null preserves; 0 through 255 maps to VMIN/VTIME. Explicit timing requires `CanonicalInput = false`. |
| `PtyProcess.GetTerminalCapabilities() : PtyTerminalCapabilities` | Static, side-effect-free prelaunch discovery for the current backend and process architecture; does not create a child or inspect the host console. |

`PtyTerminalCapabilities` is a separate flags enum: `None = 0`, `RawProfile = 1`, `Echo = 2`, `CanonicalInput = 4`, `SignalProcessing = 8`, `ControlCharacters = 16`, `ReadTiming = 32`. It does not reuse or renumber `PtyProcessCapabilities`. Consumers launching sessions use the same prelaunch discovery method; no redundant session-only configuration API is needed.

Discovery describes implemented operations, not an active child's current settings, liveness, native permissions, or guaranteed launch success. Unknown/unsupported platforms and unsupported process architectures return `None`; ordinary process launch keeps its existing platform checks.

### Validation and deterministic interpretation

- `null` and an all-default options object are equivalent no-ops, including on Windows. Do not make extra terminal syscalls for either.
- Snapshot every scalar and the options reference before yielding. Mutating the original object after capture cannot change the launch; concurrent mutation during capture remains unsupported, as for existing settings.
- Reject undefined profiles and out-of-range integers with argument exceptions before child creation.
- Raw is an exclusive preset: reject Raw combined with any explicit nullable override. This avoids undocumented ordering between a preset and custom settings.
- Preserve plus overrides modifies only the requested logical controls. `Echo = false` clears both ECHO and ECHONL; unrelated echo flags remain untouched. `Echo = true` sets ECHO and preserves ECHONL.
- Character settings may be stored while their associated canonical/signal mode is inactive; document when they take effect. A requested byte equal to the platform's disabled sentinel is rejected as ambiguous; use `DisabledCharacter` instead.
- Explicit VMIN/VTIME with CanonicalInput null or true is invalid. Noncanonical mode without explicit timing preserves native timing; the Raw preset uses VMIN = 1 and VTIME = 0.
- A request needing unsupported capability flags fails with `PlatformNotSupportedException` before allocating a PTY or starting a child. Never silently ignore it, downgrade it, or select a different ownership policy.
- Capability validation is separate from native application failure. Native get/set/readback failures become `IOException` with operation/native-error context where available; they must not contain terminal input or output.

## Platform contract and feasibility gate

| Requested operation | Linux x64/ARM64 | macOS x64/ARM64 | Windows x64/ARM64 |
| --- | --- | --- | --- |
| Default/no-op configuration | Preserve existing launch | Preserve existing launch | Preserve existing launch |
| Explicit controls and Raw profile | Target: native termios before launch, subject to TC01 | Target: native termios before launch, subject to TC01 | Report unsupported; reject before child creation |
| Host console configuration | Never changed | Never changed | Never changed |
| Live child-mode query/update | Deferred | Deferred | Deferred |

Windows ConPTY is not a termios endpoint. The proposed Windows result is deliberately `None` for these optional launch controls, not a claim that Windows applications cannot configure their own console. Do not add AttachConsole, injected child code, a Windows shim, terminal escape-sequence guesses, or parent-console SetConsoleMode calls to simulate support. Such an expansion requires a separate proposal.

TC01 proved the Unix path with a pure-C# native probe on all four Unix OS/architecture combinations. The probe allocates a new PTY, reads the slave state, applies Preserve/custom/Raw states, reads each back, and compares the result with the first state reported by a separately spawned managed child before Console initialization. Existing default launches also produced the same baseline semantics through both ownership policies.

### TC01 native evidence

| ABI | Layout and control-character indices | Disabled byte | Verified Raw transformation |
| --- | --- | --- | --- |
| Linux x64/ARM64 | 60 bytes; four 32-bit flags at 0/4/8/12, line byte at 16, 32-byte `c_cc` at 17, speeds at 52/56. VINTR=0, VEOF=4, VERASE=2, VMIN=6, VTIME=5. | 0 | glibc `cfmakeraw`: clear input mask `0x05eb`, OPOST, local mask `0x804b`, and control mask `0x0130`; set CS8 `0x0030`, VMIN=1, VTIME=0. |
| Darwin x64/ARM64 | 72 bytes; four 64-bit flags at 0/8/16/24, 20-byte `c_cc` at 32, speeds at 56/64. VINTR=8, VEOF=0, VERASE=3, VMIN=16, VTIME=17. | 255 | Apple `cfmakeraw`: clear input mask `0x27fe` then set IGNBRK; clear OPOST; clear local mask `0xa040059e`; clear control mask `0x1300` then set CS8/CREAD `0x0b00`; set VMIN=1, VTIME=0. |

The probe ignored ABI padding and Darwin's transient PENDIN bit in semantic comparisons. It verified noncanonical/no-echo configuration, VINTR/VEOF/VERASE, Raw timing, readback, and child-first-state equality on Linux and macOS x64/ARM64. Windows builds and default ConPTY behavior passed without any termios or host-console configuration path. Executable rejection of the frozen explicit options follows in TC02/TC05, after those request types exist.

## Native implementation boundary

Add a small `UnixTerminalConfiguration` adapter around the existing slave descriptor in `UnixBackend.StartAsync`. It runs inside the existing descriptor-cleanup try/finally, after native handles are safely owned and before either `UnixSpawn.StartAsync` or the primary-only `Process.Start` path.

1. Obtain the freshly allocated slave's settings with tcgetattr.
2. Build the requested state by changing defined fields only. Use platform-specific structs/constants rather than raw public native masks.
3. For Raw, apply the platform cfmakeraw transformation and explicitly set VMIN = 1 and VTIME = 0. Document the verified masks in the TC01 evidence; Raw is not a terminal emulator, encoding conversion, or serial-port API.
4. Apply with TCSANOW; this first increment does not drain or flush queues as a configuration side effect.
5. Read back settings. Compare requested semantic fields, including Raw's defined transformation, not padding or transient native bits. A successful tcsetattr alone is not sufficient evidence that every requested field took effect.
6. Only after successful verification, proceed with existing child launch. On error, close the new terminal resources and fail startup; never launch a child with a partially accepted request.

The parent already owns the slave during startup, so this design does not require a new helper wire protocol or a new retained slave handle. Keep the captured options parent-only in `LaunchConfiguration` under the existing `PTY_HELPER` compilation boundary. The helper's JSON remains unchanged. Verify that both the library and helper still compile.

Cancellation checkpoints belong before/after native configuration and before helper launch. Native calls themselves are not promised interruptible. Failed `PtySession.StartAsync` must still leave supplied streams caller-owned and open. There is no restoration operation at disposal: this is a newly created child terminal, not borrowed host state.

## Focused diagnostics and behavioral limits

The focused portion of option 4 consists of prelaunch capability flags, exact unsupported-setting validation, and native get/set/readback failure context. Existing session diagnostics remain bounded and content-free. No new lifecycle journal events, transcript capture, metrics exporters, callback subscriptions, or general startup tracing are required.

`SendInterruptAsync` still writes exactly ETX (0x03); it must not silently change to the configured interrupt character. A caller using a custom interrupt character writes that byte explicitly, or uses the existing supported native signal API. EOF characters affect canonical reads; they do not close the PTY transport or establish portable stream half-close semantics. VMIN/VTIME are child-terminal read semantics, not Icod.Pty stream timeouts or cancellation budgets.

A child can replace initial settings immediately. Tests asserting initial state must use a managed native-reading fixture that reports its first state before changing it, rather than an interactive shell which can configure itself.

## Acceptance

- Null/default requests preserve all existing launch, ownership, sample, and session tests.
- Invalid/unsupported settings fail before launch; Windows verifies rejection without host console mutation.
- A controlled Unix child observes configured settings on its first instruction path and proves immediate noncanonical input, canonical delimiter behavior, no echo, custom control characters, and raw ETX delivery.
- Configuration failures and cancellation leave no new descriptors, helper processes, or scope resources behind; session stream ownership remains correct.
- Both ownership policies and all three launch entry points (`Start`, `StartAsync`, session startup) are covered.
- All six platform jobs and three target frameworks pass; package consumers exercise a new `--terminal-config-smoke` mode, including meaningful Windows unsupported/no-op assertions rather than a skipped test.
- Documentation distinguishes initial configuration, current child state, host console state, capabilities, and unsupported operations. Manual laptop observations are recorded only when supplied.

## Native references

- [POSIX termios definitions](https://pubs.opengroup.org/onlinepubs/9799919799/basedefs/termios.h.html).
- [POSIX tcsetattr and readback semantics](https://pubs.opengroup.org/onlinepubs/009696799/functions/tcsetattr.html).
- [Linux termios and cfmakeraw reference](https://man7.org/linux/man-pages/man3/termios.3.html).
- [Apple Darwin termios layout and constants](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/termios.h).
- [Apple libc cfmakeraw implementation](https://github.com/apple-oss-distributions/Libc/blob/main/gen/FreeBSD/termios.c).
- [Microsoft CreatePseudoConsole](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole) and [pseudoconsole session creation](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session).

These references and workflow run 53 supply the TC01 evidence. Production configuration behavior begins in TC02-TC04.
