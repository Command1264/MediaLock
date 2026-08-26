[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Verify')]
    [string] $Mode,

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

    [int] $CancellationExitCode = -1,

    [string] $ResultPath = 'C:\MediaLockResults\installer-cancellation-smoke.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'MediaLockReleaseArtifacts.ps1')

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

$manifests = @(Get-MediaLockArtifactPair `
    -ArtifactRoot $ArtifactRoot `
    -OlderVersion $OlderVersion `
    -NewerVersion $NewerVersion)

$older = $manifests[0]
$newer = $manifests[1]
$olderInstallerArtifact = Assert-MediaLockInstallerArtifact `
    -Artifact $older `
    -ExpectedSha256 $ExpectedOlderInstallerSha256
$newerInstallerArtifact = Assert-MediaLockInstallerArtifact -Artifact $newer
$olderInstaller = $olderInstallerArtifact.Path
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\MediaLock'
$installedExe = Join-Path $installRoot 'MediaLock.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'MediaLock'
$retainedMarker = Join-Path $userDataRoot 'cancellation-smoke-retained.txt'
$settingsPath = Join-Path $userDataRoot 'settings.json'
$statePath = Join-Path $userDataRoot 'state.json'
$expectedSettings = '{"schemaVersion":7,"marker":"cancellation-settings"}'
$expectedState = '{"schemaVersion":1,"marker":"cancellation-state"}'
$expectedStartupValue = '"{0}" --startup' -f $installedExe
$preparationPath = "$ResultPath.prepare.json"

if ($Mode -eq 'Prepare') {
    Assert-Condition (-not (Test-Path -LiteralPath $installRoot)) `
        "Sandbox install root was not clean: $installRoot"
    Assert-Condition (@(Get-MediaLockUninstallEntries).Count -eq 0) `
        'Sandbox already contained a Media Lock uninstall entry.'

    $install = Start-Process `
        -FilePath $olderInstaller `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER' `
        -Wait `
        -PassThru
    Assert-Condition ($install.ExitCode -eq 0) `
        "Older installer failed with exit code $($install.ExitCode)."

    New-Item -ItemType Directory -Path $userDataRoot -Force | Out-Null
    Set-Content -LiteralPath $retainedMarker -Value 'retain' -Encoding ascii -NoNewline
    [IO.File]::WriteAllText($settingsPath, $expectedSettings, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($statePath, $expectedState, [Text.UTF8Encoding]::new($false))
    Set-ItemProperty -Path $runKey -Name 'MediaLock' -Value $expectedStartupValue

    $preparation = [ordered]@{
        olderVersion = $older.Manifest.version
        newerVersion = $newer.Manifest.version
        olderInstallerSha256 = $olderInstallerArtifact.Sha256
        newerInstallerSha256 = $newerInstallerArtifact.Sha256
        installedState = Get-MediaLockInstalledStateSnapshot `
            -InstalledExe $installedExe `
            -ShortcutPath (Join-Path `
                $env:APPDATA `
                'Microsoft\Windows\Start Menu\Programs\Media Lock\Media Lock.lnk') `
            -RunKey $runKey `
            -SettingsPath $settingsPath `
            -StatePath $statePath `
            -RetainedMarkerPath $retainedMarker
    }
    $preparationDirectory = Split-Path -Parent $preparationPath
    New-Item -ItemType Directory -Path $preparationDirectory -Force | Out-Null
    $preparation | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $preparationPath -Encoding utf8
    exit 0
}

Assert-Condition ($CancellationExitCode -eq 2) `
    "Pre-install cancellation must return exit code 2, but returned $CancellationExitCode."
Assert-Condition (Test-Path -LiteralPath $preparationPath -PathType Leaf) `
    "Cancellation preparation snapshot was not found: $preparationPath"
$preparation = Get-Content -LiteralPath $preparationPath -Raw | ConvertFrom-Json
Assert-Condition ($preparation.olderVersion -eq $older.Manifest.version) `
    'Cancellation preparation snapshot selected a different older version.'
Assert-Condition ($preparation.newerVersion -eq $newer.Manifest.version) `
    'Cancellation preparation snapshot selected a different newer version.'
Assert-Condition ($preparation.olderInstallerSha256 -eq $olderInstallerArtifact.Sha256) `
    'Cancellation preparation snapshot used a different older installer.'
Assert-Condition ($preparation.newerInstallerSha256 -eq $newerInstallerArtifact.Sha256) `
    'Cancellation preparation snapshot used a different newer installer.'
$entries = @(Get-MediaLockUninstallEntries)
$startupProperty = (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']
$shortcut = Join-Path `
    $env:APPDATA `
    'Microsoft\Windows\Start Menu\Programs\Media Lock\Media Lock.lnk'
$postCancellationSnapshot = Get-MediaLockInstalledStateSnapshot `
    -InstalledExe $installedExe `
    -ShortcutPath $shortcut `
    -RunKey $runKey `
    -SettingsPath $settingsPath `
    -StatePath $statePath `
    -RetainedMarkerPath $retainedMarker
Assert-Condition ($entries.Count -eq 1) `
    'Cancelled upgrade changed the Installed apps entry count.'
Assert-Condition ($entries[0].DisplayVersion -eq $older.Manifest.version) `
    'Cancelled upgrade changed the Installed apps version.'
Assert-Condition ((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion -eq `
    $older.Manifest.version) 'Cancelled upgrade replaced the installed payload.'
Assert-Condition (Test-Path -LiteralPath $retainedMarker -PathType Leaf) `
    'Cancelled upgrade removed retained user data.'
Assert-Condition ((Get-Content -LiteralPath $settingsPath -Raw) -eq $expectedSettings) `
    'Cancelled upgrade changed settings.json.'
Assert-Condition ((Get-Content -LiteralPath $statePath -Raw) -eq $expectedState) `
    'Cancelled upgrade changed state.json.'
Assert-Condition ($null -ne $startupProperty) `
    'Cancelled upgrade removed the enabled login-startup value.'
Assert-Condition ([string]::Equals(
    [string]$startupProperty.Value,
    $expectedStartupValue,
    [StringComparison]::Ordinal)) 'Cancelled upgrade changed the login-startup command.'
Assert-MediaLockInstalledStateUnchanged `
    -Expected $preparation.installedState `
    -Actual $postCancellationSnapshot `
    -Context 'Cancelled upgrade'

$result = [ordered]@{
    passed = $true
    olderVersion = $older.Manifest.version
    attemptedVersion = $newer.Manifest.version
    olderInstallerSha256 = $olderInstallerArtifact.Sha256
    newerInstallerSha256 = $newerInstallerArtifact.Sha256
    cancellationStage = 'BeforeInstall'
    cancellationExitCode = $CancellationExitCode
    installedVersion = $entries[0].DisplayVersion
    installedAppsEntryCount = $entries.Count
    payloadUnchanged = $true
    registrationUnchanged = $true
    shortcutUnchanged = $true
    userDataRetained = $true
    settingsUnchanged = $true
    stateUnchanged = $true
    startupValuePreserved = $true
}

$resultDirectory = Split-Path -Parent $ResultPath
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
$result | ConvertTo-Json
