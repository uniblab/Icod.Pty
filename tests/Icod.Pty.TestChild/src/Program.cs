using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
if (args is ["exit"]) { Console.WriteLine("FINAL-MARKER"); return 37; }
if (args is ["flood"]) { while (true) Console.Write(new string('x', 4096)); }
bool terminal = OperatingSystem.IsWindows() ? !Console.IsInputRedirected : Native.isatty(0) == 1 && Native.isatty(1) == 1 && Native.isatty(2) == 1;
bool controlling = true;
if (!OperatingSystem.IsWindows()) {
	int fd = Native.open("/dev/tty", 2);
	controlling = fd >= 0;
	if (fd >= 0) Native.close(fd);
}
Console.WriteLine("READY:" + (args.Length == 0 ? "ok" : JsonSerializer.Serialize(new {
	Arguments = args,
	Directory = Environment.CurrentDirectory,
	Value = Environment.GetEnvironmentVariable("ICOD_PTY_TEST_VALUE"),
	Removed = Environment.GetEnvironmentVariable("ICOD_PTY_TEST_REMOVED"),
	Terminal = terminal,
	ControllingTerminal = controlling
})));
while (Console.ReadLine() is string line) {
	if (line == "quit") { Console.WriteLine("BYE-MARKER"); return 23; }
	if (line == "size") {
		int columns, rows;
		if (OperatingSystem.IsWindows()) { columns = Console.WindowWidth; rows = Console.WindowHeight; }
		else {
			if (Native.ioctl(0, OperatingSystem.IsMacOS() ? 0x40087468u : 0x5413u, out Native.WindowSize size) != 0) throw new IOException("TIOCGWINSZ failed.");
			columns = size.Columns; rows = size.Rows;
		}
		Console.WriteLine($"SIZE:{columns},{rows}");
	} else Console.WriteLine("ECHO:" + line);
}
return 0;

internal static class Native {
	[StructLayout(LayoutKind.Sequential)]
	internal struct WindowSize { internal ushort Rows, Columns, XPixel, YPixel; }
	[DllImport("libc")] internal static extern int isatty(int fd);
	[DllImport("libc")] internal static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
	[DllImport("libc")] internal static extern int close(int fd);
	[DllImport("libc")] internal static extern int ioctl(int fd, nuint request, out WindowSize size);
}
