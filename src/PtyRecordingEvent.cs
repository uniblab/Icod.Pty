namespace Icod.Pty;

/// <summary>Identifies a recorded session event.</summary>
public enum PtyRecordingEventKind {
	/// <summary>Bytes accepted by the session output destination.</summary>
	Output,
	/// <summary>A successfully applied terminal size.</summary>
	Resize
}

/// <summary>One immutable event read from a recording.</summary>
public sealed class PtyRecordingEvent {
	internal PtyRecordingEvent(PtyRecordingEventKind kind, TimeSpan elapsed, byte[] output, PtySize? size) {
		Kind = kind; Elapsed = elapsed; Output = output; Size = size;
	}
	/// <summary>Gets the event kind.</summary>
	public PtyRecordingEventKind Kind { get; }
	/// <summary>Gets the nondecreasing elapsed time captured by the writer.</summary>
	public TimeSpan Elapsed { get; }
	/// <summary>Gets owned output bytes, or an empty value for a resize.</summary>
	public ReadOnlyMemory<byte> Output { get; }
	/// <summary>Gets the applied size for a resize, or null for output.</summary>
	public PtySize? Size { get; }
}

/// <summary>Controls validation and source ownership for a recording reader.</summary>
public sealed class PtyRecordingReaderOptions {
	/// <summary>Gets or sets whether disposal leaves the source open. Defaults to true.</summary>
	public bool LeaveOpen { get; set; } = true;
	/// <summary>Gets or sets the maximum bytes the reader will consume. Defaults to 64 MiB.</summary>
	public long MaxBytes { get; set; } = 64L * 1024 * 1024;
	/// <summary>Gets or sets the maximum output payload in one frame. Defaults to 16 KiB.</summary>
	public int MaxFrameBytes { get; set; } = 16 * 1024;
}

/// <summary>Summarizes deterministic output replay.</summary>
public sealed record PtyRecordingReplayResult(PtyRecordingStatus Status, long OutputBytes, long EventCount);

/// <summary>Reports invalid, unsupported, incomplete, or oversized recording structure.</summary>
public sealed class PtyRecordingFormatException : FormatException {
	/// <summary>Creates a format exception without terminal payload data.</summary>
	public PtyRecordingFormatException(string message) : base(message) { }
}
