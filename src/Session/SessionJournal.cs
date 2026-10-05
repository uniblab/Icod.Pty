using System.Diagnostics;

namespace Icod.Pty.Session;

internal sealed class SessionJournal {
	private const int Capacity = 32;
	private readonly object gate = new();
	private readonly Stopwatch clock = Stopwatch.StartNew();
	private readonly Queue<PtySessionEvent> events = new();
	private long nextSequence, dropped, writtenToPty, readFromPty, writtenToOutput;
	private bool inputSealed;
	private PtySessionPhase phase = PtySessionPhase.Running;
	internal SessionJournal() => Record(PtySessionEventKind.Started);
	internal void Record(PtySessionEventKind kind) {
		lock (gate) {
			if (events.Count == Capacity) { events.Dequeue(); dropped++; }
			events.Enqueue(new(++nextSequence, clock.Elapsed, kind));
		}
	}
	internal void SetPhase(PtySessionPhase value) { lock (gate) phase = value; }
	internal void SealInput() { lock (gate) inputSealed = true; Record(PtySessionEventKind.InputSealed); }
	internal void AddWrittenToPty(int count) { lock (gate) writtenToPty += count; }
	internal void AddReadFromPty(int count) { lock (gate) readFromPty += count; }
	internal void AddWrittenToOutput(int count) { lock (gate) writtenToOutput += count; }
	internal PtySessionDiagnostics Snapshot() {
		lock (gate) return new(phase, inputSealed, writtenToPty, readFromPty, writtenToOutput, dropped,
			Array.AsReadOnly(events.ToArray()));
	}
}
