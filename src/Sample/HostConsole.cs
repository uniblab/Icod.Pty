using Icod.Pty;

namespace Icod.Pty.Sample;

internal abstract class HostConsole : IDisposable {
	internal static HostConsole Open() {
		if (Console.IsInputRedirected || Console.IsOutputRedirected) throw new InvalidOperationException("Interactive mode requires a terminal for input and output. Use --line for redirected input/output.");
		return OperatingSystem.IsWindows() ? new WindowsHostConsole() : new UnixHostConsole();
	}
	internal abstract Stream Output { get; }
	internal abstract PtySize? GetSize();
	internal abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
	internal static async Task ReportErrorAsync(string message) {
		if (Console.IsErrorRedirected) { Console.Error.WriteLine(message); return; }
		try {
			using Stream error = OperatingSystem.IsWindows() ? new WindowsConsoleOutput() : new UnixConsoleOutput(2);
			using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(250));
			await error.WriteAsync(Console.OutputEncoding.GetBytes(message + Environment.NewLine), timeout.Token);
		} catch (Exception) { /* A blocked/closed host cannot accept a diagnostic; retain the failure exit code. */ }
	}
	public abstract void Dispose();
}
