[CmdletBinding()]
param(
    [string] $ArtifactRoot = 'C:\MediaLockArtifacts',
    [string] $ResultPath = 'C:\MediaLockResults\installer-upgrade-smoke.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

function Get-ArtifactPair {
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
        'Exactly two stable-version manifests are required for the upgrade smoke.'
    Assert-Condition ([version]$manifests[0].Manifest.version -lt [version]$manifests[1].Manifest.version) `
        'The upgrade smoke requires distinct older and newer versions.'

    return $manifests
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

$artifacts = Get-ArtifactPair
$older = $artifacts[0]
$newer = $artifacts[1]
$olderInstaller = Join-Path $older.Directory $older.Manifest.installer.fileName
$newerInstaller = Join-Path $newer.Directory $newer.Manifest.installer.fileName
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\MediaLock'
$installedExe = Join-Path $installRoot 'MediaLock.exe'
$shortcut = Join-Path `
    $env:APPDATA `
    'Microsoft\Windows\Start Menu\Programs\Media Lock\Media Lock.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'MediaLock'
$retainedMarker = Join-Path $userDataRoot 'upgrade-smoke-retained.txt'

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
Assert-Condition ($null -ne $startupProperty) `
    'Upgrade removed the enabled login-startup value.'
Assert-Condition ([string]::Equals(
    [string]$startupProperty.Value,
    $expectedStartupValue,
    [StringComparison]::Ordinal)) 'Upgrade changed the login-startup command.'

$downgrade = Invoke-Installer -Path $olderInstaller
$postDowngradeEntries = @(Get-MediaLockUninstallEntries)
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

$result = [ordered]@{
    passed = $true
    olderVersion = $older.Manifest.version
    newerVersion = $newer.Manifest.version
    upgradeExitCode = $upgrade.ExitCode
    downgradeExitCode = $downgrade.ExitCode
    installedAppsEntryCount = $postDowngradeEntries.Count
    installedVersion = $postDowngradeEntries[0].DisplayVersion
    userDataRetained = $true
    startupValuePreserved = $true
}

$resultDirectory = Split-Path -Parent $ResultPath
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
$result | ConvertTo-Json
