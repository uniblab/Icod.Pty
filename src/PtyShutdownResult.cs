namespace Icod.Pty;

/// <summary>Describes whether shutdown collected the primary child's exit status.</summary>
public enum PtyShutdownStatus {
	/// <summary>The primary child's exit status was collected.</summary>
	Exited,
	/// <summary>The operation's deadline expired before exit status was collected.</summary>
	TimedOut
}

/// <summary>Reports the outcome of a shutdown operation without closing the session streams.</summary>
/// <param name="Status">Whether exit status was collected before the deadline.</param>
/// <param name="ExitCode">The collected exit code, or null when the operation timed out.</param>
/// <param name="ForcedTerminationRequested">Whether this operation invoked primary-child termination.
/// Natural exit can race with that request, so this does not identify the actual cause of exit.</param>
public readonly record struct PtyShutdownResult(PtyShutdownStatus Status, int? ExitCode, bool ForcedTerminationRequested);
