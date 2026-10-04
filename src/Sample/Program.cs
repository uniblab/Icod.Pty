using System.Text;
using Icod.Pty;

bool smoke = args.Length == 1 && args[0] == "--smoke";
string executable = args.Length > 0 && !smoke ? args[0] :
	OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh";
PtyStartInfo start = new(executable) { Size = new PtySize(100, 30) };
if (smoke) {
	start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
	start.ArgumentList.Add("echo ICOD-PTY-SMOKE");
} else {
	foreach (string argument in args.Skip(1)) start.ArgumentList.Add(argument);
}
await using PtyProcess process = PtyProcess.Start(start);
if (smoke) {
	using StreamReader reader = new(process.Output, Encoding.UTF8, false, 4096, true);
	Task<string> output = reader.ReadToEndAsync();
	int code = await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
	string text = await output.WaitAsync(TimeSpan.FromSeconds(30));
	if (code != 0 || !text.Contains("ICOD-PTY-SMOKE", StringComparison.Ordinal)) throw new IOException("PTY package smoke check failed: " + text);
	Console.WriteLine("PTY package smoke check passed.");
	return 0;
}
// Line-oriented sample. A terminal emulator should forward raw input and render VT output.
Task drain = process.Output.CopyToAsync(Console.OpenStandardOutput());
_ = Task.Run(async () => {
	while (Console.ReadLine() is string line) await process.Input.WriteAsync(Encoding.UTF8.GetBytes(line + (OperatingSystem.IsWindows() ? "\r" : "\n")));
});
int exit = await process.WaitForExitAsync();
await drain;
return exit;
