param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $root $ArtifactDirectory }
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$packages = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Filter '*.nupkg' -File)
if ($packages.Count -ne 1) { throw 'The consumer check requires exactly one NuGet package.' }
$metadata = Get-PackageMetadata -PackagePath $packages[0].FullName
if ($metadata.Id -ne 'Icod.Pty') { throw 'Expected the Icod.Pty package.' }

$consumer = Join-Path $root 'artifacts/package-consumer'
if (Test-Path -LiteralPath $consumer) { Remove-Item -LiteralPath $consumer -Recurse -Force }
New-Item -ItemType Directory -Path $consumer -Force | Out-Null
$project = Join-Path $consumer 'Consumer.csproj'
$xml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <LangVersion>13.0</LangVersion>
    <PlatformTarget>AnyCPU</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <IsPackable>false</IsPackable>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Icod.Pty" Version="$($metadata.Version)" />
  </ItemGroup>
</Project>
"@
[System.IO.File]::WriteAllText($project, $xml, [System.Text.UTF8Encoding]::new($false))
$sampleRoot = Join-Path $root 'src/Sample'
foreach ($source in @(Get-ChildItem -LiteralPath $sampleRoot -Filter '*.cs' -Recurse -File)) {
    $relative = $source.FullName.Substring($sampleRoot.Length).TrimStart([char[]]@('\', '/'))
    $destination = Join-Path $consumer $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source.FullName -Destination $destination
}
Invoke-DotNet -Arguments @('restore', $project, '--source', $ArtifactDirectory, '--packages', (Join-Path $consumer 'packages'))
foreach ($framework in @('net8.0', 'net9.0', 'net10.0')) {
    Invoke-DotNet -Arguments @('build', $project, '--framework', $framework, '--configuration', 'Release', '--no-restore')
    foreach ($mode in @('--smoke', '--lifecycle-smoke', '--cancel-start-smoke', '--interrupt-smoke', '--scope-smoke', '--session-smoke', '--session-scope-smoke', '--terminal-config-smoke', '--recording-smoke')) {
        Invoke-DotNet -Arguments @('run', '--project', $project, '--framework', $framework, '--configuration', 'Release', '--no-build', '--no-restore', '--', $mode)
    }
}
$publish = Join-Path $consumer 'publish'
Invoke-DotNet -Arguments @('publish', $project, '--framework', 'net10.0', '--configuration', 'Release', '--no-restore', '--output', $publish)
foreach ($name in @('Icod.Pty.Host.dll', 'Icod.Pty.Host.deps.json', 'Icod.Pty.Host.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish "Icod.Pty.Host/$name") -PathType Leaf)) { throw "Published helper asset is missing: $name" }
}
foreach ($mode in @('--smoke', '--lifecycle-smoke', '--cancel-start-smoke', '--interrupt-smoke', '--scope-smoke', '--session-smoke', '--session-scope-smoke', '--terminal-config-smoke', '--recording-smoke')) {
    Invoke-DotNet -Arguments @((Join-Path $publish 'Consumer.dll'), $mode)
}
Write-Host 'Package consumer and publish verification passed.'
