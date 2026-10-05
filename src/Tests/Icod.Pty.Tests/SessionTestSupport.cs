using Icod.Pty.Session;

namespace Icod.Pty.Tests;

internal static class SessionTestSupport {
	internal static Task<PtySession> Start(ControlledBackend backend, Stream output, Stream? input = null, TimeSpan? drain = null,
		bool leaveOpen = true) => PtySession.StartCoreAsync(ControlledBackend.Launch(), SessionConfiguration.Capture(new(output) {
			Input = input, DrainTimeout = drain ?? TimeSpan.FromSeconds(5), LeaveInputOpen = leaveOpen, LeaveOutputOpen = leaveOpen
		}), default, (_, token) => backend.Start(token));
	internal static async Task Until(Func<bool> condition) {
		using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
		while (!condition()) await Task.Delay(10, deadline.Token);
	}
	internal static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class GateWriteStream : MemoryStream {
	internal readonly TaskCompletionSource Entered = SessionTestSupport.NewSignal(), Release = SessionTestSupport.NewSignal();
	internal int Prefix, Calls, Active, MaximumActive;
	internal bool IgnoreCancellation;
	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
		int active = Interlocked.Increment(ref Active); MaximumActive = Math.Max(MaximumActive, active);
		try {
			if (Interlocked.Increment(ref Calls) == 1) {
				if (Prefix != 0) base.Write(buffer.Span[..Prefix]);
				Entered.TrySetResult();
				if (IgnoreCancellation) await Release.Task; else await Release.Task.WaitAsync(token);
				buffer = buffer[Prefix..];
			}
			token.ThrowIfCancellationRequested(); base.Write(buffer.Span);
		} finally { Interlocked.Decrement(ref Active); }
	}
}

internal sealed class TrackingStream : MemoryStream {
	internal int Flushes, Disposals;
	internal Exception? FlushFailure, DisposeFailure;
	public override Task FlushAsync(CancellationToken token) { Flushes++; return FlushFailure == null ? Task.CompletedTask : Task.FromException(FlushFailure); }
	protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); if (DisposeFailure != null) throw DisposeFailure; }
}
