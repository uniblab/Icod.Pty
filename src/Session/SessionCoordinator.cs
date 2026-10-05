namespace Icod.Pty.Session;

internal sealed class SessionCoordinator {
	private readonly object gate = new();
	private readonly SessionConfiguration configuration;
	private Task? disposal;
	internal PtyProcess Process { get; }
	internal Stream Input { get; }
	internal SessionWriter Writer { get; }
	internal Stream Output { get; }
	internal SessionCoordinator(PtyProcess process, SessionConfiguration configuration) {
		Process = process; this.configuration = configuration;
		Input = process.Input; Output = process.Output; Writer = new(Input);
	}
	internal Task DisposeAsync() {
		lock (gate) return disposal ??= Task.Run(() => { Writer.Stop(); CleanupActions.Run(Process.Dispose,
			() => { if (!configuration.LeaveInputOpen) configuration.Input?.Dispose(); },
			() => { if (!configuration.LeaveOutputOpen) configuration.Output.Dispose(); }); });
	}
}
