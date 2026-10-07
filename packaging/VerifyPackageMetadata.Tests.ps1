$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        $Expected,

        [Parameter(Mandatory = $true)]
        $Actual,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Expected -ne $Actual) {
        throw "Expected $Name '$Expected', but found '$Actual'."
    }
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) ('icod-pty-metadata-selftest-' + [Guid]::NewGuid().ToString('N'))
$packagePath = Join-Path $root 'fixture.1.2.3.nupkg'
$failure = $null

try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $archive = [System.IO.Compression.ZipFile]::Open($packagePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('fixture.nuspec')
        $writer = [System.IO.StreamWriter]::new($entry.Open())
        try {
            $writer.Write(@'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Icod.Pty</id>
    <version>1.2.3</version>
    <authors>Timothy J. Bruce</authors>
    <description>Cross-platform pseudoterminal process hosting for .NET.</description>
    <projectUrl>https://github.com/uniblab/Icod.Pty</projectUrl>
    <repository type="git" url="https://github.com/uniblab/Icod.Pty" />
    <license type="expression">LGPL-3.0-or-later</license>
    <requireLicenseAcceptance>true</requireLicenseAcceptance>
    <readme>docs\README.md</readme>
    <releaseNotes>See CHANGELOG.md for unreleased changes.</releaseNotes>
    <tags>pty pseudoterminal conpty terminal process cross-platform</tags>
  </metadata>
</package>
'@)
        } finally {
            $writer.Dispose()
        }
    } finally {
        $archive.Dispose()
    }

    $metadata = Get-PackageMetadata -PackagePath $packagePath
    Assert-Equal 'Icod.Pty' $metadata.Id 'ID'
    Assert-Equal '1.2.3' $metadata.Version 'version'
    Assert-Equal 'Timothy J. Bruce' $metadata.Authors 'authors'
    Assert-Equal 'Cross-platform pseudoterminal process hosting for .NET.' $metadata.Description 'description'
    Assert-Equal 'https://github.com/uniblab/Icod.Pty' $metadata.ProjectUrl 'project URL'
    Assert-Equal 'https://github.com/uniblab/Icod.Pty' $metadata.RepositoryUrl 'repository URL'
    Assert-Equal 'git' $metadata.RepositoryType 'repository type'
    Assert-Equal 'LGPL-3.0-or-later' $metadata.LicenseExpression 'license expression'
    Assert-Equal $true $metadata.RequireLicenseAcceptance 'license acceptance'
    Assert-Equal 'docs/README.md' $metadata.Readme 'normalized readme path'
    Assert-Equal 'See CHANGELOG.md for unreleased changes.' $metadata.ReleaseNotes 'release notes'
    Assert-Equal 'pty pseudoterminal conpty terminal process cross-platform' $metadata.Tags 'tags'
} catch {
    $failure = $_
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $root) {
    throw "Metadata self-test did not clean temporary path '$root'."
}
if ($null -ne $failure) {
    throw $failure
}

Write-Host 'Package metadata parser self-test passed.'
