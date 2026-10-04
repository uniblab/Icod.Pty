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
	// Darwin ARM64 puts variadic arguments on the stack. Fill x2..x7 so the
	// single integer/pointer vararg occupies the first stack slot. Other ABIs
	// used here pass this argument in a register. No floating-point varargs are used.
	// https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms
	private static bool AppleArm64 => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
	internal static int fcntl(int fd, int command, int value) => AppleArm64 ? FcntlApple(fd, command, 0, 0, 0, 0, 0, 0, value) : FcntlNative(fd, command, value);
	[DllImport("libc", EntryPoint = "fcntl", SetLastError = true)] private static extern int FcntlNative(int fd, int command, int value);
	[DllImport("libc", EntryPoint = "fcntl", SetLastError = true)] private static extern int FcntlApple(int fd, int command, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, nint value);
	[DllImport("libc", SetLastError = true)] internal static extern int close(int fd);
	internal static unsafe int ioctl(int fd, nuint request, ref WindowSize size) { fixed (WindowSize* pointer = &size) return ioctl_value(fd, request, (nint)pointer); }
	internal static int ioctl_value(int fd, nuint request, nint value) => AppleArm64 ? IoctlApple(fd, request, 0, 0, 0, 0, 0, 0, value) : IoctlNative(fd, request, value);
	[DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] private static extern int IoctlNative(int fd, nuint request, nint value);
	[DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] private static extern int IoctlApple(int fd, nuint request, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, nint value);
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
