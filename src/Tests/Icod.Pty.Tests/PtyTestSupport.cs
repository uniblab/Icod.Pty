using System.Text;

namespace Icod.Pty.Tests;

internal static class PtyTestSupport {
	internal static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
	internal static PtyStartInfo Child(params string[] arguments) {
		PtyStartInfo info = new(DotNet) { DotNetHostPath = DotNet, Size = new PtySize(1024, 30) };
		info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll"));
		foreach (string argument in arguments) info.ArgumentList.Add(argument);
		return info;
	}
	internal static byte[] Line(string line) => Encoding.UTF8.GetBytes(line + (OperatingSystem.IsWindows() ? "\r" : "\n"));
	internal static async Task<string> ReadUntil(Stream output, string marker) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
		using MemoryStream bytes = new(); byte[] one = new byte[1];
		while (bytes.Length < 200000) {
			try {
				if (await output.ReadAsync(one, timeout.Token) == 0) throw new IOException("Unexpected EOF: " + Encoding.UTF8.GetString(bytes.ToArray()));
			} catch (OperationCanceledException error) when (timeout.IsCancellationRequested) {
				throw new TimeoutException("Waiting for " + marker + "; received: " + Encoding.UTF8.GetString(bytes.ToArray()), error);
			}
			bytes.WriteByte(one[0]);
			string text = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
			if (text.Contains(marker, StringComparison.Ordinal)) return text;
		}
		throw new IOException("Output limit exceeded.");
	}
	internal static async Task<string> Drain(Stream output) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
		using StreamReader reader = new(output, Encoding.UTF8, false, 4096, true);
		return await reader.ReadToEndAsync(timeout.Token);
	}
}
