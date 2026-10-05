namespace Icod.Pty;

/// <summary>Identifies the current session lifecycle phase.</summary>
public enum PtySessionPhase {
	/// <summary>The session is forwarding or awaiting primary exit.</summary>
	Running,
	/// <summary>Primary exit was observed and final output is being drained.</summary>
	Draining,
	/// <summary>Pumps and resources are being released.</summary>
	Releasing,
	/// <summary>All session work and release attempts have settled.</summary>
	Completed
}
/// <summary>Identifies a lifecycle event without recording terminal contents.</summary>
public enum PtySessionEventKind {
	/// <summary>Session ownership was established.</summary>
	Started,
	/// <summary>The optional input source reached EOF.</summary>
	InputEnded,
	/// <summary>Normal input was permanently sealed.</summary>
	InputSealed,
	/// <summary>An application shutdown was accepted.</summary>
	ShutdownStarted,
	/// <summary>A shutdown returned a result.</summary>
	ShutdownCompleted,
	/// <summary>A shutdown was cancelled.</summary>
	ShutdownCancelled,
	/// <summary>A shutdown threw an exception.</summary>
	ShutdownFailed,
	/// <summary>Primary exit was observed.</summary>
	PrimaryExited,
	/// <summary>PTY EOF and destination flush completed.</summary>
	OutputEnded,
	/// <summary>The requested drain interval elapsed.</summary>
	DrainTimedOut,
	/// <summary>An input operation failed.</summary>
	InputFailed,
	/// <summary>An output operation failed.</summary>
	OutputFailed,
	/// <summary>Primary observation failed.</summary>
	ProcessFailed,
	/// <summary>Resource release began.</summary>
	ReleaseStarted,
	/// <summary>A release operation failed.</summary>
	CleanupFailed,
	/// <summary>Session finalization settled.</summary>
	Completed
}
/// <summary>Records an event's sequence and monotonic elapsed time, without payload data.</summary>
public sealed record PtySessionEvent(long Sequence, TimeSpan Elapsed, PtySessionEventKind Kind);
/// <summary>A detached, read-only snapshot of bounded lifecycle history and completed I/O counters.</summary>
/// <remarks>Byte counters exclude unconfirmed partial writes. DroppedEvents counts evictions from the last 32 events.</remarks>
public sealed record PtySessionDiagnostics(PtySessionPhase Phase, bool InputSealed, long BytesWrittenToPty,
	long BytesReadFromPty, long BytesWrittenToOutput, long DroppedEvents, IReadOnlyList<PtySessionEvent> Events);
