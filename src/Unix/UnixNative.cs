using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Icod.Pty.Unix;

internal static class UnixNative {
	[StructLayout(LayoutKind.Sequential)]
	internal struct WindowSize { internal ushort Rows, Columns, XPixel, YPixel; }
	[StructLayout(LayoutKind.Sequential)]
	internal struct PollDescriptor { internal int FileDescriptor; internal short Events, ReturnedEvents; }
	internal static nuint SetWindowSize => OperatingSystem.IsMacOS() ? 0x80087467u : 0x5414u;
	internal static nuint SetControllingTerminal => OperatingSystem.IsMacOS() ? 0x20007461u : 0x540Eu;
	internal static int NonBlocking => OperatingSystem.IsMacOS() ? 4 : 0x800;
	[DllImport("libc", SetLastError = true)] internal static extern int openpty(out int master, out int slave, [Out] byte[] name, nint termios, ref WindowSize size);
	[DllImport("libc", SetLastError = true)] internal static extern int fcntl(int fd, int command, int value);
	[DllImport("libc", SetLastError = true)] internal static extern int close(int fd);
	[DllImport("libc", SetLastError = true)] internal static extern int ioctl(int fd, nuint request, ref WindowSize size);
	[DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] internal static extern int ioctl_value(int fd, nuint request, nint value);
	[DllImport("libc", SetLastError = true)] internal static extern int poll(ref PollDescriptor descriptor, nuint count, int timeout);
	[DllImport("libc", SetLastError = true)] internal static extern unsafe nint read(int fd, byte* buffer, nuint count);
	[DllImport("libc", SetLastError = true)] internal static extern unsafe nint write(int fd, byte* buffer, nuint count);
	[DllImport("libc", SetLastError = true)] internal static extern int setsid();
	[DllImport("libc", SetLastError = true)] internal static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
	[DllImport("libc", SetLastError = true)] internal static extern int dup(int fd);
	[DllImport("libc", SetLastError = true)] internal static extern int dup2(int source, int destination);
	[DllImport("libc", SetLastError = true)] internal static extern int execve(nint path, nint arguments, nint environment);
	[DllImport("libc", SetLastError = true)] internal static extern nint signal(int signal, nint handler);
	[DllImport("libc", SetLastError = true)] internal static extern int sigemptyset(nint set);
	[DllImport("libc", SetLastError = true)] internal static extern int sigprocmask(int how, nint set, nint old);
	internal static void Check(int result, string operation) { if (result < 0) throw Error(operation); }
	internal static IOException Error(string operation, int? error = null) {
		int code = error ?? Marshal.GetLastPInvokeError();
		return new IOException($"{operation} failed (errno {code}).", new Win32Exception(code));
	}
}
