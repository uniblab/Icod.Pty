namespace Icod.Pty;

/// <summary>Selects an explicitly owned target for a native process-control request.</summary>
public enum PtyProcessTarget {
	/// <summary>Target the primary child only.</summary>
	PrimaryProcess,
	/// <summary>Target the platform scope established by an opted-in launch.</summary>
	OwnedScope
}
