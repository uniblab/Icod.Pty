namespace Icod.Pty;

/// <summary>Owns a child process and its pseudoterminal.</summary>
/// <remarks>Use one reader and one writer concurrently. Output combines standard output and error.
/// Disposal closes the terminal and cleans the selected ownership scope. PlatformScope covers a Windows job
/// or the initial Unix group, with no universal descendant guarantee.</remarks>
public sealed class PtyProcess : IDisposable, IAsyncDisposable {
	private readonly IPtyBackend backend;
	private readonly object gate = new();
	private PtySize size;
	private bool disposed;
	private int shutdownActive;
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
		} catch (Exception error) { CleanupActions.AfterFailure(error, backend.Dispose); throw; }
	}
	internal static Task<PtyProcess> StartCapturedAsync(LaunchConfiguration launch, CancellationToken token) => StartCoreAsync(launch, token, CreateBackendAsync);
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
	/// <summary>Gets the ownership policy captured at launch; available after disposal.</summary>
	public PtyProcessOwnership Ownership => backend.Ownership;
	/// <summary>Gets supported optional controls. Capabilities do not imply that a target is still alive.</summary>
	public PtyProcessCapabilities Capabilities => backend.Capabilities;
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
	/// <summary>Sends terminal Ctrl+C input (one byte, 0x03) to the child terminal.</summary>
	/// <remarks>The child's terminal modes determine whether this interrupts a foreground job or is ordinary input.
	/// Coordinate with other input writers. Cancellation may occur after the byte was delivered; it never terminates the child.</remarks>
	public ValueTask SendInterruptAsync(CancellationToken cancellationToken = default) {
		lock (gate) {
			ThrowIfDisposed();
			if (HasExited) throw new InvalidOperationException("The child has exited.");
			cancellationToken.ThrowIfCancellationRequested();
			return backend.Input.WriteAsync(new byte[] { 0x03 }, cancellationToken);
		}
	}
	/// <summary>Writes an optional exit request, waits for exit, and optionally requests forced termination after a deadline.</summary>
	/// <remarks>Keep draining Output concurrently and coordinate other input writers. This operation does not read output,
	/// close streams, or dispose the session. A second concurrent shutdown is rejected. Cancellation stops this operation;
	/// it cannot retract bytes or undo a termination request already issued.</remarks>
	public Task<PtyShutdownResult> ShutdownAsync(PtyShutdownOptions options, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(options);
		var snapshot = options.Capture();
		lock (gate) {
			ThrowIfDisposed(); cancellationToken.ThrowIfCancellationRequested();
			ValidateTarget(snapshot.Target);
			if (Interlocked.CompareExchange(ref shutdownActive, 1, 0) != 0) throw new InvalidOperationException("A shutdown operation is already in progress.");
		}
		return ShutdownCoreAsync(snapshot.Request, snapshot.GracePeriod, snapshot.ForceTermination, snapshot.TerminationTimeout, snapshot.Target, cancellationToken);
	}
	internal Task<PtyShutdownResult> ShutdownAsync(PtyShutdownOptions options, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> writeRequest, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(writeRequest);
		var snapshot = options.Capture();
		lock (gate) {
			ThrowIfDisposed(); cancellationToken.ThrowIfCancellationRequested(); ValidateTarget(snapshot.Target);
			if (Interlocked.CompareExchange(ref shutdownActive, 1, 0) != 0) throw new InvalidOperationException("A shutdown operation is already in progress.");
		}
		return ShutdownCoreAsync(snapshot.Request, snapshot.GracePeriod, snapshot.ForceTermination, snapshot.TerminationTimeout, snapshot.Target, cancellationToken, writeRequest);
	}
	private async Task<PtyShutdownResult> ShutdownCoreAsync(byte[] request, TimeSpan gracePeriod, bool forceTermination, TimeSpan terminationTimeout, PtyProcessTarget target, CancellationToken cancellationToken, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? writeRequest = null) {
		try { return await ShutdownCoordinator.RunAsync(backend, request, gracePeriod, forceTermination, terminationTimeout, cancellationToken, target, writeRequest).ConfigureAwait(false); }
		finally { Interlocked.Exchange(ref shutdownActive, 0); }
	}
	/// <summary>Forcibly terminates a live primary child; repeated calls after exit have no effect.</summary>
	public void Terminate() { lock (gate) { ThrowIfDisposed(); backend.Terminate(); } }
	/// <summary>Requests forced termination of the primary child or the opted-in platform scope.</summary>
	/// <remarks>Requested reports native acceptance, not completion or universal descendant cleanup.
	/// Default Unix primary control returns DispatchUnconfirmed because Process.Kill cannot report native dispatch.
	/// OwnedScope requires PlatformScope at launch. On Unix it covers only the initial process group.</remarks>
	/// <exception cref="ArgumentOutOfRangeException">The target is not a defined value.</exception>
	/// <exception cref="InvalidOperationException">OwnedScope was requested without launch opt-in.</exception>
	/// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
	/// <exception cref="IOException">Native control failed or Unix child identity was lost.</exception>
	public PtyControlResult RequestTermination(PtyProcessTarget target) {
		lock (gate) { ThrowIfDisposed(); ValidateTarget(target); return backend.RequestTermination(target); }
	}
	/// <summary>Sends a named Unix signal to an anchored primary child or its initial group.</summary>
	/// <remarks>Requires PlatformScope even for a primary target. This is independent of terminal modes;
	/// SendInterruptAsync instead writes a Ctrl+C input byte. Windows does not support native Unix signals.</remarks>
	/// <exception cref="ArgumentOutOfRangeException">The signal or target is not defined.</exception>
	/// <exception cref="PlatformNotSupportedException">The platform is Windows.</exception>
	/// <exception cref="InvalidOperationException">PlatformScope was not selected at launch.</exception>
	/// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
	/// <exception cref="IOException">Native delivery failed or child identity was lost.</exception>
	public PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target) {
		lock (gate) {
			ThrowIfDisposed();
			if (!Enum.IsDefined(signal)) throw new ArgumentOutOfRangeException(nameof(signal));
			if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
			if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Unix signals are not available on Windows.");
			if (Ownership != PtyProcessOwnership.PlatformScope) throw new InvalidOperationException("Native signals require platform-scope ownership at launch.");
			return backend.SendSignal(signal, target);
		}
	}
	private void ValidateTarget(PtyProcessTarget target) {
		if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
		if (target == PtyProcessTarget.OwnedScope && Ownership != PtyProcessOwnership.PlatformScope) throw new InvalidOperationException("OwnedScope requires platform-scope ownership at launch.");
	}
	/// <summary>Cleans the selected ownership scope, collects the primary child, and releases terminal resources.</summary>
	/// <remarks>Opted-in scope cleanup also runs after primary exit. All resource releases are attempted even
	/// after control failure; disposal may throw. A Unix child that rejects termination is reaped on natural exit.
	/// Abrupt cleanup need not preserve unread output. Repeated disposal is harmless.</remarks>
	public void Dispose() { lock (gate) { if (disposed) return; disposed = true; backend.Dispose(); } }
	/// <summary>Performs disposal without blocking the calling thread.</summary>
	public ValueTask DisposeAsync() => new(Task.Run(Dispose));
	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
