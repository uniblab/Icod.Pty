namespace Icod.Pty;

/// <summary>Describes how a live byte expectation completed.</summary>
public enum PtyExpectStatus {
	/// <summary>The first occurrence was consumed.</summary>
	Matched,
	/// <summary>The expectation deadline elapsed.</summary>
	TimedOut,
	/// <summary>Output reached end of stream without a match.</summary>
	OutputEnded,
	/// <summary>Output forwarding stopped without a match.</summary>
	OutputStopped,
	/// <summary>Final output draining timed out without a match.</summary>
	OutputTimedOut,
	/// <summary>Output forwarding failed without a match.</summary>
	OutputFaulted,
	/// <summary>Unconsumed output exceeded the configured bound.</summary>
	BufferLimitExceeded
}

/// <summary>Reports a byte expectation outcome without exposing terminal content.</summary>
/// <param name="Status">The completion status.</param>
/// <param name="ConsumedBytes">The number of output bytes consumed through a successful match.</param>
public sealed record PtyExpectResult(PtyExpectStatus Status, long ConsumedBytes);
