namespace Icod.Pty;

/// <summary>Owns a child process and its pseudoterminal.</summary>
/// <remarks>Use one reader and one writer concurrently. Output combines standard output and error.
/// Disposal closes the terminal and terminates a live primary child. Detached descendants are not owned.</remarks>
public sealed class PtyProcess : IDisposable, IAsyncDisposable {
	private readonly IPtyBackend backend;
	private readonly object gate = new();
	private PtySize size;
	private bool disposed;
	private PtyProcess(IPtyBackend backend, PtySize size) { this.backend = backend; this.size = size; }
	/// <summary>Starts the executable on a new terminal. Unix requires an installed dotnet host.</summary>
	public static PtyProcess Start(PtyStartInfo startInfo) => StartAsync(startInfo).GetAwaiter().GetResult();
	/// <summary>Starts an executable asynchronously and transfers ownership after startup succeeds.</summary>
	/// <remarks>Cancellation cleans up a partially created child before completing. Native creation and cleanup
	/// are not interruptible hard deadlines. Cancellation after a successful return does not stop the child.</remarks>
	public static Task<PtyProcess> StartAsync(PtyStartInfo startInfo, CancellationToken cancellationToken = default) {
		LaunchConfiguration launch = LaunchConfiguration.Capture(startInfo);
		return StartCoreAsync(launch, cancellationToken, CreateBackendAsync);
	}
	internal static async Task<PtyProcess> StartCoreAsync(LaunchConfiguration launch, CancellationToken cancellationToken,
		Func<LaunchConfiguration, CancellationToken, Task<IPtyBackend>> backendFactory) {
		cancellationToken.ThrowIfCancellationRequested();
		IPtyBackend backend = await backendFactory(launch, cancellationToken).ConfigureAwait(false);
		try {
			cancellationToken.ThrowIfCancellationRequested();
			return new PtyProcess(backend, new PtySize(launch.Columns, launch.Rows));
		} catch { backend.Dispose(); throw; }
	}
	private static Task<IPtyBackend> CreateBackendAsync(LaunchConfiguration launch, CancellationToken token) {
		if (OperatingSystem.IsWindows()) return Windows.WindowsBackend.StartAsync(launch, token);
		if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return Unix.UnixBackend.StartAsync(launch, token);
		throw new PlatformNotSupportedException("Icod.Pty supports Windows, Linux and macOS.");
	}
	/// <summary>Gets the writable stream carrying bytes to the terminal.</summary>
	public Stream Input { get { lock (gate) { ThrowIfDisposed(); return backend.Input; } } }
	/// <summary>Gets the readable stream carrying terminal output bytes.</summary>
	public Stream Output { get { lock (gate) { ThrowIfDisposed(); return backend.Output; } } }
	/// <summary>Gets the primary child's process identifier.</summary>
	public int ProcessId => backend.ProcessId;
	/// <summary>Gets the most recently applied terminal dimensions.</summary>
	public PtySize Size { get { lock (gate) return size; } }
	/// <summary>Gets whether exit status has been collected.</summary>
	public bool HasExited => backend.Exit.IsCompletedSuccessfully;
	/// <summary>Gets the collected exit code, or throws while the child is running.</summary>
	public int ExitCode => HasExited ? backend.Exit.Result : throw new InvalidOperationException("The child has not exited.");
	/// <summary>Changes the terminal's character-cell dimensions.</summary>
	public void Resize(PtySize newSize) {
		_ = new PtySize(newSize.Columns, newSize.Rows);
		lock (gate) { ThrowIfDisposed(); if (HasExited) throw new InvalidOperationException("The child has exited."); backend.Resize(newSize); size = newSize; }
	}
	/// <summary>Waits for process exit. Cancellation stops only this wait, not the process.</summary>
	public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) => backend.Exit.WaitAsync(cancellationToken);
	/// <summary>Forcibly terminates a live primary child; repeated calls after exit have no effect.</summary>
	public void Terminate() { lock (gate) { ThrowIfDisposed(); backend.Terminate(); } }
	/// <summary>Closes the terminal, terminates and reaps the primary child, and releases owned resources.</summary>
	public void Dispose() { lock (gate) { if (disposed) return; disposed = true; backend.Dispose(); } }
	/// <summary>Performs disposal without blocking the calling thread.</summary>
	public ValueTask DisposeAsync() => new(Task.Run(Dispose));
	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
