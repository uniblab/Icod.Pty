using System.ComponentModel;

namespace Icod.Pty;

internal static class PrimaryProcessControl {
	private static PtyControlResult Result(PtyControlStatus status) => new(PtyProcessTarget.PrimaryProcess, status);
	internal static PtyControlResult RequestNative(Func<bool> exited, Func<bool> terminate, Func<int> nativeError) {
		if (exited()) return Result(PtyControlStatus.TargetUnavailable);
		if (terminate()) return Result(PtyControlStatus.Requested);
		int code = nativeError(); // Capture before another native liveness check replaces it.
		if (exited()) return Result(PtyControlStatus.TargetUnavailable);
		throw new IOException($"TerminateProcess target PrimaryProcess failed (Win32 error {code}).", new Win32Exception(code));
	}
	internal static PtyControlResult RequestManaged(Func<bool> exited, Action terminate) {
		if (exited()) return Result(PtyControlStatus.TargetUnavailable);
		try {
			terminate();
			// Process.Kill may silently do nothing after a concurrent exit, or swallow ESRCH.
			// Its void return cannot prove a native request was accepted.
			return Result(PtyControlStatus.DispatchUnconfirmed);
		} catch (InvalidOperationException) when (exited()) { return Result(PtyControlStatus.TargetUnavailable); }
		catch (Win32Exception error) {
			throw new IOException($"Process.Kill target PrimaryProcess failed (native error {error.NativeErrorCode}).", error);
		}
	}
}
