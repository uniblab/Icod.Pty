using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Windows;

internal sealed class WindowsBackend : IPtyBackend {
	private const int ErrorAccessDenied = 5;
	private readonly SafeProcessHandle process;
	private readonly SafePseudoConsoleHandle console;
	private readonly WindowsJob? job;
	private readonly object gate = new();
	private bool disposed;
	public Stream Input { get; }
	public Stream Output { get; }
	public int ProcessId { get; }
	public PtyProcessOwnership Ownership => job == null ? PtyProcessOwnership.PrimaryProcess : PtyProcessOwnership.PlatformScope;
	public PtyProcessCapabilities Capabilities => job == null ? PtyProcessCapabilities.None : PtyProcessCapabilities.TerminateOwnedScope;
	public PtyControlResult RequestTermination(PtyProcessTarget target) {
		lock (gate) {
			ObjectDisposedException.ThrowIf(disposed, this);
			if (target == PtyProcessTarget.OwnedScope) return (job ?? throw new InvalidOperationException("OwnedScope requires platform-scope ownership at launch.")).RequestTermination();
			if (target != PtyProcessTarget.PrimaryProcess) throw new ArgumentOutOfRangeException(nameof(target));
			return PrimaryProcessControl.RequestNative(
				() => { uint state = WindowsNative.WaitForSingleObject(process, 0); if (state == uint.MaxValue) throw Error("WaitForSingleObject PrimaryProcess"); return state == 0; },
				() => WindowsNative.TerminateProcess(process, 1), Marshal.GetLastPInvokeError);
		}
	}
	public PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target) => throw new PlatformNotSupportedException("Native Unix signals are not available on Windows.");
	public Task<int> Exit { get; }
	private WindowsBackend(SafeProcessHandle process, SafePseudoConsoleHandle console, Stream input, Stream output, int id, WindowsJob? job) {
		this.process = process; this.console = console; this.job = job; Input = input; Output = output; ProcessId = id;
		Exit = Task.Factory.StartNew(() => {
			if (WindowsNative.WaitForSingleObject(process, uint.MaxValue) != 0) throw Error("WaitForSingleObject");
			if (!WindowsNative.GetExitCodeProcess(process, out uint code)) throw Error("GetExitCodeProcess");
			// ClosePseudoConsole is nonblocking on supported Windows builds.
			// Closing after child exit allows the caller to finish draining output.
			lock (gate) { if (!disposed) console.Dispose(); }
			return unchecked((int)code);
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}
	internal static Task<IPtyBackend> StartAsync(LaunchConfiguration launch, CancellationToken cancellationToken) =>
		StartAsync(launch, cancellationToken, WindowsJob.CreateAssigned, WindowsNative.ResumeThread);
	internal static Task<IPtyBackend> StartAsync(LaunchConfiguration launch, CancellationToken cancellationToken,
		Func<SafeProcessHandle, WindowsJob> assignScope, Func<nint, uint> resumeThread) =>
		Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); return Start(launch, assignScope, resumeThread); });
	private static IPtyBackend Start(LaunchConfiguration launch, Func<SafeProcessHandle, WindowsJob> assignScope, Func<nint, uint> resumeThread) {
		NamedPipeServerStream? input = null, output = null;
		NamedPipeClientStream? inputClient = null, outputClient = null;
		SafePseudoConsoleHandle? console = null;
		SafeProcessHandle? process = null;
		WindowsJob? job = null;
		nint attributes = 0, environment = 0, thread = 0;
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
			uint flags = 0x00080000 | 0x00000400;
			if (launch.Ownership == PtyProcessOwnership.PlatformScope) flags |= 4; // CREATE_SUSPENDED
			if (!WindowsNative.CreateProcessW(launch.FileName, command, 0, 0, false, flags, environment, launch.WorkingDirectory, ref startup, out WindowsNative.ProcessInformation native)) throw Error("CreateProcessW");
			process = new SafeProcessHandle(native.Process, true);
			thread = native.Thread;
			if (launch.Ownership == PtyProcessOwnership.PlatformScope) {
				job = assignScope(process);
				if (resumeThread(thread) == uint.MaxValue) throw Error("ResumeThread");
			}
			return new WindowsBackend(process, console, input, output, checked((int)native.ProcessId), job);
		} catch (Exception failure) {
			CleanupActions.AfterFailure(failure, () => job?.Dispose(),
				() => { if (process != null) { try { WindowsNative.TerminateProcess(process, 1); WindowsNative.WaitForSingleObject(process, uint.MaxValue); } finally { process.Dispose(); } } },
				() => input?.Dispose(), () => output?.Dispose(), () => console?.Dispose());
			throw;
		} finally {
			if (thread != 0) WindowsNative.CloseHandle(thread);
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
			TerminatePrimary(milliseconds => WindowsNative.WaitForSingleObject(process, milliseconds),
				() => WindowsNative.TerminateProcess(process, 1), Marshal.GetLastPInvokeError);
		}
	}
	internal static void TerminatePrimary(Func<uint, uint> wait, Func<bool> terminate, Func<int> getLastError) {
		uint state = wait(0);
		if (state == 0) return;
		if (state == uint.MaxValue) throw Error("WaitForSingleObject PrimaryProcess", getLastError());
		if (terminate()) return;

		int terminateError = getLastError();
		state = wait(0);
		if (state == 0) return;
		if (state == uint.MaxValue) throw Error("WaitForSingleObject PrimaryProcess", getLastError());
		// TerminateProcess documents ERROR_ACCESS_DENIED after the target has terminated. Its handle can become
		// signaled asynchronously, so finish collecting that terminal race instead of reporting cleanup failure.
		if (terminateError == ErrorAccessDenied) {
			state = wait(uint.MaxValue);
			if (state == 0) return;
			if (state == uint.MaxValue) throw Error("WaitForSingleObject PrimaryProcess", getLastError());
		}
		throw Error("TerminateProcess", terminateError);
	}
	public void Dispose() {
		lock (gate) { if (disposed) return; disposed = true; }
		bool terminationRequested = false;
		CleanupActions.Run(
			() => { if (job != null) job.RequestTermination(); else Terminate(); terminationRequested = true; },
			() => { if (job != null) { job.Dispose(); terminationRequested = true; } },
			Input.Dispose, Output.Dispose, console.Dispose,
			() => { if (terminationRequested) Exit.GetAwaiter().GetResult(); }, process.Dispose);
	}
	private static IOException Error(string operation) => Error(operation, Marshal.GetLastPInvokeError());
	private static IOException Error(string operation, int code) => new($"{operation} failed (Win32 error {code}).", new Win32Exception(code));
	private static void CheckHResult(int result, string operation) { if (result < 0) throw new IOException($"{operation} failed (HRESULT 0x{result:X8}).", Marshal.GetExceptionForHR(result)); }
}
