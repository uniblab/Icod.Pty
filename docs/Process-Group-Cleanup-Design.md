# Process-Group Control and Descendant Cleanup Design

**Status:** approved for implementation on 2026-10-04; PG01 native feasibility passed on six platforms; implementation and lifecycle acceptance are in progress.
Public ownership APIs are not implemented yet. Findings below qualify the proposed native mechanism.
**Companion:** [development roadmap](Process-Group-Cleanup-Implementation-Plan.md).

## Purpose and success criteria

Consumers hosting shells, build tools, and interactive programs need an explicit way to control more than
the primary child. The present library's termination, shutdown escalation, and disposal target that child.
A descendant can outlive it or retain a terminal handle.

Add opt-in platform scope ownership, targeted native control, and useful outcomes without changing defaults.
Success means callers know precisely which processes are targeted, can request cleanup after primary exit
where the owned scope remains valid, and cannot confuse request acceptance with complete descendant exit.

## Constraints

- C# 13; net8.0, net9.0, and net10.0; AnyCPU assemblies.
- Windows, Linux, and macOS, each on x64 and ARM64.
- Minimum supported Windows build: 10.0.26200.9457.
- CMD, SH, and PowerShell 5.1-compatible tooling; no C or Python.
- Root solution and library project; every C# source file under root `src/`.
- One NuGet library package; retain LGPL-3.0-or-later and the shared repository conventions.
- Unix uses OS PTYs and the managed helper, with an installed .NET runtime and `dotnet` host.

No new runtime package or bundled native executable is planned. Native OS calls remain C# interop.
Keep the root solution/project and existing test, helper, sample, and packaging layout.

## Alternatives and selected architecture

1. **Primary-only control:** retain as the default. It cannot satisfy explicit descendant cleanup.
2. **Enumerate descendants and kill discovered PIDs:** reject as the ownership mechanism. Ancestry changes,
   concurrent launches, reparenting, and identifier reuse prevent this from establishing durable ownership.
3. **Platform scope established during launch:** selected. Use a Windows job and the Unix initial process
   group. Their membership and guarantees are deliberately different.

The Unix group needs a lifetime anchor, not merely the primary PID copied into a field.
The mechanism validated in PG01 is native launch/wait ownership for opted-in sessions:
start the existing managed helper through `posix_spawn`, observe primary termination without reaping,
then release the retained child identity only during final disposal.
The helper still becomes the target through `execve`; it does not become a persistent broker.
Default Unix sessions retain their existing `System.Diagnostics.Process` path.

PG01 proved native spawn/wait, .NET child-reaping coexistence, and post-leader group control on Linux/macOS
x64/ARM64; the six-platform gate passed in run 37199198321. Production controls and initial lifecycle tests
also passed in run 37201220251. The anchor still requires the documented exclusive-wait host preconditions.

## Ownership and targeting

Public names below are implemented; revisions must update both documents.

```csharp
public enum PtyProcessOwnership { PrimaryProcess, PlatformScope }
public enum PtyProcessTarget { PrimaryProcess, OwnedScope }
public enum PtySignal { Hangup, Interrupt, Terminate, Kill }
public enum PtyControlStatus { Requested, TargetUnavailable }

[Flags]
public enum PtyProcessCapabilities {
    None = 0,
    TerminateOwnedScope = 1,
    SignalPrimaryProcess = 2,
    SignalOwnedScope = 4
}

public readonly record struct PtyControlResult(
    PtyProcessTarget Target, PtyControlStatus Status);
```

Add `PtyStartInfo.Ownership`, default `PrimaryProcess`, captured synchronously with the launch settings.
Reject unknown enum values before any child is created. There is no conversion of a running session into
owned-scope mode.

Add to `PtyProcess`:

```csharp
public PtyProcessOwnership Ownership { get; }
public PtyProcessCapabilities Capabilities { get; }
public PtyControlResult RequestTermination(PtyProcessTarget target);
public PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target);
```

These methods issue synchronous native requests; they do not wait for descendant exit.
Existing `Terminate()` remains primary-only and keeps its existing signature/behavior.
Existing `SendInterruptAsync` remains exactly terminal ETX input. Native `SendSignal(Interrupt, ...)`
is a distinct Unix operation and does not depend on the child's terminal input mode.

| Operation | Windows | Linux/macOS |
| --- | --- | --- |
| Primary termination | Existing process handle | Existing default path or anchored child identity |
| Owned-scope termination | Job, created only for PlatformScope | Initial group, anchored only for PlatformScope |
| Native signals | Unsupported | Named signals to primary or owned initial group |
| Control after primary exit | Owned job remains usable until disposal | Initial group remains targetable only while its verified identity anchor is retained |
| Processes in another Unix group/session | Not applicable to Unix grouping | Outside owned initial group; not automatically discovered or controlled |

Capabilities describe available operations for the successfully launched instance, not current liveness.
New native Unix signals require PlatformScope, including primary targeting, so they share its identity anchor.
Default Unix sessions expose no new signal capability; their existing primary Terminate behavior is preserved.
Windows exposes owned-scope termination only when job setup succeeded. Ownership/capabilities remain readable
after disposal, like process identity; requests after disposal throw `ObjectDisposedException`.

Unsupported OS operations throw `PlatformNotSupportedException`; requesting OwnedScope or a native Unix
signal without opting in throws `InvalidOperationException`. Validate these before native side effects.
`Requested` means the native request succeeded, not that every member received it or exited.
`TargetUnavailable` means there was no target at dispatch (such as an already-exited primary or ESRCH);
it is not a general all-descendants-exited assertion. Permission and other native failures throw
`IOException` with operation, target, and native error context; never relabel them as successful cleanup.

## Windows ownership

Create a non-inheritable, unnamed job configured with kill-on-last-handle-close; do not enable breakaway.
For opted-in launches, create the primary suspended, assign it to the job, then resume its initial thread.
Check every step. Assignment/resume failure terminates and collects the suspended child and releases all
handles before startup fails. No fallback may run the application outside the requested scope.

Retain the job after primary exit. Closing ConPTY and collecting primary exit must not release job ownership.
Test nested-job hosting and incompatible restrictions. Preserve the original native startup failure while
still attempting every cleanup step. Neither job membership nor this library is a security sandbox.
Processes launched through an external service are not necessarily descendants associated with this job.

## Unix ownership and identity gate

The helper already creates a session and initial process group before exec. Only that initial group is owned.
A shell can move a pipeline or background job to a different group; detached sessions are outside scope.
Do not expose arbitrary PID/PGID parameters or silently retarget to the current foreground group.

PG01 must validate the retained-child design before PG04:
- Spawn the managed helper with explicit configuration/status/diagnostic pipes and closed unrelated descriptors.
- Own all native child-wait operations for that child; do not register a competing Process waiter.
- Observe exit with `waitid` and non-reaping semantics, retaining identity until scope disposal.
- Verify that the retained leader prevents group-identifier reuse on each supported Unix architecture.
- Publish the primary exit code without waiting for descendants; normalize signaled exit consistently with
  the existing backend.
- Reap exactly once after final group control, including failed startup and cancelled ownership transfer.
- Never issue `kill(0, ...)`, `kill(-1, ...)`, or a signal to the hosting process/group.
- Detect missing wait ownership; fail closed rather than signal an identity that is no longer anchored.

The cost is one retained exited-child record per opted-in, undisposed session after primary exit.
Document this and make deterministic disposal mandatory for releasing it. Do not install or replace a
process-wide SIGCHLD handler, reaper, or signal disposition owned by the host application.
An application using a competing global child reaper cannot be promised safe scope ownership; PG01 must
define how interference is detected and whether reliable operation requires a documented host precondition.
An uncloseable identity race blocks this approach; a passing stress test alone is not proof.

General foreground-job signaling, suspend/resume, a persistent supervisor, cgroups, and descendant enumeration
are outside this design. Expanding to those mechanisms requires a separate decision.

## Shutdown, disposal, cancellation, and output

Add `PtyShutdownOptions.TerminationTarget`, default `PrimaryProcess`; snapshot and validate it.
It selects only optional forced escalation. Existing grace timing, request bytes, and primary exit semantics
remain intact. `ShutdownAsync` still returns promptly when primary exit is collected; it does not automatically
clean a surviving scope after graceful primary exit. Call RequestTermination(OwnedScope) explicitly or dispose
an opted-in session when scope cleanup is wanted.

Preserve the three-argument constructor and deconstruction of `PtyShutdownResult`; add an init-only nullable
`PtyControlResult? TerminationResult` property. It is null when no escalation dispatch occurred, including all
old constructor calls. `ForcedTerminationRequested` continues to record a dispatch attempt rather than its
effect; the new property records target and outcome. `Exited` always concerns the primary, never scope emptiness.

PlatformScope disposal requests owned-scope force termination before releasing the job/Unix anchor, then
terminates/collects the primary as needed and releases terminal resources. It also runs after natural primary
exit. Default disposal stays primary-only. No unbounded wait for arbitrary descendants is added.
Attempt all resource cleanup even if a control request fails, preserve useful failure information, and make
repeated/concurrent disposal harmless. Cached primary exit status remains available.

Startup cancellation cannot abandon a suspended child, job, helper, or anchor. Before ownership transfer,
cleanup finishes before reporting cancellation. A cancelled wait/shutdown never starts a new escalation;
an already-issued request cannot be undone. Disposal is independently authorized cleanup, even after cancellation.
Serialize native requests and anchor release; test simultaneous natural exit, shutdown, and disposal.

Control and shutdown do not consume output or close streams. Consumers continue draining output concurrently.
Primary exit, group signaling, and output EOF remain separate observations. Preserve macOS bounded read-ahead.
Abrupt kill/disposal cannot promise output the child never wrote or terminal data discarded by forced close.

## Acceptance boundaries

Require ordinary descendants remaining in the owned scope to stop following forced scope cleanup, including
after primary exit. Fixtures observe each known child's exit; native request success alone is insufficient.
Use readiness handshakes and independently owned cleanup for every fixture, including intentional escapees.

Test changed groups, detached sessions, ignored graceful signals, nested Windows jobs, immediate spawn/exit,
startup failures, cancellation, repeat/concurrent cleanup, handle/descriptor release, and final output.
A descendant outside the documented scope must never be reported as owned or successfully cleaned.
Do not promise universal host-crash cleanup on Unix, complete process-tree containment, all-member exit codes,
or portable scope-empty detection.

Preserve all existing package and interactive checks and the separate ConPTY limitation.
Require six OS/architecture jobs, all three frameworks, fresh NuGet consumers, and published-consumer checks.
Windows laptop CMD/PowerShell 5.1 acceptance is recorded separately, never inferred from a merge.

## Native references and proof obligations

These references justify candidate primitives, not the unimplemented design's correctness:
- [Windows jobs](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).
- [Linux wait and non-reaping observation](https://man7.org/linux/man-pages/man2/waitid.2.html).
- [Linux group signaling](https://man7.org/linux/man-pages/man2/kill.2.html).
- [Apple posix_spawn](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/posix_spawn.2.html).
- [Apple foreground groups](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/tcgetpgrp.3.html).

PG01 records current macOS SDK definitions and runtime evidence for wait semantics and ABI layouts.
Do not assume Linux constants/layouts are valid on Darwin or that foreground lookup from a PTY master is
portable. Foreground retargeting is deferred rather than hidden behind an assumed primary-group equivalence.

### PG01 initial findings

Linux/macOS x64/ARM64 probes retain an unregistered native child through non-reaping exit observation while ordinary
.NET children are collected. They also signal a surviving initial-group child after the leader exits.
An explicit competing reap is detectable through ECHILD, but detection is not an atomic lock against
another component reaping between a check and a signal. Exclusive wait ownership remains a host precondition.

Ignoring SIGCHLD discards the wait record. Owned launch must reject SIG_IGN and SA_NOCLDWAIT, and reject a
PID-1 host because .NET's native signal dispatcher can reap unregistered children in that role. These checks
must precede child creation. The host must not subsequently change these settings or run a competing global
reaper. No runtime-private lock or global signal-handler replacement is an acceptable implementation.
The native-spawn probe also completes the existing managed helper's configuration/status/exec protocol on
Linux/macOS x64/ARM64. See the implementation plan for current commit-specific acceptance.

Evidence sources:
- [.NET 8 child reaping](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/ProcessWaitState.Unix.cs).
- [.NET 10 child reaping](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/ProcessWaitState.Unix.cs).
- [.NET signal dispatch](https://github.com/dotnet/runtime/blob/v10.0.0/src/native/libs/System.Native/pal_signal.c).
- [Darwin wait flags](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/wait.h).
- [Darwin siginfo layout](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/signal.h).

### Implementation refinements

- Linux PlatformScope requires glibc 2.34 close-from spawn actions; Darwin uses CLOEXEC_DEFAULT. No unrelated descriptors are intentionally inherited.
- Failed startup stops the anchored primary, observes exit without reaping, then requests initial-group cleanup before final reap. This closes the race where a helper creates its group after an earlier group request.
- Darwin returns EPERM for zombie-only groups. After EPERM, a bounded libproc inventory of the anchored group can establish that no live members remain. Unknown, truncated, or permission-denied inventory preserves the control error. Inventory PIDs are never control targets.
- Native child termination denial is reported without waiting indefinitely. Terminal resources are still released; a retained observer reaps the known child on natural exit. This exceptional path cannot guarantee descendant cleanup.
- Simultaneous startup and cleanup failures are reported together in AggregateException, retaining the original cancellation/native error. Ordinary successful rollback preserves the original exception type.

Additional primary sources: [Darwin group signal semantics](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/kern/kern_sig.c),
[Darwin process inventory](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/kern/proc_info.c),
[Darwin fixed-width process layout](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/proc_info.h),
[Darwin spawn flags](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/spawn.h),
[glibc spawn definitions](https://github.com/bminor/glibc/blob/master/posix/spawn.h).
