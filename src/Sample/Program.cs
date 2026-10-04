using System.Text;
using Icod.Pty;
using Icod.Pty.Sample;

try {
	if (args is ["--lifecycle-smoke"]) return await PackageSmokeChecks.RunLifecycleAsync();
	if (args is ["--cancel-start-smoke"]) return await PackageSmokeChecks.RunCancelledStartAsync();
	if (args is ["--interrupt-smoke"]) return await PackageSmokeChecks.RunInterruptAsync();
	if (args is ["--interrupt-child"]) return await PackageSmokeChecks.RunInterruptChildAsync();
	bool smoke = args is ["--smoke"];
	bool line = args.Length > 0 && args[0] == "--line";
	int first = args.Length > 0 && args[0] is "--line" or "--interactive" ? 1 : 0;
	if (first < args.Length && args[first] == "--") first++;
	string executable = !smoke && first < args.Length ? args[first++] :
		OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh";
	PtyStartInfo start = new(executable) { Size = new PtySize(100, 30) };
	if (smoke) {
		start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
		start.ArgumentList.Add("echo ICOD-PTY-SMOKE");
		await using PtyProcess process = await PtyProcess.StartAsync(start);
		using StreamReader reader = new(process.Output, Encoding.UTF8, false, 4096, true);
		Task<string> output = reader.ReadToEndAsync();
		int code = await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
		string text = await output.WaitAsync(TimeSpan.FromSeconds(30));
		if (code != 0 || !text.Contains("ICOD-PTY-SMOKE", StringComparison.Ordinal)) throw new IOException("PTY package smoke check failed: " + text);
		Console.WriteLine("PTY package smoke check passed."); return 0;
	}
	for (int i = first; i < args.Length; i++) start.ArgumentList.Add(args[i]);
	if (line) {
		await using PtyProcess process = await PtyProcess.StartAsync(start);
		Task drain = process.Output.CopyToAsync(Console.OpenStandardOutput());
		_ = Task.Run(async () => {
			try { while (Console.ReadLine() is string value) await process.Input.WriteAsync(Encoding.UTF8.GetBytes(value + (OperatingSystem.IsWindows() ? "\r" : "\n"))); }
			catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
		});
		int code = await process.WaitForExitAsync(); await drain.WaitAsync(TimeSpan.FromSeconds(5)); return code;
	}
	using HostConsole console = HostConsole.Open();
	return await InteractiveSession.RunAsync(start, console, CancellationToken.None);
} catch (Exception error) {
	await HostConsole.ReportErrorAsync("Icod.Pty.Sample: " + error.Message);
	return 1;
}
