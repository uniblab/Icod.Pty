namespace Icod.Pty.Session;

internal sealed class SessionCoordinator {
	private readonly object gate = new();
	private readonly SessionConfiguration configuration;
	private readonly CancellationTokenSource inputStop = new(), outputStop = new();
	private readonly TaskCompletionSource terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<PtySessionResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<PtySessionOutputStatus> outputCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly List<PtySessionFailure> failures = new();
	private PtySessionEndReason? reason;
	internal PtyProcess Process { get; }
	internal Stream Input { get; }
	internal SessionWriter Writer { get; }
	internal Stream Output { get; }
	internal Task<PtySessionResult> Completion => completion.Task;
	internal Task<PtySessionOutputStatus> OutputCompletion => outputCompletion.Task;
	internal SessionCoordinator(PtyProcess process, SessionConfiguration configuration) {
		Process = process; this.configuration = configuration;
		Input = process.Input; Output = process.Output; Writer = new(Input);
		_ = Task.Run(RunAsync);
	}
	private void Trigger(PtySessionEndReason why, PtySessionFailureStage? stage = null, Exception? error = null) {
		lock (gate) {
			if (error != null) failures.Add(new(stage!.Value, error));
			reason ??= why; terminal.TrySetResult();
		}
	}
	private async Task RunAsync() {
		Task input = configuration.Input == null ? Task.CompletedTask : Task.Run(() => SessionPumps.InputAsync(configuration.Input, Writer, inputStop.Token,
			error => Trigger(PtySessionEndReason.InputFailed, PtySessionFailureStage.Input, error)));
		Task<PtySessionOutputStatus> output = Task.Run(PumpOutputAsync);
		await terminal.Task.ConfigureAwait(false);
		Cleanup(inputStop.Cancel); Cleanup(outputStop.Cancel); Cleanup(Writer.Stop);
		await Task.Run(() => Cleanup(Process.Dispose)).ConfigureAwait(false);
		await input.ConfigureAwait(false); await output.ConfigureAwait(false); await Writer.Idle.ConfigureAwait(false);
		if (!configuration.LeaveInputOpen && configuration.Input != null) Cleanup(configuration.Input.Dispose);
		if (!configuration.LeaveOutputOpen) Cleanup(configuration.Output.Dispose);
		lock (gate) completion.TrySetResult(new(reason!.Value, Process.HasExited ? Process.ExitCode : null,
			output.Result, null, Array.AsReadOnly(failures.ToArray())));
	}
	private async Task<PtySessionOutputStatus> PumpOutputAsync() {
		PtySessionOutputStatus status = await SessionPumps.OutputAsync(Output, configuration.Output, outputStop.Token,
			error => Trigger(PtySessionEndReason.OutputFailed, PtySessionFailureStage.Output, error)).ConfigureAwait(false);
		outputCompletion.TrySetResult(status); return status;
	}
	private void Cleanup(Action action) {
		try { action(); } catch (Exception error) { lock (gate) failures.Add(new(PtySessionFailureStage.Cleanup, error)); }
	}
	internal async Task DisposeAsync() {
		Trigger(PtySessionEndReason.Disposed);
		PtySessionResult result = await Completion.ConfigureAwait(false);
		if (result.Failures.Count != 0) throw new AggregateException(result.Failures.Select(item => item.Exception));
	}
}
