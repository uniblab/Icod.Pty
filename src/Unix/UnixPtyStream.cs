using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Icod.Pty.Unix;

internal sealed class UnixPtyStream : Stream {
	private readonly SafeFileHandle handle;
	private readonly bool readable;
	private readonly SemaphoreSlim operation = new(1, 1);
	private int disposed;
	internal UnixPtyStream(SafeFileHandle handle, bool readable) { this.handle = handle; this.readable = readable; }
	public override bool CanRead => readable && Volatile.Read(ref disposed) == 0;
	public override bool CanWrite => !readable && Volatile.Read(ref disposed) == 0;
	public override bool CanSeek => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override void Flush() { CheckDisposed(); }
	public override Task FlushAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Flush(); return Task.CompletedTask; }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override int Read(byte[] buffer, int offset, int count) { ValidateBufferArguments(buffer, offset, count); return Read(buffer.AsSpan(offset, count)); }
	public override unsafe int Read(Span<byte> buffer) {
		CheckDirection(true); operation.Wait();
		try { fixed (byte* pointer = buffer) return Transfer(pointer, buffer.Length, true, CancellationToken.None); }
		finally { operation.Release(); }
	}
	public override void Write(byte[] buffer, int offset, int count) { ValidateBufferArguments(buffer, offset, count); Write(buffer.AsSpan(offset, count)); }
	public override unsafe void Write(ReadOnlySpan<byte> buffer) {
		CheckDirection(false); operation.Wait();
		try { fixed (byte* pointer = buffer) Transfer(pointer, buffer.Length, false, CancellationToken.None); }
		finally { operation.Release(); }
	}
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		CheckDirection(true); await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
		try { return await Task.Run(() => ReadMemory(buffer, cancellationToken), CancellationToken.None).ConfigureAwait(false); }
		finally { operation.Release(); }
	}
	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		CheckDirection(false); await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
		try { await Task.Run(() => WriteMemory(buffer, cancellationToken), CancellationToken.None).ConfigureAwait(false); }
		finally { operation.Release(); }
	}
	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { ValidateBufferArguments(buffer, offset, count); return ReadAsync(buffer.AsMemory(offset, count), token).AsTask(); }
	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) { ValidateBufferArguments(buffer, offset, count); return WriteAsync(buffer.AsMemory(offset, count), token).AsTask(); }
	private unsafe int ReadMemory(Memory<byte> memory, CancellationToken token) { using var pin = memory.Pin(); return Transfer((byte*)pin.Pointer, memory.Length, true, token); }
	private unsafe void WriteMemory(ReadOnlyMemory<byte> memory, CancellationToken token) { using var pin = memory.Pin(); Transfer((byte*)pin.Pointer, memory.Length, false, token); }
	private unsafe int Transfer(byte* buffer, int length, bool read, CancellationToken token) {
		CheckDisposed(); token.ThrowIfCancellationRequested();
		if (length == 0) return 0;
		bool reference = false;
		try {
			handle.DangerousAddRef(ref reference);
			int fd = (int)handle.DangerousGetHandle(); int transferred = 0;
			while (true) {
				CheckDisposed(); token.ThrowIfCancellationRequested();
				nint result = read ? UnixNative.read(fd, buffer, (nuint)length) : UnixNative.write(fd, buffer + transferred, (nuint)Math.Min(length - transferred, 65536));
				if (result > 0) { if (read) return checked((int)result); transferred += checked((int)result); if (transferred == length) return transferred; continue; }
				if (result == 0) { if (read) return 0; throw new IOException("PTY write made no progress."); }
				int error = Marshal.GetLastPInvokeError();
				if (read && OperatingSystem.IsLinux() && error == 5) return 0;
				if (error == 4) continue;
				if (error != (OperatingSystem.IsMacOS() ? 35 : 11)) throw UnixNative.Error(read ? "read PTY" : "write PTY", error);
				UnixNative.PollDescriptor descriptor = new() { FileDescriptor = fd, Events = (short)(read ? 1 : 4) };
				int polled = UnixNative.poll(ref descriptor, 1, 50);
				if (polled < 0 && Marshal.GetLastPInvokeError() != 4) throw UnixNative.Error("poll PTY");
			}
		} finally { if (reference) handle.DangerousRelease(); }
	}
	private void CheckDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
	private void CheckDirection(bool read) { CheckDisposed(); if (read != readable) throw new NotSupportedException(); }
	protected override void Dispose(bool disposing) { Interlocked.Exchange(ref disposed, 1); base.Dispose(disposing); }
}
