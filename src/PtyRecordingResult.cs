namespace Icod.Pty;

/// <summary>Describes how an optional recording ended.</summary>
public enum PtyRecordingStatus {
	/// <summary>Recording was not requested.</summary>
	Disabled,
	/// <summary>Output reached EOF and the recording closed normally.</summary>
	Complete,
	/// <summary>The configured file limit was reached and a valid prefix was retained.</summary>
	Truncated,
	/// <summary>Session finalization stopped recording before output EOF.</summary>
	Stopped,
	/// <summary>The recording destination failed.</summary>
	Faulted
}

/// <summary>Reports an independent recording outcome without exposing terminal content.</summary>
public sealed record PtyRecordingResult(PtyRecordingStatus Status, long BytesWritten, long EventCount, Exception? Exception) {
	/// <summary>Gets the shared result used when recording is disabled.</summary>
	public static PtyRecordingResult Disabled { get; } = new(PtyRecordingStatus.Disabled, 0, 0, null);
}
