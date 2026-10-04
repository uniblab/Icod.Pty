# C#/.NET build and packaging workflow

This directory is the shared implementation behind the repository template's local build scripts and GitHub Actions workflows.

The design is intentionally optimized for C#/.NET repositories. It assumes:

- one root `.sln` or `.slnx` file once development begins;
- one or more C# projects (`.csproj`) in that solution;
- .NET 10 as the default SDK/runtime generation;
- NuGet for package publication; and
- optional executable projects that may also be distributed as RID-specific ZIP archives.

Repository and package names are not inferred from the GitHub repository name. Build inputs are discovered from the repository, and package identity/version metadata is read from the generated NuGet package itself or from MSBuild.

## Validation ladder

| Lifecycle | Configuration | Work |
| --- | --- | --- |
| local `build.cmd` / `build.sh` | `Debug` | clean, restore, build, test, pack, exact package validation |
| pull request | `Staging` | Windows/Linux/macOS build and test; Linux also validates generated NuGet artifacts |
| default branch | `Release` | six-runner Windows/Linux/macOS x64/ARM64 distribution validation |
| `v<semver>` tag | `Release` | package/archive production and publication |

## Scripts

### `RepositoryTools.psm1`

Shared helpers for:

- locating exactly one root solution;
- listing C# projects in the solution;
- reading MSBuild properties;
- discovering executable projects from `OutputType`; and
- reading package identity, version, and readme metadata from `.nupkg` files.

### `Get-RepositoryMetadata.ps1`

Returns repository-level build metadata and can write GitHub Actions outputs:

```text
has_solution
solution_path
has_executables
```

A repository created from the template may initially have no solution. PR/default-branch workflows treat that as a valid bootstrap state and skip compilation. Tagged releases require a solution.

### `Invoke-Build.ps1`

Implements the local build contract used by both `build.cmd` and `build.sh`.

The default invocation performs:

```text
clean → restore → build → test → pack → validate
```

with the `Debug` configuration. Individual stages can also be requested.

### `VerifyDistribution.ps1`

Performs authoritative source-tree validation:

1. restore;
2. build;
3. test;
4. pack without rebuilding; and
5. verify the exact generated NuGet artifacts.

This is the implementation used by the six-platform `main` and manual distribution-validation workflows.

### `VerifyPackageArtifact.ps1`

Validates already-produced `.nupkg` files. It opens the exact artifacts supplied by the caller, verifies nuspec identity/version metadata, checks declared readme presence, and checks .NET tool metadata shape when applicable.

`-ExpectedVersion` restricts validation to packages matching a tagged release version. `-AllowNoPackages` is used for solutions that intentionally contain no packable projects.

### `SelectReleasePackages.ps1`

Filters all packages produced by a solution pack and copies only packages whose nuspec version equals the `v<semver>` release tag.

This is important for solutions that contain independently versioned packages. A suite release therefore cannot accidentally publish an unrelated package merely because that project is present in the same solution.

### `BuildReleaseArchive.ps1`

Discovers all `Exe`/`WinExe` C# projects through MSBuild and produces one framework-dependent single-file ZIP for a requested RID.

Default automated release RIDs are:

```text
win-x64
win-arm64
linux-x64
linux-arm64
osx-x64
osx-arm64
```

Library-only repositories skip this release path automatically.

## Tagged release graph

After tag/default-branch validation, package and executable archive production run independently:

```text
metadata
  ├── package
  │     ├── publish-nuget
  │     └── publish-github-packages
  └── archives (optional, 6 RIDs)

publish-nuget ────────────────┐
publish-github-packages ──────┼── github-release
archives ─────────────────────┘
```

NuGet.org and GitHub Packages deliberately publish in parallel. Both consume the same validated package artifact. A partial registry publication is recoverable because both pushes use `--skip-duplicate`; GitHub Release creation still requires all applicable registry and archive jobs to succeed.

## Release prerequisites

Each repository that publishes to NuGet.org needs:

- a GitHub environment named `Release`;
- an Actions secret `NUGET_USER`; and
- a NuGet.org Trusted Publishing policy for `release.yaml` and environment `Release`.

GitHub Packages and GitHub Release use `GITHUB_TOKEN` with job-scoped permissions.

## Version contract

A release tag must match:

```text
vMAJOR.MINOR.PATCH
vMAJOR.MINOR.PATCH-prerelease
```

Only NuGet packages whose actual nuspec version equals the tag version are selected for publication. The template does not require one particular MSBuild version-property name; each repository remains free to centralize versioning in `Directory.Build.props`, project files, or another MSBuild mechanism.

## Customization points

Most repositories should only need to adjust:

- `DOTNET_VERSION` in the workflows if moving SDK generations;
- the RID matrix if platform support differs;
- package/project metadata in MSBuild; and
- product-specific smoke tests if deeper behavioral verification is required.

Do not derive solution/project/package paths from `${{ github.event.repository.name }}` simply because names happen to match. Repository layout and GitHub metadata are separate contracts.
