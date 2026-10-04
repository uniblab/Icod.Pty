namespace Icod.Pty;

internal interface IPtyBackend : IDisposable {
	Stream Input { get; }
	Stream Output { get; }
	int ProcessId { get; }
	PtyProcessOwnership Ownership { get; }
	PtyProcessCapabilities Capabilities { get; }
	PtyControlResult RequestTermination(PtyProcessTarget target);
	PtyControlResult SendSignal(PtySignal signal, PtyProcessTarget target);
	Task<int> Exit { get; }
	void Resize(PtySize size);
	void Terminate();
}
