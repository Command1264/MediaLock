[CmdletBinding()]
param(
    [Parameter(DontShow)]
    [string] $RegistryRoot = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts',

    [Parameter(DontShow)]
    [string] $ArtifactRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$hostName = 'com.command1264.medialock.browser'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repositoryRoot 'artifacts\browser-integration-candidate'
}
$ownedManifest = Join-Path ([System.IO.Path]::GetFullPath($ArtifactRoot)) 'native-host-manifest.json'
$registryPath = Join-Path $RegistryRoot $hostName
$removed = $false
$foreignPreserved = $false
if (Test-Path -LiteralPath $registryPath) {
    $existing = [string](Get-Item -LiteralPath $registryPath).GetValue('')
    if ([string]::Equals($existing, $ownedManifest, [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $registryPath
        $removed = $true
    }
    else {
        $foreignPreserved = $true
    }
}

[pscustomobject]@{
    RegistryPath = $registryPath
    OwnedRegistrationRemoved = $removed
    ForeignRegistrationPreserved = $foreignPreserved
    ArtifactRetained = (Test-Path -LiteralPath $ArtifactRoot)
    SharedChromiumRegistration = $true
}
