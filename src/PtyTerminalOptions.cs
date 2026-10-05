namespace Icod.Pty;

/// <summary>Optional terminal settings captured before a child is created.</summary>
/// <remarks>Null members preserve the newly allocated terminal's baseline. These settings establish an initial state; the child can change it later.</remarks>
public sealed class PtyTerminalOptions {
	/// <summary>Requests the native disabled value for a control character.</summary>
	public const int DisabledCharacter = -1;
	/// <summary>Gets or sets the terminal profile. The default is Preserve.</summary>
	public PtyTerminalProfile Profile { get; set; }
	/// <summary>Gets or sets whether terminal input is echoed.</summary>
	public bool? Echo { get; set; }
	/// <summary>Gets or sets whether input is delivered in canonical lines.</summary>
	public bool? CanonicalInput { get; set; }
	/// <summary>Gets or sets whether configured control characters generate terminal signals.</summary>
	public bool? SignalProcessing { get; set; }
	/// <summary>Gets or sets the interrupt byte, from 0 through 255, or DisabledCharacter.</summary>
	public int? InterruptCharacter { get; set; }
	/// <summary>Gets or sets the canonical end-of-file byte, from 0 through 255, or DisabledCharacter.</summary>
	public int? EndOfFileCharacter { get; set; }
	/// <summary>Gets or sets the canonical erase byte, from 0 through 255, or DisabledCharacter.</summary>
	public int? EraseCharacter { get; set; }
	/// <summary>Gets or sets VMIN, from 0 through 255. Requires CanonicalInput=false.</summary>
	public int? MinimumReadBytes { get; set; }
	/// <summary>Gets or sets VTIME in deciseconds, from 0 through 255. Requires CanonicalInput=false.</summary>
	public int? ReadTimeoutDeciseconds { get; set; }
}
