using System.Collections;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Unix;

internal static class UnixSpawn {
	internal static async Task<UnixChildLifetime> StartAsync(LaunchConfiguration launch, CancellationToken token) {
		UnixChildLifetime.ValidateHost(); token.ThrowIfCancellationRequested();
		using Pipe input = new(false); using Pipe status = new(true); using Pipe error = new(true);
		// glibc actions/attributes are 80/336 bytes on supported 64-bit ABIs;
		// Darwin uses opaque pointer slots. Native init/destroy owns the contents.
		nint actions = Marshal.AllocHGlobal(128), attributes = Marshal.AllocHGlobal(512);
		bool actionsReady = false, attributesReady = false;
		UnixChildLifetime? child = null;
		try {
			Check(posix_spawn_file_actions_init(actions), "spawn actions init"); actionsReady = true;
			Check(posix_spawnattr_init(attributes), "spawn attributes init"); attributesReady = true;
			Check(posix_spawn_file_actions_adddup2(actions, input.Child, 0), "spawn stdin");
			Check(posix_spawn_file_actions_adddup2(actions, status.Child, 1), "spawn stdout");
			Check(posix_spawn_file_actions_adddup2(actions, error.Child, 2), "spawn stderr");
			if (OperatingSystem.IsMacOS()) Check(posix_spawnattr_setflags(attributes, 0x4000), "spawn CLOEXEC_DEFAULT");
			else {
				try { Check(posix_spawn_file_actions_addclosefrom_np(actions, 3), "spawn closefrom"); }
				catch (EntryPointNotFoundException failure) { throw new PlatformNotSupportedException("PlatformScope on Linux requires glibc 2.34 or later for safe descriptor inheritance.", failure); }
			}
			string dotnet = UnixBackend.FindDotNet(launch.DotNetHostPath);
			using Vector argv = new([dotnet, "exec", UnixBackend.FindHelper()]);
			using Vector env = new(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Select(p => p.Key + "=" + p.Value));
			token.ThrowIfCancellationRequested();
			Check(posix_spawnp(out int pid, dotnet, actions, attributes, argv.Pointer, env.Pointer), "posix_spawnp PTY helper");
			child = new UnixChildLifetime(pid);
			input.CloseChild(); status.CloseChild(); error.CloseChild();
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(launch.StartTimeout);
			try { await Handshake(input, status, error, launch, timeout.Token).ConfigureAwait(false); }
			catch (OperationCanceledException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
			catch (OperationCanceledException) when (timeout.IsCancellationRequested) { throw new TimeoutException("The Unix PTY helper did not complete startup within StartTimeout."); }
			return child;
		} catch (Exception failure) {
			CleanupActions.AfterFailure(failure, () => child?.Dispose()); throw;
		} finally {
			if (actionsReady) posix_spawn_file_actions_destroy(actions);
			if (attributesReady) posix_spawnattr_destroy(attributes);
			Marshal.FreeHGlobal(actions); Marshal.FreeHGlobal(attributes);
		}
	}
	private static async Task Handshake(Pipe input, Pipe status, Pipe error, LaunchConfiguration launch, CancellationToken token) {
		using CancellationTokenSource io = CancellationTokenSource.CreateLinkedTokenSource(token);
		using StreamReader statusReader = new(status.Stream, leaveOpen: true), errorReader = new(error.Stream, leaveOpen: true);
		Task<string> ready = statusReader.ReadToEndAsync(io.Token), diagnostic = errorReader.ReadToEndAsync(io.Token);
		try {
			await input.Stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(launch), io.Token).ConfigureAwait(false); input.CloseParent();
			string result = await ready.ConfigureAwait(false), message = await diagnostic.ConfigureAwait(false);
			if (result != "1" || message.Length != 0) throw new IOException("Unix PTY launch failed: " + result + message);
		} catch {
			io.Cancel(); try { await Task.WhenAll(ready, diagnostic).ConfigureAwait(false); } catch { }
			throw;
		}
	}
	private sealed class Pipe : IDisposable {
		private SafeFileHandle? parent, child;
		internal int Child => (int)child!.DangerousGetHandle();
		internal UnixPtyStream Stream { get; }
		internal Pipe(bool read) {
			int[] fds = [-1, -1]; UnixNative.Check(pipe(fds), "pipe helper");
			try {
				for (int i = 0; i < 2; i++) {
					if (fds[i] < 3) { int duplicate = UnixNative.fcntl(fds[i], 0, 3); UnixNative.Check(duplicate, "duplicate helper pipe"); UnixNative.close(fds[i]); fds[i] = duplicate; }
					UnixNative.Check(UnixNative.fcntl(fds[i], 2, 1), "helper pipe CLOEXEC");
				}
				int fd = fds[read ? 0 : 1];
				int flags = UnixNative.fcntl(fd, 3, 0); UnixNative.Check(flags, "helper pipe flags");
				UnixNative.Check(UnixNative.fcntl(fd, 4, flags | UnixNative.NonBlocking), "helper pipe nonblocking");
				parent = new(fd, true); child = new(fds[read ? 1 : 0], true); Stream = new(parent, read);
			} catch { foreach (int fd in fds) UnixNative.close(fd); throw; }
		}
		internal void CloseChild() { child?.Dispose(); child = null; }
		internal void CloseParent() { Stream.Dispose(); parent?.Dispose(); parent = null; }
		public void Dispose() { CloseChild(); CloseParent(); }
	}
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
	private static void Check(int error, string operation) { if (error != 0) throw UnixNative.Error(operation, error); }
	[DllImport("libc", SetLastError = true)] private static extern int pipe([Out] int[] fds);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_init(nint actions);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_destroy(nint actions);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_adddup2(nint actions, int from, int to);
	[DllImport("libc")] private static extern int posix_spawn_file_actions_addclosefrom_np(nint actions, int from);
	[DllImport("libc")] private static extern int posix_spawnattr_init(nint attributes);
	[DllImport("libc")] private static extern int posix_spawnattr_destroy(nint attributes);
	[DllImport("libc")] private static extern int posix_spawnattr_setflags(nint attributes, short flags);
	[DllImport("libc")] private static extern int posix_spawnp(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint actions, nint attributes, nint argv, nint environment);
}
