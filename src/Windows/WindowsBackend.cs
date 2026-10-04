using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Windows;

internal sealed class WindowsBackend : IPtyBackend {
	private readonly SafeProcessHandle process;
	private readonly SafePseudoConsoleHandle console;
	private readonly object gate = new();
	private bool disposed;
	public Stream Input { get; }
	public Stream Output { get; }
	public int ProcessId { get; }
	public Task<int> Exit { get; }
	private WindowsBackend(SafeProcessHandle process, SafePseudoConsoleHandle console, Stream input, Stream output, int id) {
		this.process = process; this.console = console; Input = input; Output = output; ProcessId = id;
		Exit = Task.Factory.StartNew(() => {
			if (WindowsNative.WaitForSingleObject(process, uint.MaxValue) != 0) throw Error("WaitForSingleObject");
			if (!WindowsNative.GetExitCodeProcess(process, out uint code)) throw Error("GetExitCodeProcess");
			// ClosePseudoConsole is nonblocking on supported Windows builds.
			// Closing after child exit allows the caller to finish draining output.
			lock (gate) { if (!disposed) console.Dispose(); }
			return unchecked((int)code);
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}
	internal static IPtyBackend Start(LaunchConfiguration launch) {
		NamedPipeServerStream? input = null, output = null;
		NamedPipeClientStream? inputClient = null, outputClient = null;
		SafePseudoConsoleHandle? console = null;
		SafeProcessHandle? process = null;
		nint attributes = 0, environment = 0;
		bool attributesInitialized = false;
		try {
			(input, inputClient) = CreatePipe(false);
			(output, outputClient) = CreatePipe(true);
			int result = WindowsNative.CreatePseudoConsole(new() { X = (short)launch.Columns, Y = (short)launch.Rows }, inputClient.SafePipeHandle, outputClient.SafePipeHandle, 0, out console);
			CheckHResult(result, "CreatePseudoConsole");
			nuint bytes = 0;
			WindowsNative.InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
			if (bytes == 0) throw Error("InitializeProcThreadAttributeList size");
			attributes = Marshal.AllocHGlobal(checked((nint)bytes));
			if (!WindowsNative.InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw Error("InitializeProcThreadAttributeList");
			attributesInitialized = true;
			if (!WindowsNative.UpdateProcThreadAttribute(attributes, 0, 0x00020016, console.DangerousGetHandle(), (nuint)nint.Size, 0, 0)) throw Error("UpdateProcThreadAttribute");
			// Explicit null standard handles prevent redirected parent handles from overriding ConPTY.
			WindowsNative.StartupInfoEx startup = new() { Startup = new() { Size = (uint)Marshal.SizeOf<WindowsNative.StartupInfoEx>(), Flags = 0x00000100 }, Attributes = attributes };
			string environmentBlock = string.Join('\0', launch.Environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0";
			environment = Marshal.StringToHGlobalUni(environmentBlock);
			StringBuilder command = new(Quote(launch.FileName));
			foreach (string argument in launch.Arguments) command.Append(' ').Append(Quote(argument));
			if (!WindowsNative.CreateProcessW(launch.FileName, command, 0, 0, false, 0x00080000 | 0x00000400, environment, launch.WorkingDirectory, ref startup, out WindowsNative.ProcessInformation native)) throw Error("CreateProcessW");
			process = new SafeProcessHandle(native.Process, true);
			WindowsNative.CloseHandle(native.Thread);
			return new WindowsBackend(process, console, input, output, checked((int)native.ProcessId));
		} catch {
			if (process != null) { WindowsNative.TerminateProcess(process, 1); WindowsNative.WaitForSingleObject(process, uint.MaxValue); process.Dispose(); }
			input?.Dispose(); output?.Dispose(); console?.Dispose();
			throw;
		} finally {
			inputClient?.Dispose(); outputClient?.Dispose();
			if (attributesInitialized) WindowsNative.DeleteProcThreadAttributeList(attributes);
			if (attributes != 0) Marshal.FreeHGlobal(attributes);
			if (environment != 0) Marshal.FreeHGlobal(environment);
		}
	}
	private static (NamedPipeServerStream, NamedPipeClientStream) CreatePipe(bool parentReads) {
		string name = "icod-pty-" + Guid.NewGuid().ToString("N");
		NamedPipeServerStream server = new(name, parentReads ? PipeDirection.In : PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
		NamedPipeClientStream? client = null;
		try {
			Task connected = server.WaitForConnectionAsync();
			client = new NamedPipeClientStream(".", name, parentReads ? PipeDirection.Out : PipeDirection.In, PipeOptions.None);
			client.Connect(5000); connected.GetAwaiter().GetResult();
			return (server, client);
		} catch { client?.Dispose(); server.Dispose(); throw; }
	}
	// Microsoft C runtime argument quoting; the executable is passed separately too.
	private static string Quote(string argument) {
		// Leave simple arguments unquoted, as ProcessStartInfo.ArgumentList does.
		// In particular, cmd.exe parses switches such as /c before CRT-style quoting.
		if (argument.Length > 0 && !argument.Any(value => char.IsWhiteSpace(value) || value == '"')) return argument;
		StringBuilder result = new("\""); int slashes = 0;
		foreach (char value in argument) {
			if (value == '\\') { slashes++; continue; }
			if (value == '"') result.Append('\\', slashes * 2 + 1).Append('"');
			else result.Append('\\', slashes).Append(value);
			slashes = 0;
		}
		return result.Append('\\', slashes * 2).Append('"').ToString();
	}
	public void Resize(PtySize size) { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); if (console.IsClosed) throw new InvalidOperationException("The child has exited."); CheckHResult(WindowsNative.ResizePseudoConsole(console, new() { X = (short)size.Columns, Y = (short)size.Rows }), "ResizePseudoConsole"); } }
	public void Terminate() {
		lock (gate) {
			if (WindowsNative.WaitForSingleObject(process, 0) == 0) return;
			if (!WindowsNative.TerminateProcess(process, 1) && WindowsNative.WaitForSingleObject(process, 0) != 0) throw Error("TerminateProcess");
		}
	}
	public void Dispose() {
		lock (gate) {
			if (disposed) return; disposed = true;
			try { Terminate(); }
			finally { Input.Dispose(); Output.Dispose(); console.Dispose(); }
		}
		try { Exit.GetAwaiter().GetResult(); } finally { process.Dispose(); }
	}
	private static IOException Error(string operation) { int code = Marshal.GetLastPInvokeError(); return new IOException($"{operation} failed (Win32 error {code}).", new Win32Exception(code)); }
	private static void CheckHResult(int result, string operation) { if (result < 0) throw new IOException($"{operation} failed (HRESULT 0x{result:X8}).", Marshal.GetExceptionForHR(result)); }
}
