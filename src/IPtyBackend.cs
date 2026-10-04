namespace Icod.Pty;

internal interface IPtyBackend : IDisposable {
	Stream Input { get; }
	Stream Output { get; }
	int ProcessId { get; }
	Task<int> Exit { get; }
	void Resize(PtySize size);
	void Terminate();
}
