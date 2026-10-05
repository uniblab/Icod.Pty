using System.Runtime.InteropServices;

namespace Icod.Pty.Unix;

internal enum UnixTerminalPlatform { Linux, Darwin }

internal sealed class UnixTerminalState {
	internal UnixTerminalPlatform Platform { get; }
	internal ulong InputFlags { get; set; }
	internal ulong OutputFlags { get; set; }
	internal ulong ControlFlags { get; set; }
	internal ulong LocalFlags { get; set; }
	internal byte Line { get; set; }
	internal byte[] ControlCharacters { get; }
	internal ulong InputSpeed { get; set; }
	internal ulong OutputSpeed { get; set; }
	internal UnixTerminalState(UnixTerminalPlatform platform, ulong inputFlags, ulong outputFlags, ulong controlFlags, ulong localFlags, byte line, byte[] controlCharacters, ulong inputSpeed, ulong outputSpeed) {
		Platform = platform; InputFlags = inputFlags; OutputFlags = outputFlags; ControlFlags = controlFlags; LocalFlags = localFlags; Line = line;
		ControlCharacters = (byte[])controlCharacters.Clone(); InputSpeed = inputSpeed; OutputSpeed = outputSpeed;
	}
	internal UnixTerminalState Clone() => new(Platform, InputFlags, OutputFlags, ControlFlags, LocalFlags, Line, ControlCharacters, InputSpeed, OutputSpeed);
}

internal sealed record UnixTerminalConstants(
	int ControlCount, int InterruptIndex, int EndOfFileIndex, int EraseIndex, int MinimumIndex, int TimeoutIndex,
	ulong Echo, ulong EchoNewline, ulong Canonical, ulong SignalProcessing, ulong OutputPostProcessing,
	ulong RawInputClear, ulong RawInputSet, ulong RawControlClear, ulong RawControlSet, ulong RawLocalClear) {
	internal static UnixTerminalConstants For(UnixTerminalPlatform platform) => platform == UnixTerminalPlatform.Darwin
		? new(20, 8, 0, 3, 16, 17, 0x08, 0x10, 0x100, 0x80, 0x01, 0x27fe, 0x01, 0x1300, 0x0b00, 0xa040059e)
		: new(32, 0, 4, 2, 6, 5, 0x08, 0x40, 0x02, 0x01, 0x01, 0x05eb, 0, 0x0130, 0x0030, 0x804b);
	internal ulong RawInput(ulong value) => (value & ~RawInputClear) | RawInputSet;
	internal ulong RawControl(ulong value) => (value & ~RawControlClear) | RawControlSet;
}

internal interface IUnixTerminalOperations {
	int LastError { get; }
	int GetAttributes(int descriptor, out UnixTerminalState state);
	int SetAttributes(int descriptor, UnixTerminalState state);
	int GetDisabledCharacter(int descriptor, out byte value);
	void MakeRaw(UnixTerminalState state);
}

internal sealed class UnixTerminalNative : IUnixTerminalOperations {
	internal static UnixTerminalNative Instance { get; } = new();
	public int LastError { get; private set; }
	private UnixTerminalNative() { }

	public int GetAttributes(int descriptor, out UnixTerminalState state) {
		int result;
		if (OperatingSystem.IsMacOS()) { result = tcgetattr(descriptor, out DarwinTermios value); LastError = result < 0 ? Marshal.GetLastPInvokeError() : 0; state = From(value); }
		else { result = tcgetattr(descriptor, out LinuxTermios value); LastError = result < 0 ? Marshal.GetLastPInvokeError() : 0; state = From(value); }
		return result;
	}
	public int SetAttributes(int descriptor, UnixTerminalState state) {
		int result = state.Platform == UnixTerminalPlatform.Darwin ? tcsetattr(descriptor, 0, ToDarwin(state)) : tcsetattr(descriptor, 0, ToLinux(state));
		LastError = result < 0 ? Marshal.GetLastPInvokeError() : 0; return result;
	}
	public int GetDisabledCharacter(int descriptor, out byte value) {
		long result = fpathconf(descriptor, OperatingSystem.IsMacOS() ? 9 : 8);
		if (result < 0 || result > byte.MaxValue) { LastError = Marshal.GetLastPInvokeError(); value = 0; return -1; }
		LastError = 0; value = (byte)result; return 0;
	}
	public void MakeRaw(UnixTerminalState state) {
		if (state.Platform == UnixTerminalPlatform.Darwin) { DarwinTermios value = ToDarwin(state); cfmakeraw(ref value); Copy(value, state); }
		else { LinuxTermios value = ToLinux(state); cfmakeraw(ref value); Copy(value, state); }
	}

	private static unsafe UnixTerminalState From(LinuxTermios value) {
		byte[] controls = new byte[32]; fixed (byte* source = value.ControlCharacters) Marshal.Copy((nint)source, controls, 0, controls.Length);
		return new(UnixTerminalPlatform.Linux, value.InputFlags, value.OutputFlags, value.ControlFlags, value.LocalFlags, value.Line, controls, value.InputSpeed, value.OutputSpeed);
	}
	private static unsafe UnixTerminalState From(DarwinTermios value) {
		byte[] controls = new byte[20]; fixed (byte* source = value.ControlCharacters) Marshal.Copy((nint)source, controls, 0, controls.Length);
		return new(UnixTerminalPlatform.Darwin, value.InputFlags, value.OutputFlags, value.ControlFlags, value.LocalFlags, 0, controls, value.InputSpeed, value.OutputSpeed);
	}
	private static unsafe LinuxTermios ToLinux(UnixTerminalState state) {
		LinuxTermios value = new() { InputFlags = (uint)state.InputFlags, OutputFlags = (uint)state.OutputFlags, ControlFlags = (uint)state.ControlFlags, LocalFlags = (uint)state.LocalFlags, Line = state.Line, InputSpeed = (uint)state.InputSpeed, OutputSpeed = (uint)state.OutputSpeed };
		fixed (byte* target = value.ControlCharacters) Marshal.Copy(state.ControlCharacters, 0, (nint)target, 32); return value;
	}
	private static unsafe DarwinTermios ToDarwin(UnixTerminalState state) {
		DarwinTermios value = new() { InputFlags = state.InputFlags, OutputFlags = state.OutputFlags, ControlFlags = state.ControlFlags, LocalFlags = state.LocalFlags, InputSpeed = state.InputSpeed, OutputSpeed = state.OutputSpeed };
		fixed (byte* target = value.ControlCharacters) Marshal.Copy(state.ControlCharacters, 0, (nint)target, 20); return value;
	}
	private static void Copy(LinuxTermios value, UnixTerminalState state) { UnixTerminalState changed = From(value); Copy(changed, state); }
	private static void Copy(DarwinTermios value, UnixTerminalState state) { UnixTerminalState changed = From(value); Copy(changed, state); }
	private static void Copy(UnixTerminalState source, UnixTerminalState target) {
		target.InputFlags = source.InputFlags; target.OutputFlags = source.OutputFlags; target.ControlFlags = source.ControlFlags; target.LocalFlags = source.LocalFlags; target.Line = source.Line;
		source.ControlCharacters.CopyTo(target.ControlCharacters, 0); target.InputSpeed = source.InputSpeed; target.OutputSpeed = source.OutputSpeed;
	}

	[StructLayout(LayoutKind.Sequential)] private unsafe struct LinuxTermios {
		internal uint InputFlags, OutputFlags, ControlFlags, LocalFlags;
		internal byte Line;
		internal fixed byte ControlCharacters[32];
		internal uint InputSpeed, OutputSpeed;
	}
	[StructLayout(LayoutKind.Sequential)] private unsafe struct DarwinTermios {
		internal ulong InputFlags, OutputFlags, ControlFlags, LocalFlags;
		internal fixed byte ControlCharacters[20];
		internal ulong InputSpeed, OutputSpeed;
	}
	[DllImport("libc", EntryPoint = "tcgetattr", SetLastError = true)] private static extern int tcgetattr(int descriptor, out LinuxTermios state);
	[DllImport("libc", EntryPoint = "tcgetattr", SetLastError = true)] private static extern int tcgetattr(int descriptor, out DarwinTermios state);
	[DllImport("libc", EntryPoint = "tcsetattr", SetLastError = true)] private static extern int tcsetattr(int descriptor, int action, in LinuxTermios state);
	[DllImport("libc", EntryPoint = "tcsetattr", SetLastError = true)] private static extern int tcsetattr(int descriptor, int action, in DarwinTermios state);
	[DllImport("libc", EntryPoint = "cfmakeraw")] private static extern void cfmakeraw(ref LinuxTermios state);
	[DllImport("libc", EntryPoint = "cfmakeraw")] private static extern void cfmakeraw(ref DarwinTermios state);
	[DllImport("libc", SetLastError = true)] private static extern long fpathconf(int descriptor, int name);
}
