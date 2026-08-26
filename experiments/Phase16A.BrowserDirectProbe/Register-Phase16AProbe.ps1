[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Chrome', 'Brave')]
    [string] $Browser
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$hostName = 'com.command1264.medialock.phase16a'
$extensionId = 'kggfkkiifnclhhmibdglkbdfbacakemn'
$projectPath = Join-Path $PSScriptRoot 'Phase16A.BrowserDirectProbe.csproj'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputRoot = Join-Path $repositoryRoot "artifacts\phase16a-browser-direct\$($Browser.ToLowerInvariant())"
$publishRoot = Join-Path $outputRoot 'native-host'
$manifestPath = Join-Path $outputRoot 'native-host-manifest.json'

$registryRoot = switch ($Browser) {
    'Chrome' { 'HKCU:\Software\Google\Chrome\NativeMessagingHosts' }
    'Brave' { 'HKCU:\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts' }
}
$registryPath = Join-Path $registryRoot $hostName

if (Test-Path -LiteralPath $registryPath) {
    $existingManifest = [string](Get-Item -LiteralPath $registryPath).GetValue('')
    if (![string]::Equals($existingManifest, $manifestPath, [StringComparison]::Ordinal)) {
        throw "The Native Messaging host name is already registered to a different manifest: $existingManifest"
    }
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $publishRoot
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

[pscustomobject]@{
    Browser = $Browser
    ExtensionId = $extensionId
    ExtensionRoot = (Resolve-Path (Join-Path $PSScriptRoot 'extension')).Path
    NativeHostExecutable = $hostExecutable
    NativeHostManifest = $manifestPath
    RegistryPath = $registryPath
    RegistrationMatches = $true
    UnsignedPrototype = $true
}
