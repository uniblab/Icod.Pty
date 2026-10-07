namespace Icod.Pty;

/// <summary>Controls timed dispatch of events from a recording reader.</summary>
public sealed class PtyRecordingTimedPlaybackOptions {
	/// <summary>Gets or sets the largest accepted event timestamp. Defaults to one hour.</summary>
	public TimeSpan MaxEventElapsed { get; set; } = TimeSpan.FromHours(1);
}
