using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Icod.Pty.Unix;
using Microsoft.Win32.SafeHandles;

internal static class UnixHelperSpawnProbe {
	internal static async Task<int> RunAsync() {
		string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
		string helper = Path.Combine(AppContext.BaseDirectory, "Icod.Pty.Host", "Icod.Pty.Host.dll");
		Dictionary<string, string> environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
			.ToDictionary(p => (string)p.Key, p => (string)p.Value!);
		int[] input = [-1, -1], status = [-1, -1], error = [-1, -1];
		int master = -1, slave = -1, pid = 0;
		nint actions = Marshal.AllocHGlobal(128); bool initialized = false;
		try {
			UnixNative.WindowSize size = new() { Columns = 80, Rows = 24 }; byte[] name = new byte[1024];
			UnixNative.Check(UnixNative.openpty(out master, out slave, name, 0, ref size), "probe openpty");
			UnixNative.Check(pipe(input), "probe pipe input"); UnixNative.Check(pipe(status), "probe pipe status"); UnixNative.Check(pipe(error), "probe pipe error");
			int[] descriptors = [master, slave, .. input, .. status, .. error];
			foreach (int fd in descriptors) UnixNative.Check(UnixNative.fcntl(fd, 2, 1), "probe CLOEXEC");
			Check(posix_spawn_file_actions_init(actions), "actions init"); initialized = true;
			Check(posix_spawn_file_actions_adddup2(actions, input[0], 0), "redirect stdin");
			Check(posix_spawn_file_actions_adddup2(actions, status[1], 1), "redirect stdout");
			Check(posix_spawn_file_actions_adddup2(actions, error[1], 2), "redirect stderr");
			foreach (int fd in descriptors) Check(posix_spawn_file_actions_addclose(actions, fd), "close inherited descriptor");
			using Vector argv = new([dotnet, "exec", helper]);
			using Vector envp = new(environment.Select(p => p.Key + "=" + p.Value));
			Check(posix_spawn(out pid, dotnet, actions, 0, argv.Pointer, envp.Pointer), "spawn helper");
			Close(ref input[0]); Close(ref status[1]); Close(ref error[1]);
			using FileStream config = Take(ref input[1], FileAccess.Write);
			using StreamReader output = new(Take(ref status[0], FileAccess.Read));
			using StreamReader diagnostics = new(Take(ref error[0], FileAccess.Read));
			Task<string> handshake = output.ReadToEndAsync(), diagnostic = diagnostics.ReadToEndAsync();
			byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new {
				FileName = dotnet, Arguments = new[] { typeof(UnixHelperSpawnProbe).Assembly.Location, "exit" },
				WorkingDirectory = Environment.CurrentDirectory, Environment = environment,
				SlaveName = Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0)), Columns = 80, Rows = 24
			});
			await config.WriteAsync(payload); config.Dispose();
			string ready = await handshake.WaitAsync(TimeSpan.FromSeconds(10));
			string failure = await diagnostic.WaitAsync(TimeSpan.FromSeconds(10));
			if (ready != "1" || failure.Length != 0) throw new IOException("Helper handshake: " + ready + failure);
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
			byte[] info = new byte[128];
			int offset = OperatingSystem.IsMacOS() ? 12 : 16;
			while (true) {
				Array.Clear(info);
				UnixNative.Check(waitid(1, pid, info, OperatingSystem.IsMacOS() ? 0x25 : 0x01000005), "helper waitid");
				if (BitConverter.ToInt32(info, offset) == pid) break;
				await Task.Delay(10, timeout.Token);
			}
			int code = BitConverter.ToInt32(info, OperatingSystem.IsMacOS() ? 20 : 24);
			Console.WriteLine(JsonSerializer.Serialize(new { Handshake = ready, ExitCode = code, InitialGroupRetained = getpgid(pid) == pid }));
			return 0;
		} finally {
			foreach (int fd in input.Concat(status).Concat(error)) if (fd >= 0) UnixNative.close(fd);
			if (master >= 0) UnixNative.close(master); if (slave >= 0) UnixNative.close(slave);
			if (pid > 1) { kill(pid, 9); waitpid(pid, out _, 0); }
			if (initialized) posix_spawn_file_actions_destroy(actions); Marshal.FreeHGlobal(actions);
		}
	}
	private static void Close(ref int fd) { if (fd >= 0) UnixNative.close(fd); fd = -1; }
	private static FileStream Take(ref int fd, FileAccess access) {
		SafeFileHandle handle = new(fd, true); fd = -1;
		try { return new FileStream(handle, access); } catch { handle.Dispose(); throw; }
	}
	private static void Check(int error, string operation) { if (error != 0) throw new IOException(operation + ": " + error); }
	private sealed class Vector : IDisposable {
		private readonly List<nint> strings = [];
		internal nint Pointer { get; }
		internal Vector(IEnumerable<string> values) {
			string[] entries = values.ToArray(); Pointer = Marshal.AllocHGlobal((entries.Length + 1) * nint.Size);
			try {
				for (int i = 0; i < entries.Length; i++) { nint text = Marshal.StringToCoTaskMemUTF8(entries[i]); strings.Add(text); Marshal.WriteIntPtr(Pointer, i * nint.Size, text); }
				Marshal.WriteIntPtr(Pointer, entries.Length * nint.Size, 0);
			} catch { Dispose(); throw; }
		}
		public void Dispose() { foreach (nint text in strings) Marshal.FreeCoTaskMem(text); Marshal.FreeHGlobal(Pointer); }
	}
	[DllImport("libc", SetLastError = true)] private static extern int pipe([Out] int[] descriptors);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_init(nint actions);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_destroy(nint actions);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_adddup2(nint actions, int from, int to);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_addclose(nint actions, int fd);
	[DllImport("libc")] private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint actions, nint attributes, nint argv, nint environment);
	[DllImport("libc", SetLastError = true)] private static extern int waitid(int type, int pid, [Out] byte[] info, int flags);
	[DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int flags);
	[DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
	[DllImport("libc", SetLastError = true)] private static extern int getpgid(int pid);
}
