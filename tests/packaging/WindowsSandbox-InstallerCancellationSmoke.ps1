[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Verify')]
    [string] $Mode,

    [string] $ArtifactRoot = 'C:\MediaLockArtifacts',

    [int] $CancellationExitCode = -1,

    [string] $ResultPath = 'C:\MediaLockResults\installer-cancellation-smoke.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

$manifests = @(
    Get-ChildItem -LiteralPath $ArtifactRoot -Filter 'MediaLock-*-win-x64.manifest.json' -Recurse |
        Where-Object { -not $_.PSIsContainer } |
        ForEach-Object {
            [pscustomobject]@{
                Manifest = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                Directory = $_.DirectoryName
            }
        } |
        Sort-Object { [version]$_.Manifest.version }
)
Assert-Condition ($manifests.Count -eq 2) `
    'Exactly two stable-version manifests are required for the cancellation smoke.'

$older = $manifests[0]
$newer = $manifests[1]
$olderInstaller = Join-Path $older.Directory $older.Manifest.installer.fileName
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\MediaLock'
$installedExe = Join-Path $installRoot 'MediaLock.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'MediaLock'
$retainedMarker = Join-Path $userDataRoot 'cancellation-smoke-retained.txt'
$expectedStartupValue = '"{0}" --startup' -f $installedExe

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
    Set-ItemProperty -Path $runKey -Name 'MediaLock' -Value $expectedStartupValue
    exit 0
}

Assert-Condition ($CancellationExitCode -eq 2) `
    "Pre-install cancellation must return exit code 2, but returned $CancellationExitCode."
$entries = @(Get-MediaLockUninstallEntries)
$startupProperty = (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']
Assert-Condition ($entries.Count -eq 1) `
    'Cancelled upgrade changed the Installed apps entry count.'
Assert-Condition ($entries[0].DisplayVersion -eq $older.Manifest.version) `
    'Cancelled upgrade changed the Installed apps version.'
Assert-Condition ((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion -eq `
    $older.Manifest.version) 'Cancelled upgrade replaced the installed payload.'
Assert-Condition (Test-Path -LiteralPath $retainedMarker -PathType Leaf) `
    'Cancelled upgrade removed retained user data.'
Assert-Condition ($null -ne $startupProperty) `
    'Cancelled upgrade removed the enabled login-startup value.'
Assert-Condition ([string]::Equals(
    [string]$startupProperty.Value,
    $expectedStartupValue,
    [StringComparison]::Ordinal)) 'Cancelled upgrade changed the login-startup command.'

$result = [ordered]@{
    passed = $true
    olderVersion = $older.Manifest.version
    attemptedVersion = $newer.Manifest.version
    cancellationStage = 'BeforeInstall'
    cancellationExitCode = $CancellationExitCode
    installedVersion = $entries[0].DisplayVersion
    installedAppsEntryCount = $entries.Count
    userDataRetained = $true
    startupValuePreserved = $true
}

$resultDirectory = Split-Path -Parent $ResultPath
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
$result | ConvertTo-Json
