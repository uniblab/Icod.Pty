namespace Icod.Pty.Session;

internal sealed record RecordingConfiguration(Stream Destination, bool LeaveOpen, long MaxBytes);

internal sealed record SessionConfiguration(Stream Output, Stream? Input, bool LeaveInputOpen, bool LeaveOutputOpen, TimeSpan DrainTimeout,
	RecordingConfiguration? Recording) {
	internal static SessionConfiguration Capture(PtySessionOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PtyRecordingOptions? recording = options.Recording;
		RecordingConfiguration? recordingSnapshot = recording == null ? null : new(recording.Destination, recording.LeaveOpen, recording.MaxBytes);
		var snapshot = new SessionConfiguration(options.Output, options.Input, options.LeaveInputOpen, options.LeaveOutputOpen, options.DrainTimeout, recordingSnapshot);
		if (!snapshot.Output.CanWrite) throw new ArgumentException("The output destination must be writable.", nameof(options));
		if (snapshot.Input != null && !snapshot.Input.CanRead) throw new ArgumentException("The input source must be readable.", nameof(options));
		if (ReferenceEquals(snapshot.Input, snapshot.Output)) throw new ArgumentException("Input and output must be distinct streams.", nameof(options));
		if (snapshot.Recording != null) {
			if (snapshot.Recording.Destination == null) throw new ArgumentException("The recording destination is required.", nameof(options));
			if (!snapshot.Recording.Destination.CanWrite) throw new ArgumentException("The recording destination must be writable.", nameof(options));
			if (ReferenceEquals(snapshot.Recording.Destination, snapshot.Output) || ReferenceEquals(snapshot.Recording.Destination, snapshot.Input))
				throw new ArgumentException("The recording destination must be distinct from input and output.", nameof(options));
			if (snapshot.Recording.MaxBytes < PtyRecordingOptions.MinimumBytes)
				throw new ArgumentOutOfRangeException(nameof(options), $"Recording MaxBytes must be at least {PtyRecordingOptions.MinimumBytes}.");
		}
		if (snapshot.DrainTimeout <= TimeSpan.Zero || snapshot.DrainTimeout.TotalMilliseconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(options), "DrainTimeout must be positive and at most Int32.MaxValue milliseconds.");
		return snapshot;
	}
}
