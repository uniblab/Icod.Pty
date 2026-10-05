using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Icod.Pty.Unix;

internal static class TerminalConfigurationProbe {
	internal static int ReportCurrentState() {
		if (OperatingSystem.IsWindows()) return 2;
		WriteDescriptor(1, JsonSerializer.Serialize(TerminalState.Read(0).Report()) + "\n");
		return 0;
	}

	internal static int ReadOnce(int readSize) {
		if (OperatingSystem.IsWindows() || readSize <= 0 || readSize > 4096) return 2;
		WriteDescriptor(1, "INITIAL:" + JsonSerializer.Serialize(TerminalState.Read(0).Report()) + "\nREAD-READY\n");
		byte[] bytes = new byte[readSize];
		int count = ReadDescriptor(0, bytes);
		WriteDescriptor(1, $"READ:{count}:{Convert.ToHexString(bytes.AsSpan(0, count))}\n");
		return 0;
	}

	internal static int ChangeOwnState() {
		if (OperatingSystem.IsWindows()) return 2;
		TerminalState initial = TerminalState.Read(0);
		WriteDescriptor(1, "INITIAL:" + JsonSerializer.Serialize(initial.Report()) + "\n");
		TerminalState changed = initial.Clone(); changed.MakeRaw(); changed.Apply(0);
		WriteDescriptor(1, "CHANGED:" + JsonSerializer.Serialize(TerminalState.Read(0).Report()) + "\n");
		return 0;
	}

	internal static async Task<int> RunAsync() {
		if (OperatingSystem.IsWindows()) {
			Console.WriteLine(JsonSerializer.Serialize(new { Platform = "Windows", NativeTermios = false, HostConsoleMutation = false }));
			return 0;
		}
		UnixNative.WindowSize size = new() { Columns = 80, Rows = 24 };
		byte[] name = new byte[1024];
		UnixNative.Check(UnixNative.openpty(out int master, out int slave, name, 0, ref size), "terminal probe openpty");
		try {
			int flags = UnixNative.fcntl(master, 3, 0); UnixNative.Check(flags, "terminal probe F_GETFL");
			UnixNative.Check(UnixNative.fcntl(master, 4, flags | UnixNative.NonBlocking), "terminal probe F_SETFL");
			TerminalState original = TerminalState.Read(slave);
			TerminalState unchanged = TerminalState.Read(slave);
			TerminalState.ReportModel baselineChild = await SpawnStateChild(master, slave);

			TerminalState configured = original.Clone();
			configured.LocalFlags &= ~(configured.CanonicalMask | configured.EchoMask | configured.EchoNewlineMask);
			configured.SetControl(configured.InterruptIndex, 0x1c);
			configured.SetControl(configured.EndOfFileIndex, 0x05);
			configured.SetControl(configured.EraseIndex, 0x08);
			configured.SetControl(configured.MinimumIndex, 1);
			configured.SetControl(configured.TimeoutIndex, 0);
			configured.Apply(slave);
			TerminalState configuredReadback = TerminalState.Read(slave);
			TerminalState.ReportModel configuredChild = await SpawnStateChild(master, slave);

			TerminalState raw = original.Clone();
			raw.MakeRaw();
			raw.Apply(slave);
			TerminalState rawReadback = TerminalState.Read(slave);
			TerminalState.ReportModel rawChild = await SpawnStateChild(master, slave);

			long disabled = fpathconf(slave, OperatingSystem.IsMacOS() ? 9 : 8);
			Console.WriteLine(JsonSerializer.Serialize(new {
				Platform = OperatingSystem.IsMacOS() ? "Darwin" : "Linux",
				Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
				NativeTermios = true,
				TermiosSize = original.Bytes.Length,
				original.FlagSize,
				original.ControlOffset,
				original.ControlCount,
				original.InputSpeedOffset,
				original.OutputSpeedOffset,
				original.InterruptIndex,
				original.EndOfFileIndex,
				original.EraseIndex,
				original.MinimumIndex,
				original.TimeoutIndex,
				DisabledCharacter = disabled,
				PreserveUnchanged = original.SemanticallyEquals(unchanged),
				BaselineChildMatches = original.Matches(baselineChild),
				ConfiguredReadbackMatches = configured.Matches(configuredReadback.Report()),
				ConfiguredChildMatches = configuredReadback.Matches(configuredChild),
				Configured = configuredReadback.Report(),
				RawTransformationMatches = original.HasExpectedRawTransformation(raw),
				RawReadbackMatches = raw.Matches(rawReadback.Report()),
				RawChildMatches = rawReadback.Matches(rawChild),
				Raw = rawReadback.Report()
			}));
			return 0;
		} finally { UnixNative.close(master); UnixNative.close(slave); }
	}

	private static async Task<TerminalState.ReportModel> SpawnStateChild(int master, int slave) {
		nint actions = Marshal.AllocHGlobal(128); bool initialized = false; int pid = 0;
		try {
			Check(posix_spawn_file_actions_init(actions), "terminal probe actions init"); initialized = true;
			Check(posix_spawn_file_actions_adddup2(actions, slave, 0), "terminal probe stdin");
			Check(posix_spawn_file_actions_adddup2(actions, slave, 1), "terminal probe stdout");
			Check(posix_spawn_file_actions_adddup2(actions, slave, 2), "terminal probe stderr");
			Check(posix_spawn_file_actions_addclose(actions, master), "terminal probe close master");
			if (slave > 2) Check(posix_spawn_file_actions_addclose(actions, slave), "terminal probe close slave");
			string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
			using Vector arguments = new([dotnet, "exec", typeof(TerminalConfigurationProbe).Assembly.Location, "terminal-config-state"]);
			using Vector environment = new(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Select(pair => (string)pair.Key + "=" + (string)pair.Value!));
			Check(posix_spawn(out pid, dotnet, actions, 0, arguments.Pointer, environment.Pointer), "terminal probe spawn");
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
			using MemoryStream output = new();
			int status = 0;
			while (true) {
				Drain(master, output);
				int waited = waitpid(pid, out status, 1);
				if (waited == pid) break;
				if (waited < 0) throw Error("terminal probe waitpid");
				await Task.Delay(10, timeout.Token);
			}
			pid = 0;
			for (int i = 0; i < 5; i++) { Drain(master, output); await Task.Delay(5, timeout.Token); }
			if ((status & 0x7f) != 0 || ((status >> 8) & 0xff) != 0) throw new IOException($"Terminal state child failed with wait status {status}.");
			string text = Encoding.UTF8.GetString(output.ToArray()).Replace("\r", "", StringComparison.Ordinal).Trim();
			return JsonSerializer.Deserialize<TerminalState.ReportModel>(text) ?? throw new IOException("Terminal state child returned no report.");
		} finally {
			if (pid > 0) { kill(pid, 9); waitpid(pid, out _, 0); }
			if (initialized) posix_spawn_file_actions_destroy(actions);
			Marshal.FreeHGlobal(actions);
		}
	}

	private static unsafe void Drain(int descriptor, MemoryStream output) {
		byte[] buffer = new byte[4096];
		while (true) {
			nint count;
			fixed (byte* bytes = buffer) count = UnixNative.read(descriptor, bytes, (nuint)buffer.Length);
			if (count > 0) { output.Write(buffer, 0, checked((int)count)); continue; }
			if (count == 0) return;
			int error = Marshal.GetLastPInvokeError();
			if (error is 11 or 35) return;
			if (error == 4) continue;
			throw Error("terminal probe read", error);
		}
	}

	private static unsafe void WriteDescriptor(int descriptor, string value) {
		byte[] bytes = Encoding.UTF8.GetBytes(value); int offset = 0;
		while (offset < bytes.Length) {
			nint count;
			fixed (byte* pointer = &bytes[offset]) count = UnixNative.write(descriptor, pointer, (nuint)(bytes.Length - offset));
			if (count > 0) { offset += checked((int)count); continue; }
			if (Marshal.GetLastPInvokeError() == 4) continue;
			throw Error("terminal state write");
		}
	}
	private static unsafe int ReadDescriptor(int descriptor, byte[] bytes) {
		while (true) {
			nint count;
			fixed (byte* pointer = bytes) count = UnixNative.read(descriptor, pointer, (nuint)bytes.Length);
			if (count >= 0) return checked((int)count);
			if (Marshal.GetLastPInvokeError() == 4) continue;
			throw Error("terminal behavior read");
		}
	}

	private static void Check(int error, string operation) { if (error != 0) throw Error(operation, error); }
	private static IOException Error(string operation, int? error = null) => UnixNative.Error(operation, error);

	private sealed class Vector : IDisposable {
		private readonly List<nint> strings = [];
		internal nint Pointer { get; }
		internal Vector(IEnumerable<string> values) {
			string[] entries = values.ToArray(); Pointer = Marshal.AllocHGlobal((entries.Length + 1) * nint.Size);
			try {
				for (int i = 0; i < entries.Length; i++) { nint item = Marshal.StringToCoTaskMemUTF8(entries[i]); strings.Add(item); Marshal.WriteIntPtr(Pointer, i * nint.Size, item); }
				Marshal.WriteIntPtr(Pointer, entries.Length * nint.Size, 0);
			} catch { Dispose(); throw; }
		}
		public void Dispose() { foreach (nint item in strings) Marshal.FreeCoTaskMem(item); Marshal.FreeHGlobal(Pointer); }
	}

	private sealed class TerminalState {
		internal sealed record ReportModel(ulong InputFlags, ulong OutputFlags, ulong ControlFlags, ulong LocalFlags, byte[] ControlCharacters, ulong InputSpeed, ulong OutputSpeed, bool Echo, bool CanonicalInput, bool SignalProcessing, byte InterruptCharacter, byte EndOfFileCharacter, byte EraseCharacter, byte MinimumReadBytes, byte ReadTimeoutDeciseconds);
		internal byte[] Bytes { get; }
		internal int FlagSize => OperatingSystem.IsMacOS() ? 8 : 4;
		internal int ControlOffset => OperatingSystem.IsMacOS() ? 32 : 17;
		internal int ControlCount => OperatingSystem.IsMacOS() ? 20 : 32;
		internal int InputSpeedOffset => OperatingSystem.IsMacOS() ? 56 : 52;
		internal int OutputSpeedOffset => OperatingSystem.IsMacOS() ? 64 : 56;
		internal int InterruptIndex => OperatingSystem.IsMacOS() ? 8 : 0;
		internal int EndOfFileIndex => OperatingSystem.IsMacOS() ? 0 : 4;
		internal int EraseIndex => OperatingSystem.IsMacOS() ? 3 : 2;
		internal int MinimumIndex => OperatingSystem.IsMacOS() ? 16 : 6;
		internal int TimeoutIndex => OperatingSystem.IsMacOS() ? 17 : 5;
		internal ulong EchoMask => 0x08;
		internal ulong EchoNewlineMask => OperatingSystem.IsMacOS() ? 0x10u : 0x40u;
		internal ulong CanonicalMask => OperatingSystem.IsMacOS() ? 0x100u : 0x02u;
		internal ulong SignalMask => OperatingSystem.IsMacOS() ? 0x80u : 0x01u;
		internal ulong InputFlags { get => ReadFlag(0); set => WriteFlag(0, value); }
		internal ulong OutputFlags { get => ReadFlag(FlagSize); set => WriteFlag(FlagSize, value); }
		internal ulong ControlFlags { get => ReadFlag(FlagSize * 2); set => WriteFlag(FlagSize * 2, value); }
		internal ulong LocalFlags { get => ReadFlag(FlagSize * 3); set => WriteFlag(FlagSize * 3, value); }

		private TerminalState(byte[] bytes) { Bytes = bytes; }
		internal static TerminalState Read(int descriptor) {
			byte[] bytes = new byte[OperatingSystem.IsMacOS() ? 72 : 60];
			if (tcgetattr(descriptor, bytes) != 0) throw Error("terminal probe tcgetattr");
			return new(bytes);
		}
		internal TerminalState Clone() => new((byte[])Bytes.Clone());
		internal void Apply(int descriptor) { if (tcsetattr(descriptor, 0, Bytes) != 0) throw Error("terminal probe tcsetattr"); }
		internal void MakeRaw() => cfmakeraw(Bytes);
		internal void SetControl(int index, byte value) => Bytes[ControlOffset + index] = value;
		internal byte GetControl(int index) => Bytes[ControlOffset + index];
		internal ReportModel Report() {
			ulong local = OperatingSystem.IsMacOS() ? LocalFlags & ~0x20000000UL : LocalFlags;
			return new(InputFlags, OutputFlags, ControlFlags, local, Bytes.AsSpan(ControlOffset, ControlCount).ToArray(), ReadFlag(InputSpeedOffset), ReadFlag(OutputSpeedOffset), (local & EchoMask) != 0, (local & CanonicalMask) != 0, (local & SignalMask) != 0, GetControl(InterruptIndex), GetControl(EndOfFileIndex), GetControl(EraseIndex), GetControl(MinimumIndex), GetControl(TimeoutIndex));
		}
		internal bool Matches(ReportModel other) {
			ReportModel current = Report();
			return current.InputFlags == other.InputFlags && current.OutputFlags == other.OutputFlags && current.ControlFlags == other.ControlFlags && current.LocalFlags == other.LocalFlags && current.ControlCharacters.SequenceEqual(other.ControlCharacters) && current.InputSpeed == other.InputSpeed && current.OutputSpeed == other.OutputSpeed && current.Echo == other.Echo && current.CanonicalInput == other.CanonicalInput && current.SignalProcessing == other.SignalProcessing && current.InterruptCharacter == other.InterruptCharacter && current.EndOfFileCharacter == other.EndOfFileCharacter && current.EraseCharacter == other.EraseCharacter && current.MinimumReadBytes == other.MinimumReadBytes && current.ReadTimeoutDeciseconds == other.ReadTimeoutDeciseconds;
		}
		internal bool SemanticallyEquals(TerminalState other) => Matches(other.Report());
		internal bool HasExpectedRawTransformation(TerminalState raw) {
			ulong expectedInput, expectedControl, expectedLocal;
			if (OperatingSystem.IsMacOS()) {
				expectedInput = (InputFlags & ~0x27feUL) | 0x01UL;
				expectedControl = (ControlFlags & ~0x1300UL) | 0x0b00UL;
				expectedLocal = LocalFlags & ~0xa040059eUL;
			} else {
				expectedInput = InputFlags & ~0x05ebUL;
				expectedControl = (ControlFlags & ~0x0130UL) | 0x0030UL;
				expectedLocal = LocalFlags & ~0x804bUL;
			}
			return raw.InputFlags == expectedInput && raw.OutputFlags == (OutputFlags & ~1UL) && raw.ControlFlags == expectedControl && raw.LocalFlags == expectedLocal && raw.GetControl(raw.MinimumIndex) == 1 && raw.GetControl(raw.TimeoutIndex) == 0;
		}
		private ulong ReadFlag(int offset) => FlagSize == 8 ? BitConverter.ToUInt64(Bytes, offset) : BitConverter.ToUInt32(Bytes, offset);
		private void WriteFlag(int offset, ulong value) => (FlagSize == 8 ? BitConverter.GetBytes(value) : BitConverter.GetBytes(checked((uint)value))).CopyTo(Bytes, offset);
	}

	[DllImport("libc", SetLastError = true)] private static extern int tcgetattr(int descriptor, [Out] byte[] state);
	[DllImport("libc", SetLastError = true)] private static extern int tcsetattr(int descriptor, int action, [In] byte[] state);
	[DllImport("libc")] private static extern void cfmakeraw([In, Out] byte[] state);
	[DllImport("libc", SetLastError = true)] private static extern long fpathconf(int descriptor, int name);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_init(nint actions);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_destroy(nint actions);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_adddup2(nint actions, int from, int to);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_addclose(nint actions, int descriptor);
	[DllImport("libc", SetLastError = true)] private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint actions, nint attributes, nint arguments, nint environment);
	[DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int options);
	[DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
}
