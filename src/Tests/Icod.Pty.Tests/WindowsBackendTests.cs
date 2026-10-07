using Icod.Pty.Windows;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class WindowsBackendTests {
	[Fact]
	public void Terminate_waits_for_an_already_terminated_process_after_access_denied() {
		List<uint> waits = [];
		int terminations = 0;

		WindowsBackend.TerminatePrimary(
			milliseconds => {
				waits.Add(milliseconds);
				return waits.Count < 3 ? 258u : 0u;
			},
			() => { terminations++; return false; },
			() => 5);

		Assert.Equal([0u, 0u, uint.MaxValue], waits);
		Assert.Equal(1, terminations);
	}

	[Fact]
	public void Terminate_preserves_other_native_failures() {
		List<uint> waits = [];

		IOException error = Assert.Throws<IOException>(() => WindowsBackend.TerminatePrimary(
			milliseconds => { waits.Add(milliseconds); return 258u; },
			() => false,
			() => 87));

		Assert.Equal([0u, 0u], waits);
		Assert.Contains("Win32 error 87", error.Message);
	}
}
