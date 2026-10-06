namespace Icod.Pty;

/// <summary>Identifies optional launch-time terminal controls implemented by the current backend.</summary>
[Flags]
public enum PtyTerminalCapabilities {
	/// <summary>No optional terminal configuration controls are available.</summary>
	None = 0,
	/// <summary>The native Raw profile is available.</summary>
	RawProfile = 1,
	/// <summary>Input echo can be configured.</summary>
	Echo = 2,
	/// <summary>Canonical or noncanonical input can be selected.</summary>
	CanonicalInput = 4,
	/// <summary>Terminal-generated signal processing can be configured.</summary>
	SignalProcessing = 8,
	/// <summary>Interrupt, end-of-file, and erase control bytes can be configured.</summary>
	ControlCharacters = 16,
	/// <summary>Noncanonical minimum-byte and timeout values can be configured.</summary>
	ReadTiming = 32
}
