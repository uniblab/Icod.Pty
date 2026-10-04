using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class TerminalModes {
	internal static byte[] Snapshot() {
		if (OperatingSystem.IsWindows()) {
			Check(GetConsoleMode(GetStdHandle(-10), out uint input)); Check(GetConsoleMode(GetStdHandle(-11), out uint output));
			return new[] { input, output, GetConsoleCP(), GetConsoleOutputCP() }.SelectMany(BitConverter.GetBytes).ToArray();
		}
		int size = OperatingSystem.IsMacOS() ? Marshal.SizeOf<DarwinTermios>() : Marshal.SizeOf<LinuxTermios>();
		nint state = Marshal.AllocHGlobal(size);
		try {
			byte[] bytes = new byte[size]; Marshal.Copy(bytes, 0, state, size);
			Check(tcgetattr(0, state) == 0); Marshal.Copy(state, bytes, 0, size);
			// Compare native fields, not ABI padding that tcgetattr may leave unspecified.
			if (OperatingSystem.IsMacOS()) Array.Clear(bytes, 52, 4); else Array.Clear(bytes, 49, 3);
			return bytes;
		} finally { Marshal.FreeHGlobal(state); }
	}
	internal static IDisposable EnterRawInput() => Enter(true);
	internal static IDisposable EnterProcessedInput() => Enter(false);
	// Console.OpenStandardInput uses the managed line reader on Unix terminals.
	internal static int ReadByte() {
		while (true) {
			byte value;
			if (OperatingSystem.IsWindows()) {
				Check(ReadFile(GetStdHandle(-10), out value, 1, out uint count, 0));
				return count == 0 ? -1 : value;
			}
			nint read = NativeRead(0, out value, 1);
			if (read >= 0) return read == 0 ? -1 : value;
			if (Marshal.GetLastPInvokeError() != 4) Check(false);
		}
	}
	private static IDisposable Enter(bool raw) {
		if (OperatingSystem.IsWindows()) {
			nint input = GetStdHandle(-10);
			Check(GetConsoleMode(input, out uint saved));
			uint changed = raw ? (saved & ~7u) | 0x200u : (saved | 3u) & ~0x200u;
			Check(SetConsoleMode(input, changed));
			return new Scope(() => Check(SetConsoleMode(input, saved)));
		}
		int size = OperatingSystem.IsMacOS() ? Marshal.SizeOf<DarwinTermios>() : Marshal.SizeOf<LinuxTermios>();
		nint original = Marshal.AllocHGlobal(size), changedState = Marshal.AllocHGlobal(size);
		try {
			Check(tcgetattr(0, original) == 0);
			byte[] bytes = new byte[size]; Marshal.Copy(original, bytes, 0, size); Marshal.Copy(bytes, 0, changedState, size);
			if (raw) cfmakeraw(changedState);
			else if (OperatingSystem.IsMacOS()) {
				DarwinTermios state = Marshal.PtrToStructure<DarwinTermios>(changedState); state.LocalFlags |= 0x180;
				Marshal.StructureToPtr(state, changedState, false); Marshal.WriteByte(changedState, 40, 3);
			} else {
				LinuxTermios state = Marshal.PtrToStructure<LinuxTermios>(changedState); state.LocalFlags |= 3;
				Marshal.StructureToPtr(state, changedState, false); Marshal.WriteByte(changedState, 17, 3);
			}
			Check(tcsetattr(0, 0, changedState) == 0);
			return new Scope(() => { try { Check(tcsetattr(0, 0, original) == 0); } finally { Marshal.FreeHGlobal(original); } });
		} catch { Marshal.FreeHGlobal(original); throw; }
		finally { Marshal.FreeHGlobal(changedState); }
	}
	private sealed class Scope(Action restore) : IDisposable { private Action? restore = restore; public void Dispose() => Interlocked.Exchange(ref restore, null)?.Invoke(); }
	private static void Check(bool success) { if (!success) throw new IOException("Terminal mode operation failed.", new Win32Exception(Marshal.GetLastPInvokeError())); }
	[StructLayout(LayoutKind.Sequential)] private unsafe struct LinuxTermios { public uint InputFlags, OutputFlags, ControlFlags, LocalFlags; public byte Line; public fixed byte ControlCharacters[32]; public uint InputSpeed, OutputSpeed; }
	[StructLayout(LayoutKind.Sequential)] private unsafe struct DarwinTermios { public ulong InputFlags, OutputFlags, ControlFlags, LocalFlags; public fixed byte ControlCharacters[20]; public ulong InputSpeed, OutputSpeed; }
	[DllImport("kernel32.dll")] private static extern nint GetStdHandle(int which);
	[DllImport("kernel32.dll")] private static extern uint GetConsoleCP();
	[DllImport("kernel32.dll")] private static extern uint GetConsoleOutputCP();
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadFile(nint handle, out byte value, uint length, out uint read, nint overlapped);
	[DllImport("libc", EntryPoint = "read", SetLastError = true)] private static extern nint NativeRead(int fd, out byte value, nuint count);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(nint handle, out uint mode);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleMode(nint handle, uint mode);
	[DllImport("libc", SetLastError = true)] private static extern int tcgetattr(int fd, nint state);
	[DllImport("libc", SetLastError = true)] private static extern int tcsetattr(int fd, int action, nint state);
	[DllImport("libc")] private static extern void cfmakeraw(nint state);
}
