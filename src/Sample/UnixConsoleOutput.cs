using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Icod.Pty.Sample;

internal sealed class UnixConsoleOutput : Stream {
	private readonly int descriptor;
	private int disposed;
	internal UnixConsoleOutput(int standardDescriptor = 1) {
		byte[] name = new byte[1024];
		int error = Native.ttyname_r(standardDescriptor, name, (nuint)name.Length);
		if (error != 0) throw new IOException("Find output terminal failed.", new Win32Exception(error));
		string path = Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0));
		// Reopen the same terminal to obtain an independent nonblocking file description;
		// dup/fcntl would also change flags on the caller's borrowed stdout description.
		int flags = OperatingSystem.IsMacOS() ? 1 | 4 | 0x20000 | 0x1000000 : 1 | 0x800 | 0x100 | 0x80000;
		descriptor = Native.open(path, flags);
		if (descriptor < 0) throw Error("Open output terminal");
	}
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
		new(Task.Run(() => WriteMemory(buffer, cancellationToken), CancellationToken.None));
	private unsafe void WriteMemory(ReadOnlyMemory<byte> buffer, CancellationToken token) {
		using var pin = buffer.Pin(); int offset = 0;
		while (offset < buffer.Length) {
			token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
			nint written = Native.write(descriptor, (nint)((byte*)pin.Pointer + offset), (nuint)(buffer.Length - offset));
			if (written > 0) { offset += checked((int)written); continue; }
			if (written == 0) throw new IOException("Terminal output made no progress.");
			int error = Marshal.GetLastPInvokeError();
			if (error == 4) continue;
			if (error != (OperatingSystem.IsMacOS() ? 35 : 11)) throw Error("Write output terminal");
			Native.PollDescriptor ready = new() { Descriptor = descriptor, Events = 4 };
			if (Native.poll(ref ready, 1, 50) < 0 && Marshal.GetLastPInvokeError() != 4) throw Error("Poll output terminal");
		}
	}
	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { ValidateBufferArguments(buffer, offset, count); return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask(); }
	public override void Write(byte[] buffer, int offset, int count) { ValidateBufferArguments(buffer, offset, count); WriteMemory(buffer.AsMemory(offset, count), CancellationToken.None); }
	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => Volatile.Read(ref disposed) == 0;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Flush() { }
	protected override void Dispose(bool disposing) { if (Interlocked.Exchange(ref disposed, 1) == 0) Native.close(descriptor); base.Dispose(disposing); }
	private static IOException Error(string operation) => new(operation + " failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
	private static class Native {
		[StructLayout(LayoutKind.Sequential)] internal struct PollDescriptor { internal int Descriptor; internal short Events, ReturnedEvents; }
		[DllImport("libc")] internal static extern int ttyname_r(int fd, [Out] byte[] name, nuint length);
		[DllImport("libc", SetLastError = true)] internal static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
		[DllImport("libc")] internal static extern int close(int fd);
		[DllImport("libc", SetLastError = true)] internal static extern nint write(int fd, nint buffer, nuint length);
		[DllImport("libc", SetLastError = true)] internal static extern int poll(ref PollDescriptor descriptor, nuint count, int timeout);
	}
}
