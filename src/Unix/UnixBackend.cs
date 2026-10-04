using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Icod.Pty.Unix;

internal sealed class UnixBackend : IPtyBackend {
	private readonly SafeFileHandle master;
	private readonly Process process;
	private readonly object gate = new();
	private bool disposed;
	public Stream Input { get; }
	public Stream Output { get; }
	public int ProcessId { get; }
	public Task<int> Exit { get; }
	private UnixBackend(SafeFileHandle master, Process process) {
		this.master = master; this.process = process; ProcessId = process.Id;
		Input = new UnixPtyStream(master, false); Output = new UnixPtyStream(master, true);
		Exit = ObserveExit(process);
	}
	internal static IPtyBackend Start(LaunchConfiguration launch) {
		UnixNative.WindowSize size = new() { Columns = (ushort)launch.Columns, Rows = (ushort)launch.Rows };
		byte[] name = new byte[1024];
		UnixNative.Check(UnixNative.openpty(out int masterFd, out int slaveFd, name, 0, ref size), "openpty");
		SafeFileHandle master = new(masterFd, true);
		Process? process = null;
		try {
			UnixNative.Check(UnixNative.fcntl(masterFd, 2, 1), "FD_CLOEXEC master");
			UnixNative.Check(UnixNative.fcntl(slaveFd, 2, 1), "FD_CLOEXEC slave");
			int flags = UnixNative.fcntl(masterFd, 3, 0); UnixNative.Check(flags, "F_GETFL");
			UnixNative.Check(UnixNative.fcntl(masterFd, 4, flags | UnixNative.NonBlocking), "F_SETFL");
			launch.SlaveName = Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0));
			string helper = FindHelper();
			ProcessStartInfo start = new(FindDotNet(launch.DotNetHostPath)) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
			start.ArgumentList.Add("exec"); start.ArgumentList.Add(helper);
			process = Process.Start(start) ?? throw new IOException("Unable to start the Unix PTY helper.");
			using CancellationTokenSource timeout = new(launch.StartTimeout);
			try { Handshake(process, launch, timeout.Token).GetAwaiter().GetResult(); }
			catch (OperationCanceledException) when (timeout.IsCancellationRequested) { throw new TimeoutException("The Unix PTY helper did not complete startup within StartTimeout."); }
			return new UnixBackend(master, process);
		} catch {
			master.Dispose();
			if (process != null) { try { if (!process.HasExited) process.Kill(); process.WaitForExit(); } finally { process.Dispose(); } }
			throw;
		} finally { UnixNative.close(slaveFd); }
	}
	private static async Task Handshake(Process process, LaunchConfiguration launch, CancellationToken token) {
		Task<string> diagnostic = process.StandardError.ReadToEndAsync(token);
		Task<string> status = process.StandardOutput.ReadToEndAsync(token);
		await process.StandardInput.WriteAsync(JsonSerializer.Serialize(launch).AsMemory(), token).ConfigureAwait(false);
		process.StandardInput.Close();
		string result = await status.ConfigureAwait(false);
		string stderr = await diagnostic.ConfigureAwait(false);
		// A ready byte followed by close-on-exec EOF distinguishes startup from early host failure.
		if (result != "1" || stderr.Length != 0) throw new IOException("Unix PTY launch failed: " + result + stderr);
	}
	private static string FindHelper() {
		foreach (string directory in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(PtyProcess).Assembly.Location) ?? "" }) {
			string path = Path.Combine(directory, "Icod.Pty.Host", "Icod.Pty.Host.dll"); if (File.Exists(path)) return path;
		}
		throw new FileNotFoundException("Icod.Pty.Host assets are missing. Include the NuGet buildTransitive assets or copy the helper directory with the application.");
	}
	private static string FindDotNet(string? requested) {
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
	public void Terminate() { lock (gate) { if (!process.HasExited) { try { process.Kill(); } catch (InvalidOperationException) when (process.HasExited) { } } } }
	public void Dispose() {
		lock (gate) {
			if (disposed) return; disposed = true;
			try { Terminate(); }
			finally { Input.Dispose(); Output.Dispose(); master.Dispose(); try { Exit.GetAwaiter().GetResult(); } finally { process.Dispose(); } }
		}
	}
}
