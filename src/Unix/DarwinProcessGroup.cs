using System.Runtime.InteropServices;

namespace Icod.Pty.Unix;

internal static class DarwinProcessGroup {
	// This inventory only disambiguates XNU's EPERM for zombie-only groups. It
	// never supplies control targets; only the anchored group is ever signaled.
	internal static bool IsWithoutLiveMembers(int group) {
		int capacity = 64;
		for (int attempt = 0; attempt < 8; attempt++, capacity *= 2) {
			int[] pids = new int[capacity];
			int bytes = proc_listpids(2, (uint)group, pids, checked(capacity * sizeof(int)));
			if (bytes <= 0 || bytes % sizeof(int) != 0) return false;
			if (bytes == capacity * sizeof(int)) continue; // A truncated inventory proves nothing.
			bool anchor = false;
			foreach (int pid in pids.AsSpan(0, bytes / sizeof(int))) {
				if (pid == group) anchor = true;
				byte[] info = new byte[64]; // proc_bsdshortinfo: fixed uint32 fields plus 16-byte name.
				int size = proc_pidinfo(pid, 13, 1, info, info.Length); // arg=1 includes zombies.
				if (size == 0 && Marshal.GetLastPInvokeError() == 3) continue; // Member has gone.
				if (size != info.Length) return false; // Permission/ABI failure is not emptiness.
				if (BitConverter.ToInt32(info, 8) == group && BitConverter.ToInt32(info, 12) != 5) return false;
			}
			return anchor;
		}
		return false;
	}
	[DllImport("libproc", SetLastError = true)] private static extern int proc_listpids(uint type, uint info, [Out] int[] buffer, int size);
	[DllImport("libproc", SetLastError = true)] private static extern int proc_pidinfo(int pid, int flavor, ulong arg, [Out] byte[] buffer, int size);
}
