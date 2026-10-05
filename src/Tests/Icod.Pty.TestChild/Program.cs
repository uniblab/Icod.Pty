using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// This isolated probe must set SIGCHLD before Console or Process installs runtime handlers.
if (args is ["scope-auto-reap-probe", _]) return await ProcessScopeFixture.AutoReapProbeAsync();
if (args is ["scope-host-guard", string guard]) return await ProcessScopeFixture.HostGuardAsync(guard);
// Capture the terminal before Console initialization or application mode changes.
if (args is ["terminal-config-state"]) return TerminalConfigurationProbe.ReportCurrentState();
if (args is ["terminal-config-read", string terminalReadSize]) return TerminalConfigurationProbe.ReadOnce(int.Parse(terminalReadSize, System.Globalization.CultureInfo.InvariantCulture));
if (args is ["terminal-config-change"]) return TerminalConfigurationProbe.ChangeOwnState();
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
if (args is ["terminal-config-native-probe"]) return await TerminalConfigurationProbe.RunAsync();
if (args is ["write-marker", string markerPath]) { File.WriteAllText(markerPath, "started"); return 0; }
if (args is ["scope-native-probe", string scopeScenario]) return await ProcessScopeFixture.ProbeAsync(scopeScenario);
if (args is ["scope-parent", string scopeDirectory]) return await ProcessScopeFixture.ParentAsync(scopeDirectory);
if (args is ["scope-parent", string scopeDirectoryMode, string scopeMode]) return await ProcessScopeFixture.ParentAsync(scopeDirectoryMode, scopeMode);
if (args is ["scope-child", string childDirectory]) return await ProcessScopeFixture.ChildAsync(childDirectory);
if (args is ["scope-child", string childDirectoryMode, string childMode]) return await ProcessScopeFixture.ChildAsync(childDirectoryMode, childMode);
if (args is ["scope-windows-probe", string windowsScopeScenario]) return await WindowsScopeProbe.RunAsync(windowsScopeScenario);
if (args is ["scope-windows-parent", string windowsScopeDirectory]) return await WindowsScopeProbe.ParentAsync(windowsScopeDirectory);
if (args is ["scope-windows-child", string windowsChildDirectory]) return await WindowsScopeProbe.ChildAsync(windowsChildDirectory);
if (args is ["scope-helper-probe", _]) return await UnixHelperSpawnProbe.RunAsync();
if (args is ["forward-chunks"]) return await HostConsoleProbe.ForwardChunksAsync();
if (args is ["host-console-probe", string scenario, string sample, string pidFile]) return await HostConsoleProbe.RunAsync(scenario, sample, pidFile);
if (args is ["hold-terminal-open", string record]) return await HostConsoleProbe.HoldTerminalAsync(record);
if (args is ["retained-holder", string recordPath]) {
	using System.Runtime.InteropServices.PosixSignalRegistration? hangup = OperatingSystem.IsWindows() ? null :
		System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGHUP, e => e.Cancel = true);
	File.WriteAllText(recordPath, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
	await Task.Delay(TimeSpan.FromSeconds(60)); return 0;
}
if (args is ["exit"]) { Console.WriteLine("FINAL-MARKER"); return 37; }
if (args is ["backpressure-exit", string finished]) {
	using Stream stdout = Console.OpenStandardOutput();
	stdout.Write(new byte[20000]); File.WriteAllText(finished, "finished"); return 0;
}
if (args is ["flood"]) { while (true) Console.Write(new string('x', 4096)); }
if (args is ["final-output"]) {
	Console.WriteLine("FINAL-READY");
	using StreamReader commands = new(Console.OpenStandardInput(), Encoding.UTF8);
	if (commands.ReadLine() != "quit") return 1;
	for (int i = 0; i < 1024; i++) Console.WriteLine($"FINAL:{i:D4}:" + new string('x', 128));
	Console.WriteLine("FINAL-END");
	return 23;
}
if (args is ["raw-sequence", string length, string trace, string readSize]) {
	using IDisposable mode = TerminalModes.EnterRawInput();
	using FileStream received = new(trace, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete, 1);
	Console.WriteLine("RAW-READY");
	byte[] bytes = new byte[int.Parse(length, System.Globalization.CultureInfo.InvariantCulture)];
	byte[] block = new byte[int.Parse(readSize, System.Globalization.CultureInfo.InvariantCulture)];
	for (int i = 0; i < bytes.Length;) {
		int count = TerminalModes.Read(block); if (count == 0 || count > bytes.Length - i) return 2;
		block.AsSpan(0, count).CopyTo(bytes.AsSpan(i)); i += count;
		received.Write(block, 0, count); received.Flush();
	}
	Console.WriteLine("SEQUENCE:" + Convert.ToHexString(bytes));
	return TerminalModes.ReadByte() == 4 ? 23 : 2;
}
if (args is ["raw-input"]) {
	using IDisposable mode = TerminalModes.EnterRawInput();
	Console.WriteLine("RAW-READY");
	int value;
	while ((value = TerminalModes.ReadByte()) >= 0) {
		Console.WriteLine($"BYTE:{value:X2}");
		if (value == 4) return 23;
	}
	return 0;
}
if (args is ["interrupt-handler"]) {
	using IDisposable mode = TerminalModes.EnterProcessedInput();
	ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; Console.WriteLine("INTERRUPT-ACK"); };
	Console.CancelKeyPress += handler;
	try {
		Console.WriteLine("INTERRUPT-READY");
		using StreamReader commands = new(Console.OpenStandardInput(), Encoding.UTF8);
		while (true) {
			string? command = commands.ReadLine();
			// Windows ReadFile can report a successful zero-byte console read on Ctrl+C.
			// That is not EOF for this fixture: only the explicit quit command ends it.
			if (command == null) { if (OperatingSystem.IsWindows()) continue; return 0; }
			if (command == "quit") { Console.WriteLine("BYE-MARKER"); return 23; }
		}
	} finally { Console.CancelKeyPress -= handler; }
}
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
// Read the terminal's canonical byte stream without Console.ReadLine's terminal-emulator queries.
using StreamReader input = new(Console.OpenStandardInput(), Encoding.UTF8);
while (input.ReadLine() is string line) {
	if (line == "quit") { Console.WriteLine("BYE-MARKER"); return 23; }
	if (line.StartsWith("size:", StringComparison.Ordinal)) {
		int columns, rows;
		if (OperatingSystem.IsWindows()) { columns = Console.WindowWidth; rows = Console.WindowHeight; }
		else {
			if (Native.GetSize(out Native.WindowSize size) != 0) throw new IOException("TIOCGWINSZ failed.");
			columns = size.Columns; rows = size.Rows;
		}
		Console.WriteLine($"SIZE:{line[5..]}:{columns},{rows}");
	} else Console.WriteLine("ECHO:" + line);
}
return 0;

internal static class Native {
	[StructLayout(LayoutKind.Sequential)]
	internal struct WindowSize { internal ushort Rows, Columns, XPixel, YPixel; }
	internal static int GetSize(out WindowSize size) => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ?
		IoctlApple(0, 0x40087468, 0, 0, 0, 0, 0, 0, out size) : Ioctl(0, OperatingSystem.IsMacOS() ? 0x40087468u : 0x5413u, out size);
	[DllImport("libc", EntryPoint = "ioctl")] private static extern int Ioctl(int fd, nuint request, out WindowSize size);
	// Apple ARM64 variadic arguments begin on the stack, after the eight register slots.
	[DllImport("libc", EntryPoint = "ioctl")] private static extern int IoctlApple(int fd, nuint request, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, out WindowSize size);
	[DllImport("libc")] internal static extern int isatty(int fd);
	[DllImport("libc")] internal static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
	[DllImport("libc")] internal static extern int close(int fd);
}
