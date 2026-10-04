namespace Icod.Pty;

/// <summary>Operations supported by a successfully launched instance, not a report of process liveness.</summary>
[Flags]
public enum PtyProcessCapabilities {
	/// <summary>No optional process-scope controls are available.</summary>
	None = 0,
	/// <summary>The owned platform scope can be targeted for forced termination.</summary>
	TerminateOwnedScope = 1,
	/// <summary>The primary child supports named Unix signals through an anchored identity.</summary>
	SignalPrimaryProcess = 2,
	/// <summary>The owned initial Unix process group supports named signals.</summary>
	SignalOwnedScope = 4
}
