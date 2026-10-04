using System.Runtime.InteropServices;
using Icod.Pty.Unix;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class UnixLifetimeFaultTests {
	[Fact]
	public async Task Denied_primary_termination_reports_failure_and_eventually_reaps() {
		if (OperatingSystem.IsWindows()) return;
		int id = Spawn("sleep 0.5; exit 37");
		UnixChildLifetime child = new(id, (_, _) => { Marshal.SetLastPInvokeError(1); return -1; });
		Assert.Throws<IOException>(() => child.Dispose()); child.Dispose();
		Assert.Equal(37, await child.Exit.WaitAsync(TimeSpan.FromSeconds(10)));
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		while (waitid(1, id, new byte[128], OperatingSystem.IsMacOS() ? 0x25 : 0x01000005) == 0) await Task.Delay(10, timeout.Token);
		Assert.Equal(10, Marshal.GetLastPInvokeError());
	}
	[Fact]
	public async Task Competing_reaper_disables_all_cached_targeting() {
		if (OperatingSystem.IsWindows()) return;
		int calls = 0; UnixChildLifetime child = new(Spawn("exit 37"), (pid, signal) => { calls++; return kill(pid, signal); });
		Assert.Equal(37, await child.Exit.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal(child.ProcessId, waitpid(child.ProcessId, out _, 0));
		Assert.Throws<IOException>(() => child.RequestTermination(PtyProcessTarget.OwnedScope));
		Assert.Throws<IOException>(() => child.Dispose()); child.Dispose();
		Assert.Equal(0, calls);
	}
	[Fact]
	public void Permission_error_is_not_success_and_targets_are_never_broadcast() {
		if (OperatingSystem.IsWindows()) return;
		bool fail = true; int childId = Spawn("exec sleep 20");
		using UnixChildLifetime child = new(childId, (pid, signal) => {
			Assert.True(pid == childId || pid == -childId); Assert.True(Math.Abs(pid) > 1);
			if (fail) { fail = false; Marshal.SetLastPInvokeError(1); return -1; }
			return kill(pid, signal);
		});
		IOException error = Assert.Throws<IOException>(() => child.RequestTermination(PtyProcessTarget.OwnedScope));
		Assert.Contains("OwnedScope", error.Message); Assert.Contains("errno 1", error.Message);
	}
	[Fact]
	public void Failed_group_cleanup_still_reaps_anchor_and_repeat_dispose_is_harmless() {
		if (OperatingSystem.IsWindows()) return;
		int id = Spawn("exec sleep 20");
		UnixChildLifetime child = new(id, (pid, signal) => { if (pid < 0) { Marshal.SetLastPInvokeError(1); return -1; } return kill(pid, signal); });
		Assert.Throws<IOException>(() => child.Dispose()); child.Dispose();
		Assert.Equal(-1, waitpid(id, out _, 1)); Assert.Equal(10, Marshal.GetLastPInvokeError());
	}
	private static int Spawn(string command) {
		string[] values = ["/bin/sh", "-c", command]; List<nint> strings = [];
		nint args = Marshal.AllocHGlobal(4 * nint.Size), env = Marshal.AllocHGlobal(nint.Size); Marshal.WriteIntPtr(env, 0);
		try {
			for (int i = 0; i < values.Length; i++) { nint value = Marshal.StringToCoTaskMemUTF8(values[i]); strings.Add(value); Marshal.WriteIntPtr(args, i * nint.Size, value); }
			Marshal.WriteIntPtr(args, 3 * nint.Size, 0); int error = posix_spawn(out int pid, "/bin/sh", 0, 0, args, env);
			if (error != 0) throw new IOException("test spawn errno " + error); return pid;
		} finally { foreach (nint value in strings) Marshal.FreeCoTaskMem(value); Marshal.FreeHGlobal(args); Marshal.FreeHGlobal(env); }
	}
	[DllImport("libc")] private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint actions, nint attributes, nint args, nint env);
	[DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
	[DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, out int status, int flags);
	[DllImport("libc", SetLastError = true)] private static extern int waitid(int type, int pid, [Out] byte[] info, int flags);
}
