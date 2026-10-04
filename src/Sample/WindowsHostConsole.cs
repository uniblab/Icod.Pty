using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Sample;

internal sealed class WindowsHostConsole : HostConsole {
	private readonly nint input = Native.GetStdHandle(-10), outputHandle = Native.GetStdHandle(-11);
	private readonly uint inputMode, outputMode, inputCodePage, outputCodePage;
	private readonly Stream output;
	private readonly SafeFileHandle readInput;
	private readonly BlockingCollection<ReadRequest> requests = new();
	private readonly CancellationTokenSource lifetime = new();
	private readonly Thread reader;
	private readonly SafeWaitHandle readerHandle;
	private int disposed;
	internal WindowsHostConsole() {
		Check(Native.GetConsoleMode(input, out inputMode), "get input mode");
		Check(Native.GetConsoleMode(outputHandle, out outputMode), "get output mode");
		inputCodePage = Native.GetConsoleCP(); outputCodePage = Native.GetConsoleOutputCP();
		// Use an independent console input handle. A cancelled ReadFile can leave
		// per-handle read state; never pass that state back through borrowed stdin.
		readInput = Native.CreateFileW("CONIN$", 0x80000000, 3, 0, 3, 0, 0);
		try {
			Check(!readInput.IsInvalid, "open console input");
			Check(Native.SetConsoleCP(65001), "set input encoding"); Check(Native.SetConsoleOutputCP(65001), "set output encoding");
#if ICOD_PTY_TEST_FAULTS
			HostConsoleFaults.AfterModeChange?.Invoke();
#endif
			Check(Native.SetConsoleMode(input, (inputMode & ~0x47u) | 0x280u), "set input mode");
			Check(Native.SetConsoleMode(outputHandle, outputMode | 0xdu), "set output mode");
			output = new WindowsConsoleOutput();
			TaskCompletionSource<SafeWaitHandle> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
			reader = new Thread(() => ReadLoop(started)) { IsBackground = true, Name = "Icod.Pty.Sample input" };
			reader.Start(); readerHandle = started.Task.GetAwaiter().GetResult();
		} catch {
			try { Restore(); }
			finally { readInput.Dispose(); output?.Dispose(); requests.Dispose(); lifetime.Dispose(); }
			throw;
		}
	}
	internal override Stream Output => output;
	internal override PtySize? GetSize() {
		Check(Native.GetConsoleScreenBufferInfo(outputHandle, out Native.BufferInfo info), "get window size");
		int columns = info.Window.Right - info.Window.Left + 1, rows = info.Window.Bottom - info.Window.Top + 1;
		return columns is > 0 and <= 32767 && rows is > 0 and <= 32767 ? new(columns, rows) : null;
	}
	internal override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
		linked.Token.ThrowIfCancellationRequested();
		ReadRequest request = new(buffer, linked.Token); requests.Add(request);
		try { return await request.Completion.Task.WaitAsync(linked.Token).ConfigureAwait(false); }
		catch (OperationCanceledException) when (linked.IsCancellationRequested) {
			// A cancellation can arrive just before ReadFile enters the kernel. Keep requesting
			// cancellation until this dedicated reader completes; never reuse its buffer early.
			while (!request.Completion.Task.IsCompleted) {
				CancelRead(); await Task.WhenAny(request.Completion.Task, Task.Delay(10)).ConfigureAwait(false);
			}
			return await request.Completion.Task.ConfigureAwait(false);
		}
	}
	private void ReadLoop(TaskCompletionSource<SafeWaitHandle> started) {
		SafeWaitHandle handle = new(Native.OpenThread(1, false, Native.GetCurrentThreadId()), true);
		if (handle.IsInvalid) { started.SetException(Error("open reader thread")); handle.Dispose(); return; }
		started.SetResult(handle);
		foreach (ReadRequest request in requests.GetConsumingEnumerable()) {
			try {
				request.Token.ThrowIfCancellationRequested();
				int count = ReadNative(request.Buffer, request.Token); request.Completion.TrySetResult(count);
			} catch (OperationCanceledException) { request.Completion.TrySetCanceled(request.Token); }
			catch (Exception error) { request.Completion.TrySetException(error); }
		}
	}
	private unsafe int ReadNative(Memory<byte> buffer, CancellationToken token) {
		using var pin = buffer.Pin();
#if ICOD_PTY_TEST_FAULTS
		HostConsoleFaults.BeforeRead?.Invoke();
#endif
		if (Native.ReadFile(readInput, (nint)pin.Pointer, (uint)buffer.Length, out uint count, 0)) return checked((int)count);
		if (Marshal.GetLastPInvokeError() == 995 && token.IsCancellationRequested) throw new OperationCanceledException(token);
		throw Error("read console");
	}
	private void CancelRead() {
		if (!Native.CancelSynchronousIo(readerHandle) && Marshal.GetLastPInvokeError() != 1168) throw Error("cancel console read");
	}
	private void Restore() {
		// Attempt every restoration even if one console handle has already been closed.
		Exception? failure = null;
		void Attempt(bool success, string operation) { if (!success) failure ??= Error(operation); }
		Attempt(Native.SetConsoleMode(input, inputMode), "restore input mode");
		Attempt(Native.SetConsoleMode(outputHandle, outputMode), "restore output mode");
		Attempt(Native.SetConsoleCP(inputCodePage), "restore input encoding");
		Attempt(Native.SetConsoleOutputCP(outputCodePage), "restore output encoding");
		if (failure != null) throw failure;
	}
	public override void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0) return;
		try {
			lifetime.Cancel(); requests.CompleteAdding();
			while (!reader.Join(50)) CancelRead();
		} finally {
			try { Restore(); }
			finally { readerHandle.Dispose(); readInput.Dispose(); requests.Dispose(); lifetime.Dispose(); output.Dispose(); }
		}
	}
	private sealed class ReadRequest(Memory<byte> buffer, CancellationToken token) {
		internal Memory<byte> Buffer { get; } = buffer;
		internal CancellationToken Token { get; } = token;
		internal TaskCompletionSource<int> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
	private static IOException Error(string operation) => new(operation + " failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
	private static void Check(bool success, string operation) { if (!success) throw Error(operation); }
	private static class Native {
		[StructLayout(LayoutKind.Sequential)] internal struct Coordinate { internal short X, Y; }
		[StructLayout(LayoutKind.Sequential)] internal struct Rectangle { internal short Left, Top, Right, Bottom; }
		[StructLayout(LayoutKind.Sequential)] internal struct BufferInfo { internal Coordinate Size, Cursor; internal ushort Attributes; internal Rectangle Window; internal Coordinate MaximumSize; }
		[DllImport("kernel32.dll")] internal static extern nint GetStdHandle(int which);
		[DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
		[DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CancelSynchronousIo(SafeWaitHandle thread);
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReadFile(SafeFileHandle handle, nint buffer, uint length, out uint read, nint overlapped);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetConsoleMode(nint handle, out uint mode);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetConsoleMode(nint handle, uint mode);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetConsoleScreenBufferInfo(nint handle, out BufferInfo info);
		[DllImport("kernel32.dll")] internal static extern uint GetConsoleCP();
		[DllImport("kernel32.dll")] internal static extern uint GetConsoleOutputCP();
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetConsoleCP(uint page);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetConsoleOutputCP(uint page);
	}
}
