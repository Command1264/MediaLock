[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Chrome', 'Brave')]
    [string] $Browser
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$hostName = 'com.command1264.medialock.phase16a'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputRoot = Join-Path $repositoryRoot "artifacts\phase16a-browser-direct\$($Browser.ToLowerInvariant())"
$ownedManifestPath = Join-Path $outputRoot 'native-host-manifest.json'
$registryRoot = switch ($Browser) {
    'Chrome' { 'HKCU:\Software\Google\Chrome\NativeMessagingHosts' }
    'Brave' { 'HKCU:\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts' }
}
$registryPath = Join-Path $registryRoot $hostName

$removed = $false
$preservedForeignValue = $false
if (Test-Path -LiteralPath $registryPath) {
    $registeredManifest = [string](Get-Item -LiteralPath $registryPath).GetValue('')
    if ([string]::Equals($registeredManifest, $ownedManifestPath, [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $registryPath
        $removed = $true
    }
    else {
        $preservedForeignValue = $true
    }
}

[pscustomobject]@{
    Browser = $Browser
    RegistryPath = $registryPath
    OwnedRegistrationRemoved = $removed
    ForeignRegistrationPreserved = $preservedForeignValue
    OutputRetained = (Test-Path -LiteralPath $outputRoot)
}
