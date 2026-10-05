using System.Diagnostics;
using System.Text;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtySessionIntegrationTests {
	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task Native_session_drains_final_output_after_graceful_shutdown(bool owned) {
		PtyStartInfo start = PtyTestSupport.Child("final-output"); if (owned) start.Ownership = PtyProcessOwnership.PlatformScope;
		using MemoryStream output = new(); await using PtySession session = await PtySession.StartAsync(start, new(output));
		Assert.Equal(owned ? PtyProcessOwnership.PlatformScope : PtyProcessOwnership.PrimaryProcess, session.Ownership);
		await UntilText(output, "FINAL-READY");
		PtyShutdownResult shutdown = await session.ShutdownAsync(new() { Request = PtyTestSupport.Line("quit") });
		PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(20)); string text = Encoding.UTF8.GetString(output.ToArray());
		Assert.Equal(23, shutdown.ExitCode); Assert.Equal(23, result.ExitCode); Assert.Equal(PtySessionOutputStatus.EndOfStream, result.OutputStatus);
		Assert.Contains("FINAL-END", text); Assert.Empty(result.Failures);
	}
	[Fact]
	public async Task Native_session_orders_explicit_input_and_interrupt() {
		using MemoryStream output = new(); await using PtySession session = await PtySession.StartAsync(PtyTestSupport.Child("raw-input"), new(output));
		await UntilText(output, "RAW-READY"); await session.WriteAsync(new byte[] { 0x41 }); await session.SendInterruptAsync(); await session.WriteAsync(new byte[] { 4 });
		PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(20)); string text = Encoding.UTF8.GetString(output.ToArray());
		Assert.True(text.IndexOf("BYTE:41", StringComparison.Ordinal) < text.IndexOf("BYTE:03", StringComparison.Ordinal), text);
		Assert.Equal(23, result.ExitCode); Assert.Equal(PtySessionOutputStatus.EndOfStream, result.OutputStatus);
	}
	[Fact]
	public async Task Owned_session_primary_exit_cleans_descendant_scope() {
		string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "icod-session-scope-" + Guid.NewGuid().ToString("N"))).FullName;
		try {
			PtyStartInfo info = OperatingSystem.IsWindows() ? PtyTestSupport.Child("scope-windows-parent", directory) : PtyTestSupport.Child("scope-parent", directory, "same");
			info.Ownership = PtyProcessOwnership.PlatformScope; using MemoryStream output = new();
			await using PtySession session = await PtySession.StartAsync(info, new(output) { DrainTimeout = TimeSpan.FromMilliseconds(100) });
			await WaitFile(directory, "parent-ready"); await WaitFile(directory, "child-ready");
			string pidFile = Path.Combine(directory, OperatingSystem.IsWindows() ? "child-ready" : "child-pid");
			using Process child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture));
			File.WriteAllText(Path.Combine(directory, "exit-primary"), "exit"); PtySessionResult result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(20));
			PtySessionOutputStatus expected = OperatingSystem.IsLinux() ? PtySessionOutputStatus.TimedOut : PtySessionOutputStatus.EndOfStream;
			Assert.Equal(37, result.ExitCode); Assert.Equal(expected, result.OutputStatus);
			await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
			Assert.Equal(OperatingSystem.IsLinux() ? 1 : 0, session.GetDiagnostics().Events.Count(e => e.Kind == PtySessionEventKind.DrainTimedOut));
		} finally { File.WriteAllText(Path.Combine(directory, "stop-child"), "stop"); Directory.Delete(directory, true); }
	}
	private static async Task UntilText(MemoryStream stream, string value) => await SessionTestSupport.Until(() => Encoding.UTF8.GetString(stream.ToArray()).Contains(value, StringComparison.Ordinal));
	private static async Task WaitFile(string directory, string name) => await SessionTestSupport.Until(() => File.Exists(Path.Combine(directory, name)));
}
