namespace Icod.Pty;

/// <summary>Identifies the first observed trigger for session finalization.</summary>
public enum PtySessionEndReason {
	/// <summary>The primary child's exit was observed.</summary>
	PrimaryExited,
	/// <summary>The owner explicitly requested disposal.</summary>
	Disposed,
	/// <summary>Input forwarding or writing failed.</summary>
	InputFailed,
	/// <summary>Output reading, forwarding, or flushing failed.</summary>
	OutputFailed,
	/// <summary>Primary process observation failed.</summary>
	ProcessFailed
}
/// <summary>Reports output completion separately from process exit.</summary>
public enum PtySessionOutputStatus {
	/// <summary>PTY EOF was read and the destination flushed successfully.</summary>
	EndOfStream,
	/// <summary>The requested drain interval expired; output may be incomplete.</summary>
	TimedOut,
	/// <summary>Finalization stopped forwarding before confirmed EOF.</summary>
	Stopped,
	/// <summary>Reading, forwarding, or flushing failed.</summary>
	Faulted
}
/// <summary>Identifies where an operational failure was observed.</summary>
public enum PtySessionFailureStage {
	/// <summary>Source reads or PTY input writes.</summary>
	Input,
	/// <summary>PTY reads, destination writes, or flush.</summary>
	Output,
	/// <summary>Primary process observation.</summary>
	Process,
	/// <summary>Resource release or process cleanup.</summary>
	Cleanup
}
/// <summary>Preserves an original operational exception and its stage.</summary>
public sealed record PtySessionFailure(PtySessionFailureStage Stage, Exception Exception);
/// <summary>Describes final observations after owned work and resource cleanup settle.</summary>
/// <remarks>Failures is a detached read-only collection. Neither primary exit nor scope dispatch proves all descendants exited.</remarks>
public sealed record PtySessionResult(PtySessionEndReason Reason, int? ExitCode, PtySessionOutputStatus OutputStatus,
	PtyShutdownResult? LastShutdownResult, IReadOnlyList<PtySessionFailure> Failures);
