using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Icod.Pty.Sample;

internal static class TerminalConfigurationSmokeChecks {
	internal static async Task<int> RunAsync() {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
		string executable = Environment.ProcessPath ?? throw new IOException("Cannot identify the verification executable.");
		PtyStartInfo Child() {
			PtyStartInfo start = new(executable) { Size = new PtySize(100, 30) };
			if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
				start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new IOException("Cannot identify the verification assembly."));
			start.ArgumentList.Add("--terminal-config-child"); return start;
		}
		if (OperatingSystem.IsWindows()) {
			Require(PtyProcess.GetTerminalCapabilities() == PtyTerminalCapabilities.None, "Windows unexpectedly reported terminal configuration capabilities.");
			PtyStartInfo rejected = Child(); rejected.TerminalOptions = new() { Echo = false };
			try { _ = PtyProcess.StartAsync(rejected, deadline.Token); throw new IOException("Windows accepted an explicit terminal request."); }
			catch (PlatformNotSupportedException) { }
			using MemoryStream output = new();
			await using PtySession baseline = await PtySession.StartAsync(Child(), new(output), deadline.Token);
			PtySessionResult result = await baseline.Completion.WaitAsync(deadline.Token);
			Require(result.ExitCode == 0 && Encoding.UTF8.GetString(output.ToArray()).Contains("DEFAULT-OK", StringComparison.Ordinal), "Windows default session launch failed.");
		} else {
			PtyStartInfo configured = Child();
			configured.TerminalOptions = new() { Echo = false, CanonicalInput = false, MinimumReadBytes = 1, ReadTimeoutDeciseconds = 0 };
			await using PtyProcess process = await PtyProcess.StartAsync(configured, deadline.Token);
			string ready = await ReadUntilAsync(process.Output, "CANONICAL:0", deadline.Token);
			Require(ready.Contains("ECHO:0", StringComparison.Ordinal) && ready.Contains("CANONICAL:0", StringComparison.Ordinal), "Child did not observe requested initial modes.");
			await process.Input.WriteAsync(new byte[] { 0x5a }, deadline.Token);
			string acknowledgement = await ReadUntilAsync(process.Output, "ACK:5A", deadline.Token);
			Require(!acknowledgement.Contains("Z", StringComparison.Ordinal), "Input was echoed despite Echo=false.");
			Require(await process.WaitForExitAsync(deadline.Token) == 0, "Configured child failed.");
		}
		Console.WriteLine("PTY terminal configuration smoke check passed."); return 0;
	}

	internal static unsafe int RunChild() {
		if (OperatingSystem.IsWindows()) { Console.WriteLine("DEFAULT-OK"); return 0; }
		byte[] state = new byte[OperatingSystem.IsMacOS() ? 72 : 60];
		if (tcgetattr(0, state) != 0) return 2;
		int flagSize = OperatingSystem.IsMacOS() ? 8 : 4;
		ulong local = flagSize == 8 ? BitConverter.ToUInt64(state, flagSize * 3) : BitConverter.ToUInt32(state, flagSize * 3);
		ulong canonical = OperatingSystem.IsMacOS() ? 0x100u : 0x02u;
		Write($"CONFIG-READY ECHO:{((local & 0x08) == 0 ? 0 : 1)} CANONICAL:{((local & canonical) == 0 ? 0 : 1)}\n");
		byte value = 0; nint count = read(0, &value, 1);
		if (count != 1) return 3;
		Write($"ACK:{value:X2}\n"); return 0;
	}

	private static async Task<string> ReadUntilAsync(Stream stream, string marker, CancellationToken token) {
		using MemoryStream bytes = new(); byte[] one = new byte[1];
		while (bytes.Length < 100000) {
			int count = await stream.ReadAsync(one, token); if (count == 0) break;
			bytes.WriteByte(one[0]); string value = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
			if (value.Contains(marker, StringComparison.Ordinal)) return value;
		}
		throw new IOException("Terminal configuration child did not emit " + marker + ".");
	}
	private static unsafe void Write(string value) {
		byte[] bytes = Encoding.UTF8.GetBytes(value); fixed (byte* pointer = bytes) { if (write(1, pointer, (nuint)bytes.Length) != bytes.Length) throw new IOException("Native smoke output failed."); }
	}
	private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
	[DllImport("libc", SetLastError = true)] private static extern int tcgetattr(int descriptor, [Out] byte[] state);
	[DllImport("libc", SetLastError = true)] private static extern unsafe nint read(int descriptor, byte* buffer, nuint count);
	[DllImport("libc", SetLastError = true)] private static extern unsafe nint write(int descriptor, byte* buffer, nuint count);
}
