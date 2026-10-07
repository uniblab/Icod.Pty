using System.Runtime.CompilerServices;

namespace Icod.Pty;

/// <summary>Executes a fixed sequence of send and expect operations without taking session ownership.</summary>
public static class PtyScriptRunner {
	private sealed class RunnerGate { internal int Active; }
	private static readonly ConditionalWeakTable<PtySession, RunnerGate> Gates = new();

	/// <summary>Executes copied steps in order. Cancellation and input-write failures are propagated.</summary>
	public static async Task<PtyScriptResult> RunAsync(PtySession session, IReadOnlyList<PtyScriptStep> steps,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(steps);
		RunnerGate gate = Gates.GetValue(session, static _ => new());
		if (Interlocked.CompareExchange(ref gate.Active, 1, 0) != 0)
			throw new InvalidOperationException("Only one script can run on a session at a time.");

		try {
			PtyScriptStep[] snapshot = new PtyScriptStep[steps.Count];
			for (int index = 0; index < snapshot.Length; index++)
				snapshot[index] = steps[index] ?? throw new ArgumentException("Script steps must not contain null.", nameof(steps));

			for (int index = 0; index < snapshot.Length; index++) {
				cancellationToken.ThrowIfCancellationRequested();
				PtyScriptStep step = snapshot[index];
				switch (step.Kind) {
					case PtyScriptStepKind.Send:
						await session.WriteAsync(step.Bytes, cancellationToken).ConfigureAwait(false);
						break;
					case PtyScriptStepKind.Expect:
						PtyExpectResult result = await session.ExpectAsync(step.Bytes, step.Timeout, cancellationToken).ConfigureAwait(false);
						if (result.Status != PtyExpectStatus.Matched)
							return new(PtyScriptStatus.ExpectationFailed, index, index, result.Status);
						break;
					default:
						throw new InvalidOperationException("The script contains an unsupported step kind.");
				}
			}

			return new(PtyScriptStatus.Completed, snapshot.Length, null, null);
		} finally { Volatile.Write(ref gate.Active, 0); }
	}
}
