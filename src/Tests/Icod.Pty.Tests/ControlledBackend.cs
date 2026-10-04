namespace Icod.Pty.Tests;

internal sealed class ControlledBackend : IPtyBackend {
	internal readonly TaskCompletionSource<int> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	internal int DisposeCount { get; private set; }
	internal int TerminateCount { get; private set; }
	internal int ScopeTerminationCount { get; private set; }
	public PtyProcessOwnership Ownership { get; set; }
	public PtyProcessCapabilities Capabilities { get; set; }
	public PtyControlResult RequestTermination(PtyProcessTarget target) {
		if (target == PtyProcessTarget.OwnedScope) ScopeTerminationCount++; else Terminate();
		return new(target, PtyControlStatus.Requested);
	}
	public PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target) => new(target, PtyControlStatus.Requested);
	internal bool CompleteOnTerminate { get; set; } = true;
	public Stream Input { get; set; } = new MemoryStream();
	public Stream Output { get; set; } = new MemoryStream();
	public int ProcessId => 42;
	public Task<int> Exit => Completion.Task;
	public void Resize(PtySize size) { }
	public void Terminate() { TerminateCount++; if (CompleteOnTerminate) Completion.TrySetResult(1); }
	public void Dispose() { DisposeCount++; Input.Dispose(); Output.Dispose(); Completion.TrySetResult(1); }
	internal static LaunchConfiguration Launch() => new() { Columns = 80, Rows = 24 };
	internal Task<PtyProcess> Start(CancellationToken token = default) => PtyProcess.StartCoreAsync(Launch(), token, (_, _) => Task.FromResult<IPtyBackend>(this));
}
