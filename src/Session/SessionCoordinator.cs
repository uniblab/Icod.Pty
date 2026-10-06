namespace Icod.Pty.Session;

using Icod.Pty.Recording;

internal sealed class SessionCoordinator {
	private static readonly Task<PtyRecordingResult> DisabledRecording = Task.FromResult(PtyRecordingResult.Disabled);
	private readonly object gate = new();
	private readonly SessionConfiguration configuration;
	private readonly CancellationTokenSource inputStop = new(), outputStop = new(), releaseNow = new();
	private readonly TaskCompletionSource terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<PtySessionResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<PtySessionOutputStatus> outputCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly List<PtySessionFailure> failures = new();
	private readonly List<Task> shutdowns = new();
	private readonly SessionJournal journal = new();
	private readonly SessionRecorder? recorder;
	private PtySessionEndReason? reason;
	private PtyShutdownResult? lastShutdownResult;
	private PtySessionOutputStatus? outputOverride;
	private bool inputSealed;
	internal PtyProcess Process { get; }
	internal Stream Input { get; }
	internal SessionWriter Writer { get; }
	internal Stream Output { get; }
	internal Task<PtySessionResult> Completion => completion.Task;
	internal Task<PtySessionOutputStatus> OutputCompletion => outputCompletion.Task;
	internal Task<PtyRecordingResult> RecordingCompletion => recorder?.Completion ?? DisabledRecording;
	internal PtySessionDiagnostics Diagnostics => journal.Snapshot();
	internal SessionCoordinator(PtyProcess process, SessionConfiguration configuration) {
		Process = process; this.configuration = configuration;
		Input = process.Input; Output = process.Output; Writer = new(Input, journal.AddWrittenToPty);
		recorder = configuration.Recording == null ? null : new(configuration.Recording, process.Size);
		_ = Task.Run(RunAsync);
	}
	private void Trigger(PtySessionEndReason why, PtySessionFailureStage? stage = null, Exception? error = null) {
		bool cancelRelease = false;
		lock (gate) {
			if (error != null) failures.Add(new(stage!.Value, error));
			reason ??= why;
			cancelRelease = why != PtySessionEndReason.PrimaryExited;
			terminal.TrySetResult();
		}
		if (cancelRelease) releaseNow.Cancel();
	}
	private async Task RunAsync() {
		Task input = configuration.Input == null ? Task.CompletedTask : Task.Run(() => SessionPumps.InputAsync(configuration.Input, Writer, inputStop.Token,
			error => { journal.Record(PtySessionEventKind.InputFailed); Trigger(PtySessionEndReason.InputFailed, PtySessionFailureStage.Input, error); },
			count => { if (count == 0) journal.Record(PtySessionEventKind.InputEnded); }));
		Task<PtySessionOutputStatus> output = Task.Run(PumpOutputAsync);
		Task process = Task.Run(ObserveProcessAsync);
		await terminal.Task.ConfigureAwait(false);
		PtySessionEndReason ending; lock (gate) ending = reason!.Value;
		SealInput(); Cleanup(inputStop.Cancel);
		if (ending == PtySessionEndReason.PrimaryExited) {
			journal.SetPhase(PtySessionPhase.Draining);
			Task delay = Task.Delay(configuration.DrainTimeout, releaseNow.Token);
			Task first = await Task.WhenAny(output, delay).ConfigureAwait(false);
			if (first != output) {
				PtySessionOutputStatus? assigned = null;
				lock (gate) {
					if (outputOverride == null) outputOverride = assigned = releaseNow.IsCancellationRequested ? PtySessionOutputStatus.Stopped : PtySessionOutputStatus.TimedOut;
				}
				if (assigned == PtySessionOutputStatus.TimedOut) journal.Record(PtySessionEventKind.DrainTimedOut);
				Cleanup(outputStop.Cancel);
			}
		} else { lock (gate) outputOverride ??= PtySessionOutputStatus.Stopped; Cleanup(outputStop.Cancel); }
		Writer.Stop();
		Task[] activeShutdowns; lock (gate) activeShutdowns = shutdowns.ToArray();
		journal.SetPhase(PtySessionPhase.Releasing); journal.Record(PtySessionEventKind.ReleaseStarted);
		try { await Task.WhenAll(activeShutdowns).ConfigureAwait(false); } catch (Exception) { }
		await Task.Run(() => Cleanup(Process.Dispose)).ConfigureAwait(false);
		await input.ConfigureAwait(false); await output.ConfigureAwait(false); await Writer.Idle.ConfigureAwait(false); await process.ConfigureAwait(false);
		if (recorder != null) await recorder.FinishAsync(output.Result == PtySessionOutputStatus.EndOfStream ? PtyRecordingStatus.Complete : PtyRecordingStatus.Stopped).ConfigureAwait(false);
		if (!configuration.LeaveInputOpen && configuration.Input != null) Cleanup(configuration.Input.Dispose);
		if (!configuration.LeaveOutputOpen) Cleanup(configuration.Output.Dispose);
		journal.SetPhase(PtySessionPhase.Completed); journal.Record(PtySessionEventKind.Completed);
		lock (gate) {
			completion.TrySetResult(new(reason!.Value, Process.HasExited ? Process.ExitCode : null,
				output.Result, lastShutdownResult, Array.AsReadOnly(failures.ToArray())));
		}
	}
	private async Task ObserveProcessAsync() {
		try { await Process.WaitForExitAsync().ConfigureAwait(false); journal.Record(PtySessionEventKind.PrimaryExited); Trigger(PtySessionEndReason.PrimaryExited); }
		catch (Exception error) { journal.Record(PtySessionEventKind.ProcessFailed); Trigger(PtySessionEndReason.ProcessFailed, PtySessionFailureStage.Process, error); }
	}
	private async Task<PtySessionOutputStatus> PumpOutputAsync() {
		Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? record = recorder == null ? null : recorder.RecordOutputAsync;
		PtySessionOutputStatus observed = await SessionPumps.OutputAsync(Output, configuration.Output, outputStop.Token,
			error => {
				lock (gate) { if (outputOverride is null or PtySessionOutputStatus.Stopped) outputOverride = PtySessionOutputStatus.Faulted; }
				journal.Record(PtySessionEventKind.OutputFailed); Trigger(PtySessionEndReason.OutputFailed, PtySessionFailureStage.Output, error);
			},
			journal.AddReadFromPty, journal.AddWrittenToOutput, record).ConfigureAwait(false);
		if (observed == PtySessionOutputStatus.EndOfStream) journal.Record(PtySessionEventKind.OutputEnded);
		PtySessionOutputStatus status; lock (gate) status = outputOverride ?? observed;
		outputCompletion.TrySetResult(status); return status;
	}
	private void Cleanup(Action action) {
		try { action(); } catch (Exception error) { journal.Record(PtySessionEventKind.CleanupFailed); lock (gate) failures.Add(new(PtySessionFailureStage.Cleanup, error)); }
	}
	internal async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) {
		lock (gate) {
			if (reason == PtySessionEndReason.PrimaryExited) throw new InvalidOperationException("Session input has been sealed.");
			ObjectDisposedException.ThrowIf(reason != null, this);
		}
		try { await Writer.WriteAsync(bytes, token).ConfigureAwait(false); }
		catch (Exception error) when (error is not OperationCanceledException and not InvalidOperationException and not ObjectDisposedException) {
			Trigger(PtySessionEndReason.InputFailed, PtySessionFailureStage.Input, error); throw;
		}
	}
	internal void Resize(PtySize size) { Process.Resize(size); recorder?.RecordResize(size); }
	internal Task<PtyShutdownResult> ShutdownAsync(PtyShutdownOptions options, CancellationToken token) {
		Task<PtyShutdownResult> operation; TaskCompletionSource<PtyShutdownResult> tracked = new(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (gate) {
			ObjectDisposedException.ThrowIf(reason != null, this);
			operation = Process.ShutdownAsync(options, AcceptShutdown, WriteShutdownAsync, token);
			shutdowns.Add(tracked.Task);
		}
		_ = ObserveShutdownAsync(operation, tracked);
		return tracked.Task;
	}
	private void AcceptShutdown() {
		inputSealed = true; Writer.Seal(); Cleanup(inputStop.Cancel);
		journal.SealInput(); journal.Record(PtySessionEventKind.ShutdownStarted);
	}
	private async ValueTask WriteShutdownAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) {
		try { await Writer.Idle.WaitAsync(token).ConfigureAwait(false); await Writer.WriteShutdownAsync(bytes, token).ConfigureAwait(false); }
		catch (Exception error) when (error is not OperationCanceledException and not ObjectDisposedException) {
			Trigger(PtySessionEndReason.InputFailed, PtySessionFailureStage.Input, error); throw;
		}
	}
	private async Task ObserveShutdownAsync(Task<PtyShutdownResult> operation, TaskCompletionSource<PtyShutdownResult> tracked) {
		try {
			PtyShutdownResult result = await operation.ConfigureAwait(false);
			lock (gate) lastShutdownResult = result; journal.Record(PtySessionEventKind.ShutdownCompleted); tracked.TrySetResult(result);
		} catch (OperationCanceledException error) { journal.Record(PtySessionEventKind.ShutdownCancelled); tracked.TrySetCanceled(error.CancellationToken); }
		catch (Exception error) { journal.Record(PtySessionEventKind.ShutdownFailed); tracked.TrySetException(error); }
	}
	private void SealInput() {
		lock (gate) { if (inputSealed) return; inputSealed = true; }
		Writer.Seal(); journal.SealInput();
	}
	internal async Task DisposeAsync() {
		Trigger(PtySessionEndReason.Disposed);
		PtySessionResult result = await Completion.ConfigureAwait(false);
		if (result.Failures.Count != 0) throw new AggregateException(result.Failures.Select(item => item.Exception));
	}
}
