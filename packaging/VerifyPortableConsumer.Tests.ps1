$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$subject = Join-Path $PSScriptRoot 'VerifyPortableConsumer.ps1'
if (-not (Test-Path -LiteralPath $subject -PathType Leaf)) {
	throw 'VerifyPortableConsumer.ps1 has not been implemented.'
}

. $subject -SelfTest

$selfContained = Get-PublishArguments 'Consumer.csproj' 'net10.0' 'linux-x64' 'SelfContained' 'publish'
if ('--self-contained' -notin $selfContained -or 'true' -notin $selfContained -or '-p:PublishSingleFile=true' -in $selfContained -or '-p:PublishTrimmed=true' -in $selfContained) {
	throw 'Self-contained publish arguments are incorrect.'
}
$trimmed = Get-PublishArguments 'Consumer.csproj' 'net10.0' 'linux-x64' 'Trimmed' 'publish'
if ('-p:PublishTrimmed=true' -notin $trimmed -or 'true' -notin $trimmed) { throw 'Trimmed publish arguments are incomplete.' }
$nativeAot = Get-PublishArguments 'Consumer.csproj' 'net10.0' 'linux-x64' 'NativeAot' 'publish'
if ('-p:PublishAot=true' -notin $nativeAot -or 'true' -notin $nativeAot) { throw 'NativeAOT publish arguments are incomplete.' }

$root = Join-Path ([System.IO.Path]::GetTempPath()) ('icod-pty-layout-selftest-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $root 'publish'
$helper = Join-Path $publish 'Icod.Pty.Host'
New-Item -ItemType Directory -Path $helper -Force | Out-Null
try {
	foreach ($name in @('Icod.Pty.Host.dll', 'Icod.Pty.Host.deps.json', 'Icod.Pty.Host.runtimeconfig.json')) {
		[System.IO.File]::WriteAllText((Join-Path $helper $name), $name)
	}
	[System.IO.File]::WriteAllText((Join-Path $publish 'Consumer'), 'consumer')
	$layout = New-PortableExecutionLayout -PublishDirectory $publish -Scenario Relocated
	if (-not (Test-Path -LiteralPath (Join-Path $layout.Directory 'Consumer') -PathType Leaf)) { throw 'Relocated apphost is missing.' }
	if (Test-Path -LiteralPath $publish) { throw 'Relocation retained the original publish directory.' }
	Assert-HelperLayout $layout.Directory

	$publish = Join-Path $root 'publish-missing'
	$helper = Join-Path $publish 'Icod.Pty.Host'
	New-Item -ItemType Directory -Path $helper -Force | Out-Null
	foreach ($name in @('Icod.Pty.Host.dll', 'Icod.Pty.Host.deps.json', 'Icod.Pty.Host.runtimeconfig.json')) {
		[System.IO.File]::WriteAllText((Join-Path $helper $name), $name)
	}
	[System.IO.File]::WriteAllText((Join-Path $publish 'Consumer'), 'consumer')
	$layout = New-PortableExecutionLayout -PublishDirectory $publish -Scenario MissingHelperRuntimeConfig
	try { Assert-HelperLayout $layout.Directory; throw 'Incomplete helper layout was accepted.' }
	catch { if ($_.Exception.Message -eq 'Incomplete helper layout was accepted.') { throw } }
} finally {
	Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'Portable consumer harness self-test passed.'
