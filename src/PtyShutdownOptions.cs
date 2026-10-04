namespace Icod.Pty;

/// <summary>Controls an application-specific exit request and optional forced termination.</summary>
/// <remarks>Do not mutate these settings while ShutdownAsync captures them. Request bytes are copied.
/// Both durations must be positive and no greater than Int32.MaxValue milliseconds.</remarks>
public sealed class PtyShutdownOptions {
	/// <summary>Gets or sets already-encoded input requesting exit. Empty means wait without sending input.</summary>
	public ReadOnlyMemory<byte> Request { get; set; } = ReadOnlyMemory<byte>.Empty;
	/// <summary>Gets or sets the budget for writing the request and waiting for exit, defaulting to five seconds.</summary>
	public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(5);
	/// <summary>Gets or sets whether to forcibly terminate the primary child after the grace period. Defaults to false.</summary>
	public bool ForceTermination { get; set; }
	/// <summary>Gets or sets the exit-collection budget after requesting forced termination, defaulting to five seconds.</summary>
	public TimeSpan TerminationTimeout { get; set; } = TimeSpan.FromSeconds(5);
	internal (byte[] Request, TimeSpan GracePeriod, bool ForceTermination, TimeSpan TerminationTimeout) Capture() {
		ReadOnlyMemory<byte> request = Request;
		TimeSpan grace = GracePeriod, termination = TerminationTimeout;
		bool force = ForceTermination;
		Validate(grace, nameof(GracePeriod)); Validate(termination, nameof(TerminationTimeout));
		return (request.ToArray(), grace, force, termination);
	}
	private static void Validate(TimeSpan value, string name) {
		if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(name, "A finite positive duration no greater than Int32.MaxValue milliseconds is required.");
	}
}
