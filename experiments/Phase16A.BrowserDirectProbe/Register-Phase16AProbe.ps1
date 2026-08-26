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
$extensionId = 'kggfkkiifnclhhmibdglkbdfbacakemn'
$projectPath = Join-Path $PSScriptRoot 'Phase16A.BrowserDirectProbe.csproj'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactRoot = Join-Path $repositoryRoot 'artifacts\phase16a-browser-direct'
$outputRoot = Join-Path $artifactRoot 'chromium'
$publishRoot = Join-Path $outputRoot 'native-host'
$manifestPath = Join-Path $outputRoot 'native-host-manifest.json'
$registryPath = Join-Path $registryRoot $hostName
$obsoleteBraveRegistryPath = Join-Path $ObsoleteBraveRegistryRoot $hostName
$legacyOwnedManifests = @(
    (Join-Path $artifactRoot 'chrome\native-host-manifest.json'),
    (Join-Path $artifactRoot 'brave\native-host-manifest.json')
)
$legacyRegistrationMigrated = $false

if (Test-Path -LiteralPath $registryPath) {
    $existingManifest = [string](Get-Item -LiteralPath $registryPath).GetValue('')
    if (![string]::Equals($existingManifest, $manifestPath, [StringComparison]::Ordinal)) {
        $isLegacyOwnedManifest = $legacyOwnedManifests | Where-Object {
            [string]::Equals($existingManifest, $_, [StringComparison]::Ordinal)
        }
        if ($null -eq $isLegacyOwnedManifest) {
            throw "The Native Messaging host name is already registered to a different manifest: $existingManifest"
        }

        $legacyRegistrationMigrated = $true
    }
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $publishRoot |
    Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Phase 16A Native Host publish failed with exit code $LASTEXITCODE."
}

$hostExecutable = (Resolve-Path (Join-Path $publishRoot 'MediaLock.Phase16ABrowserDirectProbe.exe')).Path
$manifest = [ordered]@{
    name = $hostName
    description = 'Media Lock Phase 16A disposable Native Messaging probe'
    path = $hostExecutable
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}

$null = New-Item -ItemType Directory -Path $outputRoot -Force
[System.IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 4) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

$null = New-Item -Path $registryPath -Force
Set-Item -LiteralPath $registryPath -Value $manifestPath

$registeredManifest = [string](Get-Item -LiteralPath $registryPath).GetValue('')
if (![string]::Equals($registeredManifest, $manifestPath, [StringComparison]::Ordinal)) {
    throw 'The Native Messaging registry value did not round-trip exactly.'
}

$obsoleteBraveRegistrationRemoved = $false
$obsoleteBraveRegistrationPreserved = $false
if (Test-Path -LiteralPath $obsoleteBraveRegistryPath) {
    $obsoleteManifest = [string](Get-Item -LiteralPath $obsoleteBraveRegistryPath).GetValue('')
    $obsoleteOwnedManifests = @($manifestPath) + $legacyOwnedManifests
    $isObsoleteOwnedManifest = $obsoleteOwnedManifests | Where-Object {
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
    ExtensionId = $extensionId
    ExtensionRoot = (Resolve-Path (Join-Path $PSScriptRoot 'extension')).Path
    NativeHostExecutable = $hostExecutable
    NativeHostManifest = $manifestPath
    RegistryPath = $registryPath
    RegistrationMatches = $true
    SharedChromiumRegistration = $true
    LegacyRegistrationMigrated = $legacyRegistrationMigrated
    ObsoleteBraveRegistrationRemoved = $obsoleteBraveRegistrationRemoved
    ObsoleteBraveRegistrationPreserved = $obsoleteBraveRegistrationPreserved
    UnsignedPrototype = $true
}
