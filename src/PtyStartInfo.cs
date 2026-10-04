namespace Icod.Pty;

/// <summary>Settings copied by Start or StartAsync before child creation.</summary>
/// <remarks>Do not mutate these settings concurrently with either startup call.</remarks>
public sealed class PtyStartInfo {
	/// <summary>Creates settings for an executable path or a name found through PATH.</summary>
	public PtyStartInfo(string fileName) { ArgumentNullException.ThrowIfNull(fileName); FileName = fileName; }
	/// <summary>Gets the executable. Scripts require an explicit interpreter.</summary>
	public string FileName { get; }
	/// <summary>Gets the arguments, without shell quoting or expansion.</summary>
	public IList<string> ArgumentList { get; } = new List<string>();
	/// <summary>Gets environment overrides. A null value removes a variable.</summary>
	public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
	/// <summary>Gets or sets the child directory; null uses the current directory.</summary>
	public string? WorkingDirectory { get; set; }
	/// <summary>Gets or sets the initial size, defaulting to 80 columns and 24 rows.</summary>
	public PtySize Size { get; set; } = new(80, 24);
	/// <summary>Gets or sets the Unix helper startup timeout, defaulting to 15 seconds.</summary>
	/// <remarks>Bounds the helper handshake only, not native process creation or cleanup. It is not used on Windows.</remarks>
	public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(15);
	/// <summary>Gets or sets an explicit dotnet executable for the Unix managed helper.</summary>
	public string? DotNetHostPath { get; set; }
}
