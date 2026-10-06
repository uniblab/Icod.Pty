namespace Icod.Pty;

/// <summary>Configures byte forwarding, stream ownership, and final output draining.</summary>
/// <remarks>Supplied streams must honor asynchronous cancellation. Settings are captured before startup yields.</remarks>
public sealed class PtySessionOptions {
	/// <summary>Creates options with a required writable output destination.</summary>
	public PtySessionOptions(Stream output) { ArgumentNullException.ThrowIfNull(output); Output = output; }
	/// <summary>Gets the output destination. It must not be written independently while the session uses it.</summary>
	public Stream Output { get; }
	/// <summary>Gets or sets an optional input source. EOF stops forwarding without terminating the child.</summary>
	public Stream? Input { get; set; }
	/// <summary>Gets or sets optional bounded output and resize recording.</summary>
	public PtyRecordingOptions? Recording { get; set; }
	/// <summary>Gets or sets whether finalization leaves the source open. Defaults to true.</summary>
	public bool LeaveInputOpen { get; set; } = true;
	/// <summary>Gets or sets whether finalization leaves the destination open. Defaults to true.</summary>
	public bool LeaveOutputOpen { get; set; } = true;
	/// <summary>Gets or sets the requested output-drain budget after primary exit, defaulting to five seconds.</summary>
	/// <remarks>Must be positive and at most Int32.MaxValue milliseconds. This cannot bound uncooperative streams or native cleanup.</remarks>
	public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
