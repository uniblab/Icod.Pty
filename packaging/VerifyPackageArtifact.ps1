param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [ValidateSet('Debug', 'Staging', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ExpectedVersion = '',

    [switch]$AllowNoPackages,

    [string]$GitHubOutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force

if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory '$ArtifactDirectory' does not exist."
}

$packages = @(
    Get-ChildItem -LiteralPath $ArtifactDirectory -Filter '*.nupkg' -File |
        Where-Object { -not $_.Name.EndsWith('.symbols.nupkg', [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object Name
)

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $packages = @(
        $packages |
            Where-Object {
                (Get-PackageMetadata -PackagePath $_.FullName).Version -eq $ExpectedVersion
            }
    )
}

if (0 -eq $packages.Count -and -not $AllowNoPackages) {
    $suffix = if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { '' } else { " with version '$ExpectedVersion'" }
    throw "No NuGet packages$suffix were found in '$ArtifactDirectory'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
if ($packages.Count -ne 1) { throw 'Icod.Pty requires exactly one NuGet DLL package.' }
foreach ($package in $packages) {
    $metadata = Get-PackageMetadata -PackagePath $package.FullName
    if ($metadata.Id -ne 'Icod.Pty') { throw "Unexpected package: $($metadata.Id)" }
    if ($metadata.Authors -ne 'Timothy J. Bruce') { throw "Unexpected package authors: $($metadata.Authors)" }
    if ($metadata.Description -ne 'Cross-platform pseudoterminal process hosting for .NET.') { throw "Unexpected package description: $($metadata.Description)" }
    if ($metadata.ProjectUrl -ne 'https://github.com/uniblab/Icod.Pty') { throw "Unexpected package project URL: $($metadata.ProjectUrl)" }
    if ($metadata.RepositoryUrl -ne 'https://github.com/uniblab/Icod.Pty') { throw "Unexpected package repository URL: $($metadata.RepositoryUrl)" }
    if ($metadata.RepositoryType -ne 'git') { throw "Unexpected package repository type: $($metadata.RepositoryType)" }
    if ($metadata.LicenseExpression -ne 'LGPL-3.0-or-later') { throw "Unexpected package license: $($metadata.LicenseExpression)" }
    if (-not $metadata.RequireLicenseAcceptance) { throw 'Package must require license acceptance.' }
    if ($metadata.Readme -ne 'README.md') { throw "Unexpected package readme: $($metadata.Readme)" }
    if ([string]::IsNullOrWhiteSpace($metadata.ReleaseNotes)) { throw 'Package release notes are empty.' }
    $tags = @($metadata.Tags -split '[\s;,]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($tag in @('pty', 'pseudoterminal', 'conpty', 'terminal', 'process', 'cross-platform')) {
        if ($tag -notin $tags) { throw "Required package tag missing: $tag" }
    }
    Write-Host "Verifying $($metadata.Id) $($metadata.Version): $($package.FullName)"

    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $required = @('CHANGELOG.md', 'LICENSE', 'README.md', 'buildTransitive/Icod.Pty.targets',
            'tools/net8.0/Icod.Pty.Host.dll', 'tools/net8.0/Icod.Pty.Host.deps.json',
            'tools/net8.0/Icod.Pty.Host.runtimeconfig.json')
        foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
            $required += "lib/$framework/Icod.Pty.dll"
            $required += "lib/$framework/Icod.Pty.xml"
        }
        foreach ($entry in $required) {
            if ($null -eq $archive.GetEntry($entry)) { throw "Required package asset missing: $entry" }
        }
        if (0 -eq $archive.Entries.Count) {
            throw "Package '$($package.FullName)' is empty."
        }

        if (-not [string]::IsNullOrWhiteSpace($metadata.Readme)) {
            $readmeEntry = $archive.Entries |
                Where-Object { $_.FullName -eq $metadata.Readme } |
                Select-Object -First 1
            if ($null -eq $readmeEntry) {
                throw "Package '$($package.FullName)' declares missing readme '$($metadata.Readme)'."
            }
        }

        $toolSettings = @(
            $archive.Entries |
                Where-Object { $_.FullName.EndsWith('/DotnetToolSettings.xml', [System.StringComparison]::OrdinalIgnoreCase) }
        )
        if (1 -lt $toolSettings.Count) {
            throw "Package '$($package.FullName)' contains multiple DotnetToolSettings.xml files."
        }
    } finally {
        $archive.Dispose()
    }
}

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    "package_count=$($packages.Count)" >> $GitHubOutputPath
    "has_packages=$((0 -lt $packages.Count).ToString().ToLowerInvariant())" >> $GitHubOutputPath
}

Write-Host "Exact package verification completed successfully for $($packages.Count) package(s) ($Configuration)."
