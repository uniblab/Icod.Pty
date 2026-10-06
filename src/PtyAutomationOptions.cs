namespace Icod.Pty;

/// <summary>Configures opt-in bounded live output matching.</summary>
public sealed class PtyAutomationOptions {
	/// <summary>Gets the smallest supported output buffer.</summary>
	public const int MinimumBufferedOutputBytes = 1;
	/// <summary>Gets the largest supported output buffer.</summary>
	public const int MaximumBufferedOutputBytes = 1024 * 1024;
	/// <summary>Gets or sets the maximum unconsumed output retained for matching, defaulting to 64 KiB.</summary>
	public int MaxBufferedOutputBytes { get; set; } = 64 * 1024;
}
