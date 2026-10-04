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
/// <param name="ForcedTerminationRequested">Whether this operation dispatched forced termination to its selected target.
/// Natural exit can race with that request, so this does not identify the actual cause of exit.</param>
public readonly record struct PtyShutdownResult(PtyShutdownStatus Status, int? ExitCode, bool ForcedTerminationRequested) {
	/// <summary>Gets the escalation request outcome, or null if no escalation was dispatched.</summary>
	/// <remarks>No outcome proves that all descendants exited. Default Unix primary requests may report
	/// DispatchUnconfirmed because the managed termination API cannot confirm native dispatch.</remarks>
	public PtyControlResult? TerminationResult { get; init; }
}
