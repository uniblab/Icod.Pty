using System.Diagnostics;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PackageSmokeTests {
	[Theory]
	[InlineData("--smoke")]
	[InlineData("--lifecycle-smoke")]
	[InlineData("--cancel-start-smoke")]
	[InlineData("--interrupt-smoke")]
	[InlineData("--session-smoke")]
	[InlineData("--session-scope-smoke")]
	[InlineData("--scope-smoke")]
	[InlineData("--terminal-config-smoke")]
	public async Task Verification_modes_work_with_redirected_host(string mode) {
		using Process process = Process.Start(new ProcessStartInfo(PtyTestSupport.DotNet) {
			ArgumentList = { Path.Combine(AppContext.BaseDirectory, "sample", "Icod.Pty.Sample.dll"), mode },
			RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
		})!;
		try {
			Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
			Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}: {await output} {await error}");
			Assert.Contains("passed", await output);
		} finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
	}
}
