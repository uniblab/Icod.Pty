using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Icod.Pty.Sample;

internal sealed class UnixHostConsole : HostConsole {
	private readonly int input;
	private readonly nint original;
	private readonly Stream output;
	private int disposed;
	internal UnixHostConsole() {
		input = Native.dup(0); Check(input >= 0, "dup stdin");
		int size = OperatingSystem.IsMacOS() ? Marshal.SizeOf<Native.DarwinTermios>() : Marshal.SizeOf<Native.LinuxTermios>();
		original = Marshal.AllocHGlobal(size);
		nint changed = Marshal.AllocHGlobal(size);
		bool captured = false;
		try {
			Check(Native.tcgetattr(input, original) == 0, "tcgetattr"); captured = true;
			byte[] copy = new byte[size]; Marshal.Copy(original, copy, 0, size); Marshal.Copy(copy, 0, changed, size);
			Native.cfmakeraw(changed); Check(Native.tcsetattr(input, 0, changed) == 0, "tcsetattr raw");
			output = Console.OpenStandardOutput();
		} catch {
			if (captured) Native.tcsetattr(input, 0, original);
			Marshal.FreeHGlobal(original); Native.close(input); throw;
		} finally { Marshal.FreeHGlobal(changed); }
	}
	internal override Stream Output => output;
	internal override PtySize? GetSize() {
		Check(Native.GetSize(input, out Native.WindowSize size) == 0, "TIOCGWINSZ");
		return size.Columns is > 0 and <= 32767 && size.Rows is > 0 and <= 32767 ? new(size.Columns, size.Rows) : null;
	}
	internal override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => new(Task.Run(() => Read(buffer, cancellationToken), CancellationToken.None));
	private unsafe int Read(Memory<byte> buffer, CancellationToken token) {
		using var pin = buffer.Pin();
		while (true) {
			token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
			Native.PollDescriptor descriptor = new() { Descriptor = input, Events = 1 };
			int ready = Native.poll(ref descriptor, 1, 50);
			if (ready < 0) { if (Marshal.GetLastPInvokeError() == 4) continue; Check(false, "poll stdin"); }
			if (ready == 0) continue;
			nint count = Native.read(input, (nint)pin.Pointer, (nuint)buffer.Length);
			if (count >= 0) return checked((int)count);
			if (Marshal.GetLastPInvokeError() != 4) Check(false, "read stdin");
		}
	}
	public override void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0) return;
		try { Check(Native.tcsetattr(input, 0, original) == 0, "restore termios"); }
		finally { Marshal.FreeHGlobal(original); Native.close(input); output.Dispose(); }
	}
	private static void Check(bool value, string operation) { if (!value) throw new IOException(operation + " failed.", new Win32Exception(Marshal.GetLastPInvokeError())); }
	private static class Native {
		[StructLayout(LayoutKind.Sequential)] internal unsafe struct LinuxTermios { public uint InputFlags, OutputFlags, ControlFlags, LocalFlags; public byte Line; public fixed byte ControlCharacters[32]; public uint InputSpeed, OutputSpeed; }
		[StructLayout(LayoutKind.Sequential)] internal unsafe struct DarwinTermios { public ulong InputFlags, OutputFlags, ControlFlags, LocalFlags; public fixed byte ControlCharacters[20]; public ulong InputSpeed, OutputSpeed; }
		[StructLayout(LayoutKind.Sequential)] internal struct WindowSize { internal ushort Rows, Columns, XPixel, YPixel; }
		[StructLayout(LayoutKind.Sequential)] internal struct PollDescriptor { internal int Descriptor; internal short Events, ReturnedEvents; }
		internal static int GetSize(int fd, out WindowSize size) => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ?
			IoctlApple(fd, 0x40087468, 0, 0, 0, 0, 0, 0, out size) : Ioctl(fd, OperatingSystem.IsMacOS() ? 0x40087468u : 0x5413u, out size);
		[DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] private static extern int Ioctl(int fd, nuint request, out WindowSize size);
		[DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] private static extern int IoctlApple(int fd, nuint request, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, out WindowSize size);
		[DllImport("libc", SetLastError = true)] internal static extern int dup(int fd);
		[DllImport("libc")] internal static extern int close(int fd);
		[DllImport("libc", SetLastError = true)] internal static extern int tcgetattr(int fd, nint state);
		[DllImport("libc", SetLastError = true)] internal static extern int tcsetattr(int fd, int action, nint state);
		[DllImport("libc")] internal static extern void cfmakeraw(nint state);
		[DllImport("libc", SetLastError = true)] internal static extern int poll(ref PollDescriptor descriptors, nuint count, int timeout);
		[DllImport("libc", SetLastError = true)] internal static extern nint read(int fd, nint buffer, nuint length);
	}
}
