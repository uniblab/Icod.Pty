using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Sample;

internal sealed class WindowsConsoleOutput : Stream {
	private readonly SafeFileHandle output;
	private readonly SafeWaitHandle threadHandle;
	private readonly Thread writer;
	private readonly CancellationTokenSource stop = new();
	private readonly BlockingCollection<WriteRequest> requests = new();
	private int disposed;
	internal WindowsConsoleOutput() {
		output = Native.CreateFileW("CONOUT$", 0x40000000, 3, 0, 3, 0, 0);
		try {
			if (output.IsInvalid) throw Error("Open console output");
			TaskCompletionSource<SafeWaitHandle> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
			writer = new Thread(() => WriteLoop(started)) { IsBackground = true, Name = "Icod.Pty.Sample output" };
			writer.Start(); threadHandle = started.Task.GetAwaiter().GetResult();
		} catch { output.Dispose(); requests.Dispose(); stop.Dispose(); throw; }
	}
	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
		linked.Token.ThrowIfCancellationRequested();
		WriteRequest request = new(buffer, linked.Token); requests.Add(request);
		try { await request.Completion.Task.WaitAsync(linked.Token).ConfigureAwait(false); }
		catch (OperationCanceledException) when (linked.IsCancellationRequested) {
			while (!request.Completion.Task.IsCompleted) {
				CancelWrite(); await Task.WhenAny(request.Completion.Task, Task.Delay(10)).ConfigureAwait(false);
			}
			await request.Completion.Task.ConfigureAwait(false);
		}
	}
	private void WriteLoop(TaskCompletionSource<SafeWaitHandle> started) {
		SafeWaitHandle handle = new(Native.OpenThread(1, false, Native.GetCurrentThreadId()), true);
		if (handle.IsInvalid) { started.SetException(Error("Open writer thread")); handle.Dispose(); return; }
		started.SetResult(handle);
		foreach (WriteRequest request in requests.GetConsumingEnumerable()) {
			try { WriteMemory(request.Bytes, request.Token); request.Completion.TrySetResult(); }
			catch (OperationCanceledException) { request.Completion.TrySetCanceled(request.Token); }
			catch (Exception error) { request.Completion.TrySetException(error); }
		}
	}
	private unsafe void WriteMemory(ReadOnlyMemory<byte> bytes, CancellationToken token) {
		using var pin = bytes.Pin(); int offset = 0;
		while (offset < bytes.Length) {
			token.ThrowIfCancellationRequested();
			if (!Native.WriteFile(output, (nint)((byte*)pin.Pointer + offset), (uint)(bytes.Length - offset), out uint written, 0)) {
				if (Marshal.GetLastPInvokeError() == 995 && token.IsCancellationRequested) throw new OperationCanceledException(token);
				throw Error("Write console");
			}
			if (written == 0) { token.ThrowIfCancellationRequested(); throw new IOException("Console output made no progress."); }
			offset += checked((int)written);
		}
	}
	private void CancelWrite() { if (!Native.CancelSynchronousIo(threadHandle) && Marshal.GetLastPInvokeError() != 1168) throw Error("Cancel console write"); }
	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { ValidateBufferArguments(buffer, offset, count); return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask(); }
	public override void Write(byte[] buffer, int offset, int count) { ValidateBufferArguments(buffer, offset, count); WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult(); }
	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => Volatile.Read(ref disposed) == 0;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Flush() { }
	protected override void Dispose(bool disposing) {
		if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) {
			stop.Cancel(); requests.CompleteAdding();
			try { while (!writer.Join(50)) CancelWrite(); }
			finally { threadHandle.Dispose(); output.Dispose(); requests.Dispose(); stop.Dispose(); }
		}
		base.Dispose(disposing);
	}
	private sealed class WriteRequest(ReadOnlyMemory<byte> bytes, CancellationToken token) {
		internal ReadOnlyMemory<byte> Bytes { get; } = bytes;
		internal CancellationToken Token { get; } = token;
		internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
	private static IOException Error(string operation) => new(operation + " failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
	private static class Native {
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
		[DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
		[DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CancelSynchronousIo(SafeWaitHandle thread);
		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WriteFile(SafeFileHandle handle, nint bytes, uint length, out uint written, nint overlapped);
	}
}
