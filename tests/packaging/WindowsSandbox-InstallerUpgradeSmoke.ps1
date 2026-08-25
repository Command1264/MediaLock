[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-rc\.\d+)?$')]
    [string] $OlderVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-rc\.\d+)?$')]
    [string] $NewerVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string] $ExpectedOlderInstallerSha256,

    [string] $ArtifactRoot = 'C:\MediaLockArtifacts',
    [string] $ResultPath = 'C:\MediaLockResults\installer-upgrade-smoke.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'MediaLockReleaseArtifacts.ps1')

trap {
    $failureResult = [ordered]@{
        passed = $false
        error = $_.Exception.Message
        scriptStackTrace = $_.ScriptStackTrace
    }

    $failureResultDirectory = Split-Path -Parent $ResultPath
    New-Item -ItemType Directory -Path $failureResultDirectory -Force | Out-Null
    $failureResult | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
    exit 1
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-MediaLockUninstallEntries {
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (-not (Test-Path -LiteralPath $uninstallRoot)) {
        return @()
    }

    @(
        Get-ChildItem -LiteralPath $uninstallRoot |
            ForEach-Object { Get-ItemProperty $_.PSPath } |
            Where-Object { $_.DisplayName -eq 'Media Lock' }
    )
}

function Invoke-Installer {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    Start-Process `
        -FilePath $Path `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER' `
        -Wait `
        -PassThru
}

$artifacts = @(Get-MediaLockArtifactPair `
    -ArtifactRoot $ArtifactRoot `
    -OlderVersion $OlderVersion `
    -NewerVersion $NewerVersion)
$older = $artifacts[0]
$newer = $artifacts[1]
$olderInstallerArtifact = Assert-MediaLockInstallerArtifact `
    -Artifact $older `
    -ExpectedSha256 $ExpectedOlderInstallerSha256
$newerInstallerArtifact = Assert-MediaLockInstallerArtifact -Artifact $newer
$olderInstaller = $olderInstallerArtifact.Path
$newerInstaller = $newerInstallerArtifact.Path
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\MediaLock'
$installedExe = Join-Path $installRoot 'MediaLock.exe'
$shortcut = Join-Path `
    $env:APPDATA `
    'Microsoft\Windows\Start Menu\Programs\Media Lock\Media Lock.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'MediaLock'
$retainedMarker = Join-Path $userDataRoot 'upgrade-smoke-retained.txt'
$settingsPath = Join-Path $userDataRoot 'settings.json'
$statePath = Join-Path $userDataRoot 'state.json'

Assert-Condition (-not (Test-Path -LiteralPath $installRoot)) `
    "Sandbox install root was not clean: $installRoot"
Assert-Condition (@(Get-MediaLockUninstallEntries).Count -eq 0) `
    'Sandbox already contained a Media Lock uninstall entry.'

$olderInstall = Invoke-Installer -Path $olderInstaller
Assert-Condition ($olderInstall.ExitCode -eq 0) `
    "Older installer failed with exit code $($olderInstall.ExitCode)."
Assert-Condition ((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion -eq `
    $older.Manifest.version) 'Older payload was not installed.'

New-Item -ItemType Directory -Path $userDataRoot -Force | Out-Null
Set-Content -LiteralPath $retainedMarker -Value 'retain' -Encoding ascii -NoNewline
[IO.File]::WriteAllText(
    $settingsPath,
    '{"schemaVersion":7,"marker":"upgrade-settings"}',
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $statePath,
    '{"schemaVersion":1,"marker":"upgrade-state"}',
    [Text.UTF8Encoding]::new($false))
$expectedSettingsSha256 =
    (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedStateSha256 =
    (Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedStartupValue = '"{0}" --startup' -f $installedExe
Set-ItemProperty -Path $runKey -Name 'MediaLock' -Value $expectedStartupValue

$upgrade = Invoke-Installer -Path $newerInstaller
Assert-Condition ($upgrade.ExitCode -eq 0) `
    "Upgrade failed with exit code $($upgrade.ExitCode)."
$upgradeEntries = @(Get-MediaLockUninstallEntries)
$startupProperty = (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']
Assert-Condition ($upgradeEntries.Count -eq 1) `
    'Upgrade created duplicate Installed apps entries.'
Assert-Condition ($upgradeEntries[0].DisplayVersion -eq $newer.Manifest.version) `
    'Installed apps did not report the newer version after upgrade.'
Assert-Condition ((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion -eq `
    $newer.Manifest.version) 'Upgrade did not replace the installed payload.'
Assert-Condition (Test-Path -LiteralPath $shortcut -PathType Leaf) `
    'Upgrade did not preserve the Start Menu shortcut.'
Assert-Condition (Test-Path -LiteralPath $retainedMarker -PathType Leaf) `
    'Upgrade removed retained user data.'
Assert-Condition ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq `
    $expectedSettingsSha256) 'Upgrade changed settings.json.'
Assert-Condition ((Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash.ToLowerInvariant() -eq `
    $expectedStateSha256) 'Upgrade changed state.json.'
Assert-Condition ($null -ne $startupProperty) `
    'Upgrade removed the enabled login-startup value.'
Assert-Condition ([string]::Equals(
    [string]$startupProperty.Value,
    $expectedStartupValue,
    [StringComparison]::Ordinal)) 'Upgrade changed the login-startup command.'
$postUpgradePayloadSha256 =
    (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash.ToLowerInvariant()

$repair = Invoke-Installer -Path $newerInstaller
Assert-Condition ($repair.ExitCode -eq 0) `
    "Same-version repair failed with exit code $($repair.ExitCode)."
$repairEntries = @(Get-MediaLockUninstallEntries)
$repairStartupProperty = (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']
Assert-Condition ($repairEntries.Count -eq 1) `
    'Same-version repair created duplicate Installed apps entries.'
Assert-Condition ($repairEntries[0].DisplayVersion -eq $newer.Manifest.version) `
    'Same-version repair changed the Installed apps version.'
Assert-Condition ((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion -eq `
    $newer.Manifest.version) 'Same-version repair changed the installed ProductVersion.'
Assert-Condition ((Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash.ToLowerInvariant() -eq `
    $postUpgradePayloadSha256) 'Same-version repair changed the installed payload.'
Assert-Condition (Test-Path -LiteralPath $shortcut -PathType Leaf) `
    'Same-version repair removed the Start Menu shortcut.'
Assert-Condition (Test-Path -LiteralPath $retainedMarker -PathType Leaf) `
    'Same-version repair removed retained user data.'
Assert-Condition ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq `
    $expectedSettingsSha256) 'Same-version repair changed settings.json.'
Assert-Condition ((Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash.ToLowerInvariant() -eq `
    $expectedStateSha256) 'Same-version repair changed state.json.'
Assert-Condition ($null -ne $repairStartupProperty) `
    'Same-version repair removed the enabled login-startup value.'
Assert-Condition ([string]::Equals(
    [string]$repairStartupProperty.Value,
    $expectedStartupValue,
    [StringComparison]::Ordinal)) 'Same-version repair changed the login-startup command.'

$downgrade = Invoke-Installer -Path $olderInstaller
$postDowngradeEntries = @(Get-MediaLockUninstallEntries)
$postDowngradeStartupProperty =
    (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']
Assert-Condition ($downgrade.ExitCode -eq 7) `
    "Downgrade must be blocked with exit code 7, but returned $($downgrade.ExitCode)."
Assert-Condition ($postDowngradeEntries.Count -eq 1) `
    'Blocked downgrade changed the Installed apps entry count.'
Assert-Condition ($postDowngradeEntries[0].DisplayVersion -eq $newer.Manifest.version) `
    'Blocked downgrade changed the Installed apps version.'
Assert-Condition ((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion -eq `
    $newer.Manifest.version) 'Blocked downgrade replaced the newer payload.'
Assert-Condition (Test-Path -LiteralPath $retainedMarker -PathType Leaf) `
    'Blocked downgrade removed retained user data.'
Assert-Condition ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq `
    $expectedSettingsSha256) 'Blocked downgrade changed settings.json.'
Assert-Condition ((Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash.ToLowerInvariant() -eq `
    $expectedStateSha256) 'Blocked downgrade changed state.json.'
Assert-Condition ($null -ne $postDowngradeStartupProperty) `
    'Blocked downgrade removed the enabled login-startup value.'
Assert-Condition ([string]::Equals(
    [string]$postDowngradeStartupProperty.Value,
    $expectedStartupValue,
    [StringComparison]::Ordinal)) 'Blocked downgrade changed the login-startup command.'

$result = [ordered]@{
    passed = $true
    olderVersion = $older.Manifest.version
    newerVersion = $newer.Manifest.version
    olderInstallerSha256 = $olderInstallerArtifact.Sha256
    newerInstallerSha256 = $newerInstallerArtifact.Sha256
    upgradeExitCode = $upgrade.ExitCode
    repairExitCode = $repair.ExitCode
    downgradeExitCode = $downgrade.ExitCode
    installedAppsEntryCount = $postDowngradeEntries.Count
    installedVersion = $postDowngradeEntries[0].DisplayVersion
    userDataRetained = $true
    settingsUnchanged = $true
    stateUnchanged = $true
    startupValuePreserved = $true
}

$resultDirectory = Split-Path -Parent $ResultPath
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
$result | ConvertTo-Json
