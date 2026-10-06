using Icod.Pty.Session;

namespace Icod.Pty;

/// <summary>Exclusively owns a newly launched PTY process and coordinated byte forwarding.</summary>
/// <remarks>Use PtyProcess when raw streams or caller-managed lifetime are required.</remarks>
public sealed class PtySession : IDisposable, IAsyncDisposable {
	private readonly SessionCoordinator coordinator;
	private PtySession(SessionCoordinator coordinator) => this.coordinator = coordinator;
	/// <summary>Captures settings and starts an owned session. Cancellation affects startup only.</summary>
	/// <remarks>Failed startup collects the child and leaves all supplied streams open. Streams must cooperate with cancellation.</remarks>
	public static Task<PtySession> StartAsync(PtyStartInfo startInfo, PtySessionOptions options, CancellationToken cancellationToken = default) {
		SessionConfiguration configuration = SessionConfiguration.Capture(options);
		LaunchConfiguration launch = LaunchConfiguration.Capture(startInfo);
		return StartCoreAsync(launch, configuration, cancellationToken, PtyProcess.StartCapturedAsync);
	}
	internal static async Task<PtySession> StartCoreAsync(LaunchConfiguration launch, SessionConfiguration configuration,
		CancellationToken token, Func<LaunchConfiguration, CancellationToken, Task<PtyProcess>> factory) {
		token.ThrowIfCancellationRequested();
		PtyProcess process = await factory(launch, token).ConfigureAwait(false);
		try {
			token.ThrowIfCancellationRequested();
			return new(new SessionCoordinator(process, configuration));
		} catch (Exception error) { CleanupActions.AfterFailure(error, process.Dispose); throw; }
	}
	/// <summary>Gets the primary process identifier, including after disposal.</summary>
	public int ProcessId => coordinator.Process.ProcessId;
	/// <summary>Gets the last applied terminal size.</summary>
	public PtySize Size => coordinator.Process.Size;
	/// <summary>Gets whether primary exit status was collected, independently of output completion.</summary>
	public bool HasExited => coordinator.Process.HasExited;
	/// <summary>Gets the captured ownership policy.</summary>
	public PtyProcessOwnership Ownership => coordinator.Process.Ownership;
	/// <summary>Gets supported native controls, not target liveness.</summary>
	public PtyProcessCapabilities Capabilities => coordinator.Process.Capabilities;
	/// <summary>Gets the shared result after all owned work and cleanup settle.</summary>
	public Task<PtySessionResult> Completion => coordinator.Completion;
	/// <summary>Gets output EOF, timeout, stop, or failure independently of primary exit.</summary>
	public Task<PtySessionOutputStatus> OutputCompletion => coordinator.OutputCompletion;
	/// <summary>Gets the independent result of optional output/resize recording.</summary>
	public Task<PtyRecordingResult> RecordingCompletion => coordinator.RecordingCompletion;
	/// <summary>Writes one ordered input operation. Keep the memory unchanged until completion.</summary>
	public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => coordinator.WriteAsync(bytes, cancellationToken);
	/// <summary>Writes one ordered ETX byte. Terminal modes determine its effect.</summary>
	public ValueTask SendInterruptAsync(CancellationToken cancellationToken = default) {
		if (HasExited) throw new InvalidOperationException("The child has exited.");
		return WriteAsync(new byte[] { 3 }, cancellationToken);
	}
	/// <summary>Seals ordinary input, sends an optional ordered request, and waits under the supplied shutdown policy.</summary>
	public Task<PtyShutdownResult> ShutdownAsync(PtyShutdownOptions options, CancellationToken cancellationToken = default) =>
		coordinator.ShutdownAsync(options, cancellationToken);
	/// <summary>Changes the terminal's character-cell dimensions.</summary>
	public void Resize(PtySize size) => coordinator.Resize(size);
	/// <summary>Requests termination of the selected process target.</summary>
	public PtyControlResult RequestTermination(PtyProcessTarget target) => coordinator.Process.RequestTermination(target);
	/// <summary>Sends a Unix signal to the selected process target.</summary>
	public PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target) => coordinator.Process.SendSignal(signal, target);
	/// <summary>Gets a detached lifecycle snapshot.</summary>
	public PtySessionDiagnostics GetDiagnostics() => coordinator.Diagnostics;
	/// <summary>Releases the session and joins owned work.</summary>
	public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
	/// <summary>Releases the session and joins owned work without blocking the calling thread.</summary>
	public ValueTask DisposeAsync() => new(coordinator.DisposeAsync());
}
