param(
	[string]$ArtifactDirectory = '',

	[ValidateSet('', 'net8.0', 'net9.0', 'net10.0')]
	[string]$Framework = '',

	[ValidateSet('', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
	[string]$RuntimeIdentifier = '',

	[ValidateSet('', 'FrameworkDependent', 'SelfContained', 'SingleFile', 'Trimmed')]
	[string]$Mode = '',

	[int]$TimeoutSeconds = 60,

	[switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-PortableRequest {
	param([string]$FrameworkValue, [string]$RuntimeValue, [string]$ModeValue)
	if ($FrameworkValue -notin @('net8.0', 'net9.0', 'net10.0')) { throw "Unsupported target framework '$FrameworkValue'." }
	if ($RuntimeValue -notin @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')) { throw "Unsupported runtime identifier '$RuntimeValue'." }
	if ($ModeValue -notin @('FrameworkDependent', 'SelfContained', 'SingleFile', 'Trimmed')) { throw "Unsupported deployment mode '$ModeValue'." }
}

function Get-PublishArguments {
	param([string]$Project, [string]$FrameworkValue, [string]$RuntimeValue, [string]$ModeValue, [string]$Output)
	$result = @('publish', $Project, '--framework', $FrameworkValue, '--configuration', 'Release', '--no-restore', '--runtime', $RuntimeValue, '--output', $Output)
	if ($ModeValue -eq 'FrameworkDependent') { return $result + @('--self-contained', 'false') }
	$result += @('--self-contained', 'true')
	if ($ModeValue -eq 'SingleFile') { $result += '-p:PublishSingleFile=true' }
	if ($ModeValue -eq 'Trimmed') { $result += '-p:PublishTrimmed=true' }
	return $result
}

function Assert-ConsumerProject {
	param([string]$Project, [string]$ExpectedVersion)
	$text = [System.IO.File]::ReadAllText($Project)
	if ($text.IndexOf('<ProjectReference', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { throw 'Portable consumer must not contain a ProjectReference.' }
	$expected = "<PackageReference Include=`"Icod.Pty`" Version=`"$ExpectedVersion`" />"
	if ($text.IndexOf($expected, [System.StringComparison]::Ordinal) -lt 0) { throw 'Portable consumer does not reference the exact packed Icod.Pty version.' }
}

function Get-PublishedExecutable {
	param([string]$PublishDirectory, [string]$RuntimeValue)
	$name = if ($RuntimeValue.StartsWith('win-', [System.StringComparison]::Ordinal)) { 'Consumer.exe' } else { 'Consumer' }
	$path = Join-Path $PublishDirectory $name
	if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Published apphost is missing: $name" }
	return $path
}

function Assert-HelperLayout {
	param([string]$PublishDirectory)
	foreach ($name in @('Icod.Pty.Host.dll', 'Icod.Pty.Host.deps.json', 'Icod.Pty.Host.runtimeconfig.json')) {
		$path = Join-Path $PublishDirectory (Join-Path 'Icod.Pty.Host' $name)
		if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Published helper asset is missing: $name" }
	}
}

function Invoke-PublishedMode {
	param([string]$Executable, [string]$SmokeMode, [string]$WorkingDirectory, [int]$Timeout)
	$token = [Guid]::NewGuid().ToString('N')
	$stdout = Join-Path $WorkingDirectory "$token.out"
	$stderr = Join-Path $WorkingDirectory "$token.err"
	$process = Start-Process -FilePath $Executable -ArgumentList @($SmokeMode) -WorkingDirectory $WorkingDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
	try {
		if (-not $process.WaitForExit($Timeout * 1000)) {
			try { $process.Kill() } catch { }
			throw "Published consumer timed out in mode $SmokeMode after $Timeout seconds."
		}
		$outputText = if (Test-Path -LiteralPath $stdout) { [System.IO.File]::ReadAllText($stdout) } else { '' }
		$errorText = if (Test-Path -LiteralPath $stderr) { [System.IO.File]::ReadAllText($stderr) } else { '' }
		if ($process.ExitCode -ne 0) { throw "Published consumer mode $SmokeMode exited $($process.ExitCode): $errorText$outputText" }
		if ($outputText.IndexOf('passed.', [System.StringComparison]::Ordinal) -lt 0) { throw "Published consumer mode $SmokeMode did not report success: $outputText$errorText" }
	} finally {
		$process.Dispose()
		Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
	}
}

function Invoke-SelfTest {
	Assert-PortableRequest 'net10.0' 'linux-x64' 'SingleFile'
	try { Assert-PortableRequest 'net10.0' 'linux-musl-x64' 'SingleFile'; throw 'Invalid RID was accepted.' } catch { if ($_.Exception.Message -eq 'Invalid RID was accepted.') { throw } }
	$root = Join-Path ([System.IO.Path]::GetTempPath()) ('icod-pty-portable-selftest-' + [Guid]::NewGuid().ToString('N'))
	New-Item -ItemType Directory -Path $root -Force | Out-Null
	try {
		$project = Join-Path $root 'Consumer.csproj'
		[System.IO.File]::WriteAllText($project, '<Project><ItemGroup><ProjectReference Include="Icod.Pty.csproj" /></ItemGroup></Project>')
		try { Assert-ConsumerProject $project '0.1.0'; throw 'ProjectReference was accepted.' } catch { if ($_.Exception.Message -eq 'ProjectReference was accepted.') { throw } }
		[System.IO.File]::WriteAllText($project, '<Project><ItemGroup><PackageReference Include="Icod.Pty" Version="0.1.0" /></ItemGroup></Project>')
		Assert-ConsumerProject $project '0.1.0'
		[System.IO.File]::WriteAllText((Join-Path $root 'Consumer.dll'), '')
		try { Get-PublishedExecutable $root 'linux-x64' | Out-Null; throw 'Consumer.dll was accepted as the final apphost.' } catch { if ($_.Exception.Message -eq 'Consumer.dll was accepted as the final apphost.') { throw } }
		[System.IO.File]::WriteAllText((Join-Path $root 'Consumer'), '')
		if ((Get-PublishedExecutable $root 'linux-x64') -ne (Join-Path $root 'Consumer')) { throw 'Final apphost resolution returned the wrong path.' }
		$arguments = Get-PublishArguments 'Consumer.csproj' 'net10.0' 'linux-x64' 'SingleFile' 'publish'
		if ('-p:PublishSingleFile=true' -notin $arguments -or '--self-contained' -notin $arguments) { throw 'Single-file publish arguments are incomplete.' }
	} finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($SelfTest) {
	Invoke-SelfTest
	return
}

Assert-PortableRequest $Framework $RuntimeIdentifier $Mode
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory }
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$packages = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Filter '*.nupkg' -File | Where-Object { -not $_.Name.EndsWith('.symbols.nupkg', [System.StringComparison]::OrdinalIgnoreCase) })
if ($packages.Count -ne 1) { throw 'Portable consumer verification requires exactly one NuGet package.' }
$metadata = Get-PackageMetadata -PackagePath $packages[0].FullName
if ($metadata.Id -ne 'Icod.Pty') { throw "Expected Icod.Pty; found $($metadata.Id)." }

$consumerRoot = Join-Path $repositoryRoot (Join-Path 'artifacts/portable-consumer' (Join-Path $Framework (Join-Path $RuntimeIdentifier $Mode)))
if (Test-Path -LiteralPath $consumerRoot) { Remove-Item -LiteralPath $consumerRoot -Recurse -Force }
$packagesRoot = Join-Path $consumerRoot 'packages'
$publishRoot = Join-Path $consumerRoot 'publish'
New-Item -ItemType Directory -Path $consumerRoot -Force | Out-Null
$nugetConfig = Join-Path $consumerRoot 'NuGet.Config'
$escapedArtifactDirectory = [System.Security.SecurityElement]::Escape($ArtifactDirectory)
$nugetXml = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-artifacts" value="$escapedArtifactDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
[System.IO.File]::WriteAllText($nugetConfig, $nugetXml, [System.Text.UTF8Encoding]::new($false))
$project = Join-Path $consumerRoot 'Consumer.csproj'
$xml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$Framework</TargetFramework>
    <LangVersion>13.0</LangVersion>
    <PlatformTarget>AnyCPU</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Icod.Pty" Version="$($metadata.Version)" />
  </ItemGroup>
</Project>
"@
[System.IO.File]::WriteAllText($project, $xml, [System.Text.UTF8Encoding]::new($false))
Assert-ConsumerProject $project $metadata.Version
$sampleRoot = Join-Path $repositoryRoot 'src/Sample'
foreach ($source in @(Get-ChildItem -LiteralPath $sampleRoot -Filter '*.cs' -Recurse -File)) {
	$relative = $source.FullName.Substring($sampleRoot.Length).TrimStart([char[]]@('\', '/'))
	$destination = Join-Path $consumerRoot $relative
	New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
	Copy-Item -LiteralPath $source.FullName -Destination $destination
}

Invoke-DotNet -Arguments @('restore', $project, '--configfile', $nugetConfig, '--packages', $packagesRoot, '--runtime', $RuntimeIdentifier)
$restoredPackage = Join-Path $packagesRoot (Join-Path 'icod.pty' (Join-Path $metadata.Version "icod.pty.$($metadata.Version).nupkg"))
if (-not (Test-Path -LiteralPath $restoredPackage -PathType Leaf)) { throw 'Restored Icod.Pty package is missing.' }
$artifactHash = (Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash
$restoredHash = (Get-FileHash -LiteralPath $restoredPackage -Algorithm SHA256).Hash
if ($artifactHash -ne $restoredHash) { throw 'Restored Icod.Pty package does not match the requested artifact.' }
Invoke-DotNet -Arguments (Get-PublishArguments $project $Framework $RuntimeIdentifier $Mode $publishRoot)
Assert-HelperLayout $publishRoot
$executable = Get-PublishedExecutable $publishRoot $RuntimeIdentifier
$smokeModes = @('--smoke', '--lifecycle-smoke', '--cancel-start-smoke', '--interrupt-smoke', '--scope-smoke', '--session-smoke', '--session-scope-smoke', '--terminal-config-smoke')
foreach ($smokeMode in $smokeModes) { Invoke-PublishedMode $executable $smokeMode $publishRoot $TimeoutSeconds }
$result = [ordered]@{ package = "$($metadata.Id) $($metadata.Version)"; framework = $Framework; runtimeIdentifier = $RuntimeIdentifier; mode = $Mode; executable = $executable; smokeModes = $smokeModes.Count }
Write-Host ('PORTABILITY-RESULT ' + ($result | ConvertTo-Json -Compress))
