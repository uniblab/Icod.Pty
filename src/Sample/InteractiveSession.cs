namespace Icod.Pty.Sample;

internal static class InteractiveSession {
	internal static async Task<int> RunAsync(PtyStartInfo startInfo, HostConsole console, CancellationToken cancellationToken) {
		if (console.GetSize() is PtySize initial) startInfo.Size = initial;
		await using PtyProcess process = await PtyProcess.StartAsync(startInfo, cancellationToken);
		using CancellationTokenSource inputStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using CancellationTokenSource outputStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task input = ForwardInputAsync(process.Input, console, inputStop.Token);
		Task output = process.Output.CopyToAsync(console.Output, outputStop.Token);
		Task resize = MonitorSize(process, console, inputStop.Token);
		Task<int> exit = process.WaitForExitAsync();
		try {
			Task completed = await Task.WhenAny(input, output, resize, exit);
			if (completed == input || completed == output) {
				await completed;
				if (!process.HasExited) {
					PtyShutdownResult stopped = await process.ShutdownAsync(new() { ForceTermination = true }, cancellationToken);
					if (stopped.Status != PtyShutdownStatus.Exited) throw new TimeoutException("The child did not exit within the shutdown deadlines.");
				}
			} else if (completed == resize) await resize;
			int code = await exit.WaitAsync(cancellationToken);
			inputStop.Cancel();
			await FinishInput(input, process, inputStop.Token); await ObserveCancellation(resize, inputStop.Token);
			try { await output.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
			catch (TimeoutException error) { throw new TimeoutException("Output draining timed out; some terminal output may be missing.", error); }
			return code;
		} finally {
			inputStop.Cancel(); outputStop.Cancel();
			// All pumps must finish before HostConsole restores the user's console state.
			await Observe(input); await Observe(resize); await Observe(output);
		}
	}
	internal static async Task ForwardInputAsync(Stream input, HostConsole console, CancellationToken token) {
		byte[] buffer = new byte[4096];
		while (true) {
			int count = await console.ReadAsync(buffer, token);
			if (count == 0) return;
			await input.WriteAsync(buffer.AsMemory(0, count), token);
		}
	}
	private static async Task MonitorSize(PtyProcess process, HostConsole console, CancellationToken token) {
		using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
		while (await timer.WaitForNextTickAsync(token)) {
			if (console.GetSize() is not PtySize size || size == process.Size) continue;
			try { process.Resize(size); }
			catch (Exception error) when (process.HasExited && error is InvalidOperationException or IOException) { return; }
		}
	}
	private static async Task FinishInput(Task input, PtyProcess process, CancellationToken token) {
		try { await input; }
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception error) when (process.HasExited && error is IOException or ObjectDisposedException) { }
	}
	private static async Task ObserveCancellation(Task task, CancellationToken token) { try { await task; } catch (OperationCanceledException) when (token.IsCancellationRequested) { } }
	private static async Task Observe(Task task) { try { await task; } catch (Exception) { /* The winning operation reports the failure; observe other completions before cleanup. */ } }
}
