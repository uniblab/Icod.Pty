namespace Icod.Pty.Sample;

// Compiled only into the fixture, alongside the actual host-console source files.
internal static class HostConsoleFaults {
	internal static Action? AfterModeChange { get; set; }
	internal static Action? BeforeRead { get; set; }
}
