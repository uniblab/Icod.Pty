namespace Icod.Pty;

/// <summary>Describes the overall script outcome.</summary>
public enum PtyScriptStatus {
	/// <summary>Every step completed.</summary>
	Completed,
	/// <summary>An expectation returned a nonmatching status.</summary>
	ExpectationFailed
}

/// <summary>Reports ordered script progress without exposing script bytes.</summary>
/// <param name="Status">The overall outcome.</param>
/// <param name="CompletedStepCount">The number of completed steps.</param>
/// <param name="FailedStepIndex">The zero-based failed step, when any.</param>
/// <param name="ExpectStatus">The failed expectation status, when any.</param>
public sealed record PtyScriptResult(PtyScriptStatus Status, int CompletedStepCount, int? FailedStepIndex, PtyExpectStatus? ExpectStatus);
