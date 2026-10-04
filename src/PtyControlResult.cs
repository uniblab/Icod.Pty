namespace Icod.Pty;

/// <summary>Reports dispatch of a native control request, not completion of descendant cleanup.</summary>
public enum PtyControlStatus {
	/// <summary>The native request succeeded; this does not prove every member received it or exited.</summary>
	Requested,
	/// <summary>The target was unavailable at dispatch; this does not assert all descendants have exited.</summary>
	TargetUnavailable,
	/// <summary>The managed primary termination call completed, but its void API cannot distinguish native
	/// acceptance from a concurrent exit. Used by default Unix sessions; not proof of delivery or exit.</summary>
	DispatchUnconfirmed
}

/// <summary>Records the selected target and native dispatch outcome.</summary>
/// <param name="Target">The target selected for the request.</param>
/// <param name="Status">The dispatch outcome, independent of primary exit and output EOF.</param>
public readonly record struct PtyControlResult(PtyProcessTarget Target, PtyControlStatus Status);
