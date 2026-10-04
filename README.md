# Icod.Pty

Icod.Pty is a C# pseudoterminal library for .NET 8, 9, and 10. The planned public API starts a child process attached to a PTY, exchanges bytes with it, resizes its terminal, waits for exit, and disposes the session.

Windows support starts at OS version `10.0.26200.9457`; earlier Windows builds are outside the support contract. Linux and macOS support both x64 and ARM64.

This branch establishes the package and six-platform build foundation. The native PTY implementations and public API are under development; this version is not ready for publication.

## Build

Run `build.cmd` on Windows (CMD and Windows PowerShell 5.1) or `./build.sh` on Unix-like systems (SH and PowerShell). Tooling uses CMD, SH, and PowerShell 5.1-compatible scripts; production code is C# 13. No C or Python source or build step is required. The scripts run clean, restore, build, test, pack, and package validation. The library project and solution are in the root; production C# files belong in `src/`.

Pull requests and pushes to `main` validate Windows x64/ARM64, Linux x64/ARM64, and macOS x64/ARM64. A `v<semver>` tag on `main` publishes the matching DLL package to NuGet.org and GitHub Packages through `.github/workflows/release.yaml`. A release tag must match the package version in `Icod.Pty.csproj`.

## License

Copyright (c) 2026 Timothy J. Bruce <uniblab@hotmail.com>. Licensed under the GNU Lesser General Public License version 3 or later; see [LICENSE](LICENSE).
