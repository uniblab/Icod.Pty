namespace Icod.Pty;

/// <summary>Selects a launch-time terminal profile.</summary>
public enum PtyTerminalProfile {
	/// <summary>Preserves the newly allocated terminal's baseline, except for explicit overrides.</summary>
	Preserve = 0,
	/// <summary>Applies the platform's native raw transformation with one-byte blocking reads.</summary>
	Raw = 1
}
