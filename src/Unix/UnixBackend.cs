using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Unix;

internal sealed class UnixBackend : IPtyBackend {
	private readonly SafeFileHandle master;
	private readonly Process? process;
	private readonly UnixChildLifetime? child;
	private readonly object gate = new();
	private bool disposed;
	public Stream Input { get; }
	public Stream Output { get; }
	public int ProcessId { get; }
	public PtyProcessOwnership Ownership => child == null ? PtyProcessOwnership.PrimaryProcess : PtyProcessOwnership.PlatformScope;
	public PtyProcessCapabilities Capabilities => child == null ? PtyProcessCapabilities.None : PtyProcessCapabilities.TerminateOwnedScope | PtyProcessCapabilities.SignalPrimaryProcess | PtyProcessCapabilities.SignalOwnedScope;
	public PtyControlResult RequestTermination(PtyProcessTarget target) {
		lock (gate) {
			ObjectDisposedException.ThrowIf(disposed, this);
			if (child != null) return child.RequestTermination(target);
			if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
			if (target == PtyProcessTarget.OwnedScope) throw new InvalidOperationException("OwnedScope requires platform-scope ownership.");
			if (process!.HasExited) return new(target, PtyControlStatus.TargetUnavailable);
			Terminate(); return new(target, PtyControlStatus.Requested);
		}
	}
	public PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target) {
		lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return (child ?? throw new InvalidOperationException("Native signals require platform-scope ownership.")).SendSignal(signal, target); }
	}
	public Task<int> Exit { get; }
	private UnixBackend(SafeFileHandle master, Process? process, SafeFileHandle? retainedSlave, UnixChildLifetime? child = null) {
		this.master = master; this.process = process; this.child = child; ProcessId = child?.ProcessId ?? process!.Id;
		Exit = child?.Exit ?? ObserveExit(process!);
		Input = new UnixPtyStream(master, false);
		Stream output = new UnixPtyStream(master, true, retainedSlave, () => Exit.IsCompletedSuccessfully);
		Output = retainedSlave == null ? output : new BufferedPtyOutputStream(output);
	}
	internal static async Task<IPtyBackend> StartAsync(LaunchConfiguration launch, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		UnixNative.WindowSize size = new() { Columns = (ushort)launch.Columns, Rows = (ushort)launch.Rows };
		byte[] name = new byte[1024];
		UnixNative.Check(UnixNative.openpty(out int masterFd, out int slaveFd, name, 0, ref size), "openpty");
		SafeFileHandle master = new(masterFd, true), slave = new(slaveFd, true);
		Process? process = null;
		UnixChildLifetime? child = null;
		try {
			UnixNative.Check(UnixNative.fcntl(masterFd, 2, 1), "FD_CLOEXEC master");
			UnixNative.Check(UnixNative.fcntl(slaveFd, 2, 1), "FD_CLOEXEC slave");
			int flags = UnixNative.fcntl(masterFd, 3, 0); UnixNative.Check(flags, "F_GETFL");
			UnixNative.Check(UnixNative.fcntl(masterFd, 4, flags | UnixNative.NonBlocking), "F_SETFL");
			launch.SlaveName = Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0));
			if (launch.Ownership == PtyProcessOwnership.PlatformScope) {
				child = await UnixSpawn.StartAsync(launch, cancellationToken).ConfigureAwait(false);
				UnixBackend owned = new(master, null, OperatingSystem.IsMacOS() ? slave : null, child);
				if (OperatingSystem.IsMacOS()) slave = null!;
				return owned;
			}
			string helper = FindHelper();
			ProcessStartInfo start = new(FindDotNet(launch.DotNetHostPath)) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
			start.ArgumentList.Add("exec"); start.ArgumentList.Add(helper);
			cancellationToken.ThrowIfCancellationRequested();
			process = Process.Start(start) ?? throw new IOException("Unable to start the Unix PTY helper.");
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(launch.StartTimeout);
			try { await Handshake(process, launch, timeout.Token).ConfigureAwait(false); }
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
			catch (OperationCanceledException) when (timeout.IsCancellationRequested) { throw new TimeoutException("The Unix PTY helper did not complete startup within StartTimeout."); }
			// Darwin flushes unread output on the last slave close. Keep our slave reference
			// until the output reader has consumed pending bytes after primary-child exit.
			UnixBackend backend = new(master, process, OperatingSystem.IsMacOS() ? slave : null);
			if (OperatingSystem.IsMacOS()) slave = null!; // Output now owns this reference.
			return backend;
		} catch {
			master.Dispose();
			child?.Dispose();
			if (process != null) {
				try {
					try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) when (process.HasExited) { }
					await process.WaitForExitAsync().ConfigureAwait(false);
				} finally { process.Dispose(); }
			}
			throw;
		} finally { slave?.Dispose(); }
	}
	private static async Task Handshake(Process process, LaunchConfiguration launch, CancellationToken token) {
		using CancellationTokenSource io = CancellationTokenSource.CreateLinkedTokenSource(token);
		Task<string> diagnostic = process.StandardError.ReadToEndAsync(io.Token);
		Task<string> status = process.StandardOutput.ReadToEndAsync(io.Token);
		try {
			await process.StandardInput.WriteAsync(JsonSerializer.Serialize(launch).AsMemory(), io.Token).ConfigureAwait(false);
			process.StandardInput.Close();
			string result = await status.ConfigureAwait(false);
			string stderr = await diagnostic.ConfigureAwait(false);
			// A ready byte followed by close-on-exec EOF distinguishes startup from early host failure.
			if (result != "1" || stderr.Length != 0) throw new IOException("Unix PTY launch failed: " + result + stderr);
		} catch {
			io.Cancel();
			try { await Task.WhenAll(status, diagnostic).ConfigureAwait(false); } catch (Exception) { /* Observe both pipe tasks; preserve the original failure. */ }
			throw;
		}
	}
	internal static string FindHelper() {
		foreach (string directory in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(PtyProcess).Assembly.Location) ?? "" }) {
			string path = Path.Combine(directory, "Icod.Pty.Host", "Icod.Pty.Host.dll"); if (File.Exists(path)) return path;
		}
		throw new FileNotFoundException("Icod.Pty.Host assets are missing. Include the NuGet buildTransitive assets or copy the helper directory with the application.");
	}
	internal static string FindDotNet(string? requested) {
		if (requested != null) return requested;
		string? environment = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
		if (!string.IsNullOrEmpty(environment) && File.Exists(environment)) return environment;
		string? root = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent?.Parent?.Parent?.FullName;
		if (root != null && File.Exists(Path.Combine(root, "dotnet"))) return Path.Combine(root, "dotnet");
		return "dotnet";
	}
	private static async Task<int> ObserveExit(Process process) { await process.WaitForExitAsync().ConfigureAwait(false); return process.ExitCode; }
	public void Resize(PtySize size) {
		lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); UnixNative.WindowSize native = new() { Columns = (ushort)size.Columns, Rows = (ushort)size.Rows }; UnixNative.Check(UnixNative.ioctl((int)master.DangerousGetHandle(), UnixNative.SetWindowSize, ref native), "TIOCSWINSZ"); }
	}
	public void Terminate() { lock (gate) { if (child != null) { child.RequestTermination(PtyProcessTarget.PrimaryProcess); return; } if (!process!.HasExited) { try { process.Kill(); } catch (InvalidOperationException) when (process.HasExited) { } } } }
	public void Dispose() {
		lock (gate) { if (disposed) return; disposed = true; }
		bool terminated = false;
		CleanupActions.Run(
			() => { if (child != null) child.Dispose(); else Terminate(); terminated = true; },
			Input.Dispose, master.Dispose, Output.Dispose,
			() => { if (terminated) Exit.GetAwaiter().GetResult(); }, () => process?.Dispose());
	}
}
