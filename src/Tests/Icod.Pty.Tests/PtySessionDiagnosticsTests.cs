using Icod.Pty.Session;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtySessionDiagnosticsTests {
	[Fact]
	public async Task Lifecycle_snapshot_is_ordered_detached_and_available_after_disposal() {
		using MemoryStream destination = new(); using GateReadStream output = new(); ControlledBackend backend = new() { Output = output };
		PtySession session = await SessionTestSupport.Start(backend, destination); await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		PtySessionDiagnostics before = session.GetDiagnostics(); backend.Completion.SetResult(7); output.Release.SetResult();
		await session.Completion; await session.DisposeAsync(); PtySessionDiagnostics after = session.GetDiagnostics();
		Assert.Single(before.Events); Assert.Equal(PtySessionEventKind.Started, before.Events[0].Kind);
		Assert.Contains(after.Events, item => item.Kind == PtySessionEventKind.PrimaryExited);
		Assert.Contains(after.Events, item => item.Kind == PtySessionEventKind.OutputEnded);
		Assert.Equal(PtySessionEventKind.Completed, after.Events[^1].Kind); Assert.Equal(PtySessionPhase.Completed, after.Phase);
		Assert.True(after.Events.Zip(after.Events.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence && pair.First.Elapsed <= pair.Second.Elapsed));
		Assert.Throws<NotSupportedException>(() => ((IList<PtySessionEvent>)after.Events).Add(after.Events[0]));
	}
	[Fact]
	public void Ring_retains_latest_32_and_exact_drop_count() {
		SessionJournal journal = new(); for (int i = 0; i < 40; i++) journal.Record(PtySessionEventKind.ShutdownStarted);
		PtySessionDiagnostics snapshot = journal.Snapshot(); Assert.Equal(32, snapshot.Events.Count); Assert.Equal(9, snapshot.DroppedEvents);
		Assert.Equal(10, snapshot.Events[0].Sequence); Assert.Equal(41, snapshot.Events[^1].Sequence);
	}
	[Fact]
	public async Task Counters_include_only_completed_operations_and_snapshot_is_private() {
		const string sentinel = "secret-command-environment-output"; using MemoryStream source = new("in"u8.ToArray()), destination = new();
		ControlledBackend backend = new() { Output = new MemoryStream("out"u8.ToArray()) }; PtySession session = await SessionTestSupport.Start(backend, destination, source);
		await SessionTestSupport.Until(() => session.GetDiagnostics().BytesWrittenToPty == 2 && session.GetDiagnostics().BytesWrittenToOutput == 3);
		await session.WriteAsync("!"u8.ToArray()); backend.Completion.SetResult(0); await session.Completion;
		PtySessionDiagnostics value = session.GetDiagnostics(); Assert.Equal(3, value.BytesWrittenToPty); Assert.Equal(3, value.BytesReadFromPty); Assert.Equal(3, value.BytesWrittenToOutput);
		Assert.DoesNotContain(sentinel, string.Join('|', value.Events)); await session.DisposeAsync();
	}
	[Fact]
	public async Task Concurrent_snapshots_are_coherent() {
		SessionJournal journal = new(); Task writer = Task.Run(() => { for (int i = 0; i < 500; i++) { journal.AddReadFromPty(1); journal.Record(PtySessionEventKind.OutputEnded); } });
		Task[] readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => { for (int i = 0; i < 100; i++) { PtySessionDiagnostics item = journal.Snapshot(); Assert.True(item.Events.Count <= 32); Assert.Equal(item.Events.Count + item.DroppedEvents, item.Events.LastOrDefault()?.Sequence ?? 0); } })).ToArray();
		await Task.WhenAll(readers.Append(writer)); Assert.Equal(500, journal.Snapshot().BytesReadFromPty);
	}
}
