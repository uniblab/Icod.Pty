using System.Text;
using System.Text.Json;
using Xunit;

namespace Icod.Pty.Tests;

public sealed class PtyTests {
	private static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
	private static PtyStartInfo Child(params string[] args) {
		PtyStartInfo info = new(DotNet) { Size = new PtySize(93, 31), DotNetHostPath = DotNet };
		info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "child", "Icod.Pty.TestChild.dll"));
		foreach (string arg in args) info.ArgumentList.Add(arg);
		return info;
	}
	[Theory]
	[InlineData(0, 1)] [InlineData(1, 0)] [InlineData(-1, 24)] [InlineData(32768, 24)]
	public void Invalid_size_is_rejected(int columns, int rows) => Assert.Throws<ArgumentOutOfRangeException>(() => new PtySize(columns, rows));
	[Fact]
	public void Missing_executable_reports_start_failure() => Assert.ThrowsAny<IOException>(() => PtyProcess.Start(new PtyStartInfo("icod-pty-missing-" + Guid.NewGuid().ToString("N"))));
	[Fact]
	public void Nul_argument_is_rejected_before_launch() { PtyStartInfo info = Child("a\0b"); Assert.Throws<ArgumentException>(() => PtyProcess.Start(info)); }
	[Fact]
	public async Task Child_has_terminal_arguments_environment_and_directory() {
		string[] args = ["", "space argument", "a\"b", "ends\\", "snow-雪", "$(literal)"];
		PtyStartInfo info = Child(args);
		info.Size = new PtySize(1024, 31);
		info.WorkingDirectory = AppContext.BaseDirectory;
		info.Environment["ICOD_PTY_TEST_VALUE"] = "value-雪";
		info.Environment["ICOD_PTY_TEST_REMOVED"] = null;
		await using PtyProcess process = PtyProcess.Start(info);
		string ready = await ReadUntil(process.Output, "READY:", "\n");
		int start = ready.IndexOf("READY:", StringComparison.Ordinal) + 6;
		using JsonDocument json = ParseReady(StripAnsi(ready[start..]).Trim());
		Assert.True(json.RootElement.GetProperty("Terminal").GetBoolean());
		Assert.True(json.RootElement.GetProperty("ControllingTerminal").GetBoolean());
		Assert.Equal(args, json.RootElement.GetProperty("Arguments").EnumerateArray().Select(x => x.GetString()).ToArray());
		Assert.Equal("value-雪", json.RootElement.GetProperty("Value").GetString());
		Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Removed").ValueKind);
		Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.TrimEndingDirectorySeparator(json.RootElement.GetProperty("Directory").GetString()!));
		await Send(process, "quit\n");
		Task<string> tail = ReadToEnd(process.Output);
		Assert.Equal(23, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Contains("BYE-MARKER", await tail.WaitAsync(TimeSpan.FromSeconds(20)));
	}
	[Fact]
	public async Task Resize_and_bidirectional_io_work() {
		await using PtyProcess process = PtyProcess.Start(Child());
		await ReadUntil(process.Output, "READY:", "\n");
		await ExpectSize(process, "93,31");
		process.Resize(new PtySize(101, 42));
		await ExpectSize(process, "101,42");
		await Send(process, "hello-雪\n");
		Assert.Contains("ECHO:hello-雪", await ReadUntil(process.Output, "ECHO:", "\n"));
		Assert.Equal(new PtySize(101, 42), process.Size);
	}
	[Fact]
	public async Task Exit_does_not_discard_final_output() {
		await using PtyProcess process = PtyProcess.Start(Child("exit"));
		Task<string> output = ReadToEnd(process.Output);
		Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Contains("FINAL-MARKER", await output.WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.True(process.HasExited); Assert.Equal(37, process.ExitCode);
	}
	[Fact]
	public async Task Output_can_be_read_after_fast_child_has_exited() {
		for (int i = 0; i < 10; i++) {
			await using PtyProcess process = await PtyProcess.StartAsync(Child("exit"));
			Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
			Assert.Contains("FINAL-MARKER", await ReadToEnd(process.Output).WaitAsync(TimeSpan.FromSeconds(20)));
		}
	}
	[Fact]
	public async Task Cancelled_wait_and_read_leave_process_usable() {
		await using PtyProcess process = PtyProcess.Start(Child());
		await ReadUntil(process.Output, "READY:", "\n");
		using CancellationTokenSource cancellation = new(150);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.WaitForExitAsync(cancellation.Token));
		Assert.False(process.HasExited);
		using CancellationTokenSource readCancellation = new(150);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { byte[] buffer = new byte[128]; while (await process.Output.ReadAsync(buffer, readCancellation.Token) != 0) { } });
		await Send(process, "quit\n");
		Task<string> tail = ReadToEnd(process.Output);
		Assert.Equal(23, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Contains("BYE-MARKER", await tail.WaitAsync(TimeSpan.FromSeconds(20)));
	}
	[Fact]
	public async Task Dispose_unblocks_pending_read_and_reaps_live_child() {
		PtyProcess process = PtyProcess.Start(Child());
		await ReadUntil(process.Output, "READY:", "\n");
		Task pending = process.Output.ReadAsync(new byte[32]).AsTask();
		await process.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));
		try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { }
		Assert.True(process.HasExited);
		process.Dispose();
	}
	[Fact]
	public async Task Dispose_with_backpressured_output_completes() {
		PtyProcess process = PtyProcess.Start(Child("flood"));
		await Task.Delay(200);
		await process.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));
		Assert.True(process.HasExited);
	}
	[Fact]
	public async Task Terminate_is_idempotent_and_wait_reports_exit() {
		await using PtyProcess process = PtyProcess.Start(Child());
		await ReadUntil(process.Output, "READY:", "\n");
		process.Terminate();
		await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
		process.Terminate(); Assert.True(process.HasExited);
	}
	[Fact]
	public async Task Unix_PATH_search_skips_nonexecutable_shadow() {
		if (OperatingSystem.IsWindows()) return;
		string root = Path.Combine(Path.GetTempPath(), "icod-pty-path-" + Guid.NewGuid().ToString("N"));
		try {
			string first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
			string second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
			string name = "icod-pty-command";
			File.WriteAllText(Path.Combine(first, name), "not executable");
			File.SetUnixFileMode(Path.Combine(first, name), UnixFileMode.UserRead | UnixFileMode.UserWrite);
			File.WriteAllText(Path.Combine(second, name), "#!/bin/sh\nexit 37\n");
			File.SetUnixFileMode(Path.Combine(second, name), UnixFileMode.UserRead | UnixFileMode.UserExecute);
			PtyStartInfo info = new(name); info.Environment["PATH"] = first + Path.PathSeparator + second;
			await using PtyProcess process = PtyProcess.Start(info);
			Assert.Equal(37, await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)));
		} finally { Directory.Delete(root, true); }
	}
	[Fact]
	public void Unix_exec_failure_is_reported_during_start() {
		if (OperatingSystem.IsWindows()) return;
		string path = Path.GetTempFileName();
		try {
			File.WriteAllText(path, "not an executable image");
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
			IOException error = Assert.Throws<IOException>(() => PtyProcess.Start(new PtyStartInfo(path)));
			Assert.Contains("execve", error.Message);
		} finally { File.Delete(path); }
	}
	[Fact]
	public void Unix_helper_without_handshake_is_rejected() {
		if (OperatingSystem.IsWindows()) return;
		PtyStartInfo info = Child(); info.DotNetHostPath = "/usr/bin/true";
		Assert.ThrowsAny<IOException>(() => PtyProcess.Start(info));
	}
	[Fact]
	public void Unix_helper_startup_timeout_is_bounded() {
		if (OperatingSystem.IsWindows()) return;
		string path = Path.GetTempFileName();
		try {
			File.WriteAllText(path, "#!/bin/sh\nexec sleep 60\n");
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			PtyStartInfo info = Child(); info.DotNetHostPath = path; info.StartTimeout = TimeSpan.FromMilliseconds(250);
			Assert.Throws<TimeoutException>(() => PtyProcess.Start(info));
		} finally { File.Delete(path); }
	}
	[Fact]
	public async Task Concurrent_sessions_keep_output_separate() {
		await using PtyProcess first = PtyProcess.Start(Child());
		await using PtyProcess second = PtyProcess.Start(Child());
		await Task.WhenAll(ReadUntil(first.Output, "READY:", "\n"), ReadUntil(second.Output, "READY:", "\n"));
		await Task.WhenAll(Send(first, "first\n"), Send(second, "second\n"));
		Assert.Contains("ECHO:first", await ReadUntil(first.Output, "ECHO:", "\n"));
		Assert.Contains("ECHO:second", await ReadUntil(second.Output, "ECHO:", "\n"));
		Assert.NotEqual(first.ProcessId, second.ProcessId);
	}
	private static Task Send(PtyProcess process, string value) => process.Input.WriteAsync(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? value.Replace("\n", "\r") : value)).AsTask();
	private static JsonDocument ParseReady(string value) {
		// ConPTY can append VT title/cursor sequences after the complete JSON value.
		Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(value));
		return JsonDocument.ParseValue(ref reader);
	}
	private static async Task ExpectSize(PtyProcess process, string expected) {
		DateTime deadline = DateTime.UtcNow.AddSeconds(20);
		string observed;
		do {
			// A resize can repaint earlier output. Unique request IDs distinguish a fresh reply.
			string id = Guid.NewGuid().ToString("N")[..8];
			await Send(process, "size:" + id + "\n");
			observed = await ReadUntil(process.Output, "SIZE:" + id + ":", "\n");
			if (observed.Contains("SIZE:" + id + ":" + expected, StringComparison.Ordinal)) return;
			await Task.Delay(50);
		} while (DateTime.UtcNow < deadline);
		Assert.Fail("Terminal dimensions did not become " + expected + ": " + observed);
	}
	private static async Task<string> ReadToEnd(Stream stream) { using StreamReader reader = new(stream, Encoding.UTF8, false, 1024, true); return await reader.ReadToEndAsync(); }
	private static async Task<string> ReadUntil(Stream stream, string marker, string ending) {
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
		List<byte> bytes = []; byte[] one = new byte[1];
		while (bytes.Count < 100000) {
			if (await stream.ReadAsync(one, timeout.Token) == 0) throw new IOException("Unexpected EOF: " + Encoding.UTF8.GetString(bytes.ToArray()));
			bytes.Add(one[0]);
			string text = Encoding.UTF8.GetString(bytes.ToArray());
			int index = text.IndexOf(marker, StringComparison.Ordinal);
			if (index >= 0 && text.IndexOf(ending, index + marker.Length, StringComparison.Ordinal) >= 0) return StripAnsi(text);
		}
		throw new IOException("Output limit exceeded.");
	}
	private static string StripAnsi(string text) => System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-?]*[ -/]*[@-~]", "");
}
