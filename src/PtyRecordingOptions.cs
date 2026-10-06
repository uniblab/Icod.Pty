namespace Icod.Pty;

/// <summary>Configures an opt-in bounded session recording.</summary>
/// <remarks>The destination can contain sensitive terminal output. Settings are captured before startup yields.</remarks>
public sealed class PtyRecordingOptions {
	/// <summary>The smallest recording: one file header and one terminal frame.</summary>
	public const long MinimumBytes = 32;
	/// <summary>Creates recording options with a required writable destination.</summary>
	public PtyRecordingOptions(Stream destination) { ArgumentNullException.ThrowIfNull(destination); Destination = destination; }
	/// <summary>Gets or sets the recording destination.</summary>
	public Stream Destination { get; set; }
	/// <summary>Gets or sets whether session finalization leaves the destination open. Defaults to true.</summary>
	public bool LeaveOpen { get; set; } = true;
	/// <summary>Gets or sets the maximum file size, including headers, defaulting to 16 MiB.</summary>
	public long MaxBytes { get; set; } = 16L * 1024 * 1024;
}
