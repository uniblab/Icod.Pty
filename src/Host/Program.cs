using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Icod.Pty.Unix;

namespace Icod.Pty.Host;

internal static class Program {
	private static unsafe int Main() {
		int status = -1;
		int slave = -1;
		try {
			LaunchConfiguration config = JsonSerializer.Deserialize<LaunchConfiguration>(Console.In.ReadToEnd()) ?? throw new IOException("Missing launch configuration.");
			status = UnixNative.dup(1); UnixNative.Check(status, "dup status");
			UnixNative.Check(UnixNative.fcntl(status, 2, 1), "FD_CLOEXEC status");
			// Managed startup occurs in this fresh process, never in a forked CLR.
			System.Environment.CurrentDirectory = config.WorkingDirectory;
			using NativeStrings strings = new(config);
			UnixNative.Check(UnixNative.setsid(), "setsid");
			slave = UnixNative.open(config.SlaveName, 2); UnixNative.Check(slave, "open slave");
			UnixNative.Check(UnixNative.ioctl_value(slave, UnixNative.SetControllingTerminal, 0), "TIOCSCTTY");
			for (int fd = 0; fd <= 2; fd++) UnixNative.Check(UnixNative.dup2(slave, fd), "dup2 slave");
			if (slave > 2) { UnixNative.close(slave); slave = -1; }
			foreach (int signal in new[] { 1, 2, 3, 13, 15 }) {
				if (UnixNative.signal(signal, 0) == -1) throw UnixNative.Error("reset signal");
			}
			nint mask = Marshal.AllocHGlobal(128);
			try {
				UnixNative.Check(UnixNative.sigemptyset(mask), "sigemptyset");
				UnixNative.Check(UnixNative.sigprocmask(OperatingSystem.IsMacOS() ? 3 : 2, mask, 0), "sigprocmask");
			} finally { Marshal.FreeHGlobal(mask); }
			byte ready = (byte)'1';
			if (UnixNative.write(status, &ready, 1) != 1) throw UnixNative.Error("write startup status");
			UnixNative.execve(strings.Path, strings.Arguments, strings.Environment);
			throw UnixNative.Error("execve");
		} catch (Exception error) {
			byte[] bytes = Encoding.UTF8.GetBytes(error.Message);
			if (status >= 0) { fixed (byte* p = bytes) { UnixNative.write(status, p, (nuint)bytes.Length); } }
			else Console.Error.WriteLine(error.Message);
			return 127;
		} finally { if (status >= 0) UnixNative.close(status); if (slave >= 0) UnixNative.close(slave); }
	}
	private sealed class NativeStrings : IDisposable {
		private readonly List<nint> allocations = [];
		internal nint Path { get; }
		internal nint Arguments { get; }
		internal nint Environment { get; }
		internal NativeStrings(LaunchConfiguration config) {
			try { Path = Text(config.FileName); Arguments = Vector(new[] { config.FileName }.Concat(config.Arguments)); Environment = Vector(config.Environment.Select(p => p.Key + "=" + p.Value)); }
			catch { Dispose(); throw; }
		}
		private nint Text(string text) { nint value = Marshal.StringToCoTaskMemUTF8(text); allocations.Add(value); return value; }
		private nint Vector(IEnumerable<string> values) {
			string[] items = values.ToArray(); nint block = Marshal.AllocCoTaskMem(checked((items.Length + 1) * nint.Size)); allocations.Add(block);
			for (int i = 0; i < items.Length; i++) Marshal.WriteIntPtr(block, i * nint.Size, Text(items[i]));
			Marshal.WriteIntPtr(block, items.Length * nint.Size, 0); return block;
		}
		public void Dispose() { foreach (nint value in allocations) Marshal.FreeCoTaskMem(value); allocations.Clear(); }
	}
}
