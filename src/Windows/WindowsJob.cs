using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Windows;

internal sealed class WindowsJob : IDisposable {
	private readonly object gate = new();
	internal SafeJobHandle Handle { get; }
	internal WindowsJob() {
		Handle = WindowsNative.CreateJobObjectW(0, null);
		try {
			if (Handle.IsInvalid) throw Error("CreateJobObject");
			WindowsNative.ExtendedLimitInformation limits = new() { Basic = new() { LimitFlags = 0x2000 } };
			if (!WindowsNative.SetInformationJobObject(Handle, 9, ref limits, (uint)Marshal.SizeOf<WindowsNative.ExtendedLimitInformation>())) throw Error("SetInformationJobObject");
		} catch { Handle.Dispose(); throw; }
	}
	internal static WindowsJob CreateAssigned(SafeProcessHandle process) {
		WindowsJob job = new();
		try { job.Assign(process); return job; } catch { job.Dispose(); throw; }
	}
	internal void Assign(SafeProcessHandle process) {
		lock (gate) {
			ObjectDisposedException.ThrowIf(Handle.IsClosed, this);
			if (!WindowsNative.AssignProcessToJobObject(Handle, process)) throw Error("AssignProcessToJobObject");
		}
	}
	internal PtyControlResult RequestTermination() {
		lock (gate) {
			ObjectDisposedException.ThrowIf(Handle.IsClosed, this);
			if (!WindowsNative.TerminateJobObject(Handle, 1)) throw Error("TerminateJobObject (OwnedScope)");
			return new(PtyProcessTarget.OwnedScope, PtyControlStatus.Requested);
		}
	}
	public void Dispose() { lock (gate) Handle.Dispose(); }
	private static IOException Error(string operation) {
		int code = Marshal.GetLastPInvokeError();
		return new IOException($"{operation} failed (Win32 error {code}).", new Win32Exception(code));
	}
}

internal sealed class SafeJobHandle : SafeHandle {
	public SafeJobHandle() : base(0, true) { }
	public override bool IsInvalid => handle == 0 || handle == -1;
	protected override bool ReleaseHandle() => WindowsNative.CloseHandle(handle);
}
