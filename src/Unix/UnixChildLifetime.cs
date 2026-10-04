using System.Runtime.InteropServices;

namespace Icod.Pty.Unix;

// A waitable native child reserves its PID (and original PGID) until final reaping.
// The embedding host must not reap this child or change SIGCHLD to auto-reap it.
internal sealed class UnixChildLifetime : IDisposable {
	private readonly object gate = new();
	private readonly Func<int, int, int> send;
	private readonly TaskCompletionSource<int> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private bool released, lost, disposing;
	internal int ProcessId { get; }
	internal Task<int> Exit => exit.Task;
	internal UnixChildLifetime(int pid, Func<int, int, int>? send = null) {
		if (pid <= 1) throw new IOException("Native child identity is not safe to target.");
		ProcessId = pid; this.send = send ?? kill; _ = ObserveAsync();
	}
	internal static void ValidateHost() {
		if (Environment.ProcessId == 1) throw new InvalidOperationException("PlatformScope requires exclusive child-wait ownership and cannot run in PID 1.");
		byte[] action = new byte[256];
		UnixNative.Check(sigaction(OperatingSystem.IsMacOS() ? 20 : 17, 0, action), "sigaction SIGCHLD");
		int flags = BitConverter.ToInt32(action, OperatingSystem.IsMacOS() ? 12 : 136);
		if (BitConverter.ToInt64(action, 0) == 1 || (flags & (OperatingSystem.IsMacOS() ? 0x20 : 2)) != 0)
			throw new InvalidOperationException("PlatformScope requires a waitable child: SIGCHLD must not be ignored or use SA_NOCLDWAIT.");
	}
	private void ObserveLocked() {
		if (lost) throw new IOException("PTY child wait ownership was lost; refusing to target a cached process identifier.");
		byte[] info = new byte[128];
		int result;
		do { result = waitid(1, ProcessId, info, OperatingSystem.IsMacOS() ? 0x25 : 0x01000005); } while (result < 0 && Marshal.GetLastPInvokeError() == 4);
		if (result < 0) {
			int error = Marshal.GetLastPInvokeError(); if (error == 10) lost = true;
			throw UnixNative.Error("waitid retained PTY child", error);
		}
		if (BitConverter.ToInt32(info, OperatingSystem.IsMacOS() ? 12 : 16) != ProcessId) return;
		int status = BitConverter.ToInt32(info, OperatingSystem.IsMacOS() ? 20 : 24);
		exit.TrySetResult(BitConverter.ToInt32(info, 8) == 1 ? status : 128 + status);
	}
	private async Task ObserveAsync() {
		try {
			while (true) {
				lock (gate) { if (released) return; ObserveLocked(); if (Exit.IsCompleted) return; }
				await Task.Delay(10).ConfigureAwait(false);
			}
		} catch (Exception error) { exit.TrySetException(error); }
	}
	internal PtyControlResult RequestTermination(PtyProcessTarget target) => SendSignal(PtySignal.Kill, target);
	internal PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target) {
		lock (gate) { ObjectDisposedException.ThrowIf(disposing || released, this); return SendLocked(signal, target); }
	}
	private PtyControlResult SendLocked(PtySignal signal, PtyProcessTarget target) {
		if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
		int value = signal switch { PtySignal.Hangup => 1, PtySignal.Interrupt => 2, PtySignal.Terminate => 15, PtySignal.Kill => 9, _ => throw new ArgumentOutOfRangeException(nameof(signal)) };
		ObserveLocked(); // Also check an already reported exit: an external reaper may have stolen it.
		if (target == PtyProcessTarget.PrimaryProcess && Exit.IsCompletedSuccessfully) return new(target, PtyControlStatus.TargetUnavailable);
		int nativeTarget = target == PtyProcessTarget.OwnedScope ? -ProcessId : ProcessId;
		if (send(nativeTarget, value) == 0) return new(target, PtyControlStatus.Requested);
		int error = Marshal.GetLastPInvokeError();
		if (error == 3) return new(target, PtyControlStatus.TargetUnavailable);
		// XNU excludes zombies from group delivery and returns EPERM for a group
		// containing only zombies. Do not confuse that with a live permission failure.
		if (error == 1 && target == PtyProcessTarget.OwnedScope && OperatingSystem.IsMacOS() && DarwinProcessGroup.IsWithoutLiveMembers(ProcessId))
			return new(target, PtyControlStatus.TargetUnavailable);
		throw UnixNative.Error($"kill signal {value}, target {target} ({nativeTarget})", error);
	}
	public void Dispose() {
		lock (gate) {
			if (released || disposing) return;
			disposing = true;
			try {
				// Stop the helper first even when cancellation precedes setsid/handshake.
				// Once it exits it cannot create a group after our final group request.
				SendLocked(PtySignal.Kill, PtyProcessTarget.PrimaryProcess);
			} catch {
				if (lost) released = true;
				else _ = ReapAfterFailedTerminationAsync();
				throw;
			}
		}
		try {
			Exit.GetAwaiter().GetResult();
			lock (gate) {
				try { SendLocked(PtySignal.Kill, PtyProcessTarget.OwnedScope); }
				finally {
					if (!lost) {
						int result; do { result = waitpid(ProcessId, out _, 0); } while (result < 0 && Marshal.GetLastPInvokeError() == 4);
						if (result < 0) throw UnixNative.Error("waitpid final PTY reap");
					}
				}
			}
		} finally { lock (gate) released = true; }
	}
	private async Task ReapAfterFailedTerminationAsync() {
		// A credential-changing child may reject termination. Do not hang disposal;
		// retain its identity until natural exit, then collect that exact child.
		try {
			await Exit.ConfigureAwait(false);
			lock (gate) {
				ObserveLocked();
				int result; do { result = waitpid(ProcessId, out _, 0); } while (result < 0 && Marshal.GetLastPInvokeError() == 4);
				if (result < 0) throw UnixNative.Error("waitpid deferred PTY reap");
			}
		} catch (Exception error) { System.Diagnostics.Trace.TraceError("Deferred PTY cleanup: {0}", error); }
		finally { lock (gate) released = true; }
	}
	[DllImport("libc", SetLastError = true)] private static extern int sigaction(int signal, nint action, [Out] byte[] previous);
	[DllImport("libc", SetLastError = true)] private static extern int waitid(int type, int pid, [Out] byte[] info, int flags);
	[DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int flags);
	[DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
}
