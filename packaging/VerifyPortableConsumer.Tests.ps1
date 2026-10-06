$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$subject = Join-Path $PSScriptRoot 'VerifyPortableConsumer.ps1'
if (-not (Test-Path -LiteralPath $subject -PathType Leaf)) {
	throw 'VerifyPortableConsumer.ps1 has not been implemented.'
}

& $subject -SelfTest
Write-Host 'Portable consumer harness self-test passed.'
