# Native ConPTY input limitation

Icod.Pty forwards bytes to Windows ConPTY without parsing VT sequences. ConPTY interprets input into console events; it is not an opaque POSIX terminal byte transport. During this milestone, repeated tests found that fragmenting a CSI cursor-position reply across separate input writes can lose its prefix on hosted Windows x64 and ARM64. The sample cannot recover bytes discarded by the native console path.

## Evidence

[CI run 20](https://github.com/uniblab/Icod.Pty/actions/runs/37191999568) at `9d391c8ba91c09c1685dccd1f1c8e251cca87801` reproduced the loss both directly through `PtyProcess` and through the interactive sample. [Run 21](https://github.com/uniblab/Icod.Pty/actions/runs/37192324484) at `ab421e5409ad2cc0d76e6b10d27613117e312a46` reproduced it with native child read buffers of both 1 and 4096 bytes, across both Windows architectures and all three .NET targets. It is intermittent, not specific to .NET 8 or the sample.

The Windows ARM64 image was `20260924.168.1`; its [published image manifest](https://github.com/actions/runner-images/blob/win11-vs2026-arm64/20260924.168/images/windows/Windows11-VS2026-Arm64-Readme.md) identifies OS version **10.0.26200.9457**, the requested minimum build. The x64 image was Windows Server 2025 (`windows-latest`). This is not solely evidence from an older Windows baseline.

| Input | Expected hex | Observed hex on failing attempts |
| --- | --- | --- |
| `雪`, Up arrow, then `ESC [ 12 ; 34 R`, each byte written separately | `E99BAA1B5B411B5B31323B333452` | `E99BAA1B5B4152` |

The child recorded received bytes in a shared file, independently of screen rendering. Unicode and arrow input survived; the query reply arrived as `R`. Changing rendered acknowledgements and native read-buffer size did not remove the loss. This localizes the observation below the sample's forwarding loop; it does not identify an upstream fix or promise that other fragmented sequences are unaffected.

## Contract and coverage

- The sample forwards every byte it receives, including fragmented Unicode, VT, and query replies. A controlled fragmented-host fixture verifies the actual forwarding loop on all six platforms.
- Real direct and nested PTY tests verify fragmented Unicode/arrow input and a complete query reply on every platform. Send a complete terminal reply in one write when possible; this reduces exposure but is not a universal native transport guarantee.
- The native test that fragments the query itself remains enabled on Unix. On Windows it is explicitly skipped by default because the native behavior above prevents a portable success assertion. It remains available as an opt-in reproducer, and its skip is visible in xUnit results.
- Icod.Pty does not add a VT parser, delay lone Escape keys, synthesize handshake input, or bundle a replacement native ConPTY DLL. Those would require a separate design. Applications that require arbitrary fragmented reply delivery must account for this Windows limitation.

From Windows PowerShell 5.1, run the reproducer on a specific Windows build:

```powershell
$env:ICOD_PTY_VERIFY_SPLIT_QUERIES = '1'
try {
    dotnet test tests/Icod.Pty.Tests/Icod.Pty.Tests.csproj -c Release -f net10.0 --filter FullyQualifiedName~Native_terminal_preserves_split_query_reply
} finally {
    Remove-Item Env:ICOD_PTY_VERIFY_SPLIT_QUERIES
}
```

The diagnostic environment variable affects test discovery only. It has no effect on the library or sample. Record the OS build, architecture, runtime, and full failure trace; five attempts run per direct/nested case. A passing run establishes only those attempts, not absence of the intermittent native limitation.

Microsoft describes the [pseudoconsole's input translation responsibilities](https://learn.microsoft.com/en-us/windows/console/pseudoconsoles). Its [input state machine](https://github.com/microsoft/terminal/blob/main/src/terminal/parser/stateMachine.cpp) also documents the ambiguity between fragmented escape sequences and individual Escape/Alt keystrokes. These explain why native interpretation is part of the transport; the concrete loss above is established by this repository's recorded tests.
