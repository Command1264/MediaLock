[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Chrome', 'Brave')]
    [string] $Browser,

    [Parameter(DontShow)]
    [string] $RegistryRoot = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts',

    [Parameter(DontShow)]
    [string] $ObsoleteBraveRegistryRoot =
        'HKCU:\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$hostName = 'com.command1264.medialock.phase16a'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactRoot = Join-Path $repositoryRoot 'artifacts\phase16a-browser-direct'
$outputRoot = Join-Path $artifactRoot 'chromium'
$ownedManifestPath = Join-Path $outputRoot 'native-host-manifest.json'
$registryPath = Join-Path $registryRoot $hostName
$obsoleteBraveRegistryPath = Join-Path $ObsoleteBraveRegistryRoot $hostName
$ownedManifestPaths = @(
    $ownedManifestPath,
    (Join-Path $artifactRoot 'chrome\native-host-manifest.json'),
    (Join-Path $artifactRoot 'brave\native-host-manifest.json')
)

$removed = $false
$preservedForeignValue = $false
if (Test-Path -LiteralPath $registryPath) {
    $registeredManifest = [string](Get-Item -LiteralPath $registryPath).GetValue('')
    $isOwnedManifest = $ownedManifestPaths | Where-Object {
        [string]::Equals($registeredManifest, $_, [StringComparison]::Ordinal)
    }
    if ($null -ne $isOwnedManifest) {
        Remove-Item -LiteralPath $registryPath
        $removed = $true
    }
    else {
        $preservedForeignValue = $true
    }
}

$obsoleteBraveRegistrationRemoved = $false
$obsoleteBraveRegistrationPreserved = $false
if (Test-Path -LiteralPath $obsoleteBraveRegistryPath) {
    $obsoleteManifest = [string](Get-Item -LiteralPath $obsoleteBraveRegistryPath).GetValue('')
    $isObsoleteOwnedManifest = $ownedManifestPaths | Where-Object {
        [string]::Equals($obsoleteManifest, $_, [StringComparison]::Ordinal)
    }
    if ($null -ne $isObsoleteOwnedManifest) {
        Remove-Item -LiteralPath $obsoleteBraveRegistryPath
        $obsoleteBraveRegistrationRemoved = $true
    }
    else {
        $obsoleteBraveRegistrationPreserved = $true
    }
}

[pscustomobject]@{
    Browser = $Browser
    RegistryPath = $registryPath
    OwnedRegistrationRemoved = $removed
    ForeignRegistrationPreserved = $preservedForeignValue
    OutputRetained = (Test-Path -LiteralPath $outputRoot)
    SharedChromiumRegistration = $true
    ObsoleteBraveRegistrationRemoved = $obsoleteBraveRegistrationRemoved
    ObsoleteBraveRegistrationPreserved = $obsoleteBraveRegistrationPreserved
}
