namespace Icod.Pty.Session;

internal sealed record SessionConfiguration(Stream Output, Stream? Input, bool LeaveInputOpen, bool LeaveOutputOpen, TimeSpan DrainTimeout) {
	internal static SessionConfiguration Capture(PtySessionOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		var snapshot = new SessionConfiguration(options.Output, options.Input, options.LeaveInputOpen, options.LeaveOutputOpen, options.DrainTimeout);
		if (!snapshot.Output.CanWrite) throw new ArgumentException("The output destination must be writable.", nameof(options));
		if (snapshot.Input != null && !snapshot.Input.CanRead) throw new ArgumentException("The input source must be readable.", nameof(options));
		if (ReferenceEquals(snapshot.Input, snapshot.Output)) throw new ArgumentException("Input and output must be distinct streams.", nameof(options));
		if (snapshot.DrainTimeout <= TimeSpan.Zero || snapshot.DrainTimeout.TotalMilliseconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(options), "DrainTimeout must be positive and at most Int32.MaxValue milliseconds.");
		return snapshot;
	}
}
