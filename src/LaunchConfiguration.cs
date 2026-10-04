using System.Collections;

namespace Icod.Pty;

internal sealed class LaunchConfiguration {
	public string FileName { get; set; } = "";
	public string[] Arguments { get; set; } = [];
	public string WorkingDirectory { get; set; } = "";
	public Dictionary<string, string> Environment { get; set; } = new();
	public string SlaveName { get; set; } = "";
	public int Columns { get; set; }
	public int Rows { get; set; }
	public TimeSpan StartTimeout { get; set; }
	public string? DotNetHostPath { get; set; }

#if !PTY_HELPER
	internal static LaunchConfiguration Capture(PtyStartInfo info) {
		ArgumentNullException.ThrowIfNull(info);
		ValidateText(info.FileName, nameof(info.FileName), true);
		_ = new PtySize(info.Size.Columns, info.Size.Rows);
		if (info.StartTimeout <= TimeSpan.Zero || info.StartTimeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(info.StartTimeout));
		if (info.DotNetHostPath != null) ValidateText(info.DotNetHostPath, nameof(info.DotNetHostPath), true);
		string directory = Path.GetFullPath(info.WorkingDirectory ?? System.Environment.CurrentDirectory);
		if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
		Dictionary<string, string> environment = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables()) environment[(string)entry.Key] = (string)entry.Value!;
		if (!OperatingSystem.IsWindows() && !environment.ContainsKey("TERM")) environment["TERM"] = "xterm-256color";
		foreach ((string key, string? value) in info.Environment) {
			ValidateText(key, "environment key", true);
			if (key.Contains('=')) throw new ArgumentException("Environment names cannot contain '='.", nameof(info));
			if (value == null) environment.Remove(key);
			else { ValidateText(value, "environment value", false); environment[key] = value; }
		}
		string[] arguments = info.ArgumentList.ToArray();
		foreach (string argument in arguments) ValidateText(argument, "argument", false);
		return new LaunchConfiguration { FileName = ResolveExecutable(info.FileName, directory, environment), Arguments = arguments, WorkingDirectory = directory, Environment = environment, Columns = info.Size.Columns, Rows = info.Size.Rows, StartTimeout = info.StartTimeout, DotNetHostPath = info.DotNetHostPath };
	}
	internal static string ResolveExecutable(string name, string directory, IReadOnlyDictionary<string, string> environment) {
		IEnumerable<string> candidates;
		bool explicitPath = Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar);
		if (explicitPath) candidates = [Path.GetFullPath(name, directory)];
		else {
			IEnumerable<string> paths = environment.TryGetValue("PATH", out string? path) ? path.Split(Path.PathSeparator).Select(p => Path.GetFullPath(p.Length == 0 ? directory : p, directory)) : [];
			if (OperatingSystem.IsWindows()) paths = new[] { directory, System.Environment.SystemDirectory }.Concat(paths);
			candidates = paths.Select(p => Path.Combine(p, name));
		}
		foreach (string candidate in candidates) {
			if (File.Exists(candidate) && (explicitPath || OperatingSystem.IsWindows() || Unix.UnixNative.CanExecute(candidate))) return candidate;
			if (OperatingSystem.IsWindows() && Path.GetExtension(candidate).Length == 0 && File.Exists(candidate + ".exe")) return candidate + ".exe";
		}
		throw new FileNotFoundException($"Executable '{name}' was not found.", name);
	}
	private static void ValidateText(string? value, string name, bool nonempty) {
		if (value == null || value.IndexOf('\0') >= 0 || (nonempty && string.IsNullOrWhiteSpace(value))) throw new ArgumentException($"Invalid {name}.", name);
	}
#endif
}
