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
	public abstract void Dispose();
}
