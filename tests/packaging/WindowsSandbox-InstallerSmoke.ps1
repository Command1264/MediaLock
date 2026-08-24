[CmdletBinding()]
param(
    [string] $ArtifactRoot = 'C:\MediaLockArtifacts',
    [string] $ResultPath = 'C:\MediaLockResults\installer-smoke.json'
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

function Get-MediaLockUninstallEntry {
    Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' |
        ForEach-Object { Get-ItemProperty $_.PSPath } |
        Where-Object { $_.DisplayName -eq 'Media Lock' } |
        Select-Object -First 1
}

function Invoke-Installer {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $process = Start-Process `
        -FilePath $Path `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER' `
        -Wait `
        -PassThru
    Assert-Condition ($process.ExitCode -eq 0) "Installer failed with exit code $($process.ExitCode)."
}

function Invoke-Uninstaller {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $process = Start-Process `
        -FilePath $Path `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' `
        -Wait `
        -PassThru
    Assert-Condition ($process.ExitCode -eq 0) "Uninstaller failed with exit code $($process.ExitCode)."
}

$manifestPath = Get-ChildItem `
    -LiteralPath $ArtifactRoot `
    -Filter 'MediaLock-*-win-x64.manifest.json' `
    -File |
    Select-Object -First 1 -ExpandProperty FullName
Assert-Condition (-not [string]::IsNullOrWhiteSpace($manifestPath)) 'Manifest was not found.'

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$archivePath = Join-Path $ArtifactRoot $manifest.archive.fileName
$installerPath = Join-Path $ArtifactRoot $manifest.installer.fileName
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-Condition ($manifest.schemaVersion -eq 2) 'Manifest schemaVersion must be 2.'
Assert-Condition (-not $manifest.sourceDirty) 'Sandbox artifacts must come from a clean source.'
Assert-Condition ($archiveHash -eq $manifest.archive.sha256) 'Archive SHA-256 mismatch.'
Assert-Condition ($installerHash -eq $manifest.installer.sha256) 'Installer SHA-256 mismatch.'
Assert-Condition (
    (Get-AuthenticodeSignature -LiteralPath $installerPath).Status -eq 'NotSigned') `
    'Installer must be explicitly unsigned.'

$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\MediaLock'
$installedExe = Join-Path $installRoot 'MediaLock.exe'
$uninstaller = Join-Path $installRoot 'unins000.exe'
$shortcut = Join-Path `
    $env:APPDATA `
    'Microsoft\Windows\Start Menu\Programs\Media Lock\Media Lock.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'MediaLock'
$retainedMarker = Join-Path $userDataRoot 'installer-smoke-retained.txt'

Assert-Condition (-not (Test-Path -LiteralPath $installRoot)) `
    "Sandbox install root was not clean: $installRoot"
Assert-Condition ($null -eq (Get-MediaLockUninstallEntry)) `
    'Sandbox already contained a Media Lock uninstall entry.'

$initialStartupProperty =
    (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']
Assert-Condition ($null -eq $initialStartupProperty) `
    'Sandbox already contained a Media Lock startup value.'

Invoke-Installer -Path $installerPath
$installedEntry = Get-MediaLockUninstallEntry
Assert-Condition (Test-Path -LiteralPath $installedExe -PathType Leaf) `
    'Installed MediaLock.exe was not found.'
Assert-Condition (Test-Path -LiteralPath $shortcut -PathType Leaf) `
    'Current-user Start Menu shortcut was not found.'
Assert-Condition ($null -ne $installedEntry) 'Installed apps entry was not found.'
Assert-Condition ($installedEntry.DisplayVersion -eq $manifest.version) `
    'Installed apps version does not match the manifest.'
Assert-Condition (
    (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash.ToLowerInvariant() -eq
        $manifest.executable.sha256) `
    'Installed executable does not match the staged payload.'
Assert-Condition ($null -eq (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']) `
    'Installer enabled login startup without user consent.'

New-Item -ItemType Directory -Path $userDataRoot -Force | Out-Null
Set-Content -LiteralPath $retainedMarker -Value 'retain' -Encoding ascii -NoNewline
$installedStartupValue = '"{0}" --startup' -f $installedExe
Set-ItemProperty -Path $runKey -Name 'MediaLock' -Value $installedStartupValue
Invoke-Uninstaller -Path $uninstaller
Assert-Condition (-not (Test-Path -LiteralPath $installedExe)) `
    'Installed executable remained after uninstall.'
Assert-Condition (-not (Test-Path -LiteralPath $shortcut)) `
    'Start Menu shortcut remained after uninstall.'
Assert-Condition ($null -eq (Get-MediaLockUninstallEntry)) `
    'Installed apps entry remained after uninstall.'
Assert-Condition ($null -eq (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']) `
    'Owned login startup value remained after uninstall.'
Assert-Condition (Test-Path -LiteralPath $retainedMarker -PathType Leaf) `
    'Uninstall removed user data without consent.'

Invoke-Installer -Path $installerPath
$portableStartupValue = '"C:\Portable\MediaLock.exe" --startup'
Set-ItemProperty -Path $runKey -Name 'MediaLock' -Value $portableStartupValue
Invoke-Uninstaller -Path $uninstaller
$remainingStartupProperty =
    (Get-ItemProperty -Path $runKey).PSObject.Properties['MediaLock']
Assert-Condition ($null -ne $remainingStartupProperty) `
    'Uninstall removed a portable-owned startup value.'
Assert-Condition (
    [string]::Equals(
        [string]$remainingStartupProperty.Value,
        $portableStartupValue,
        [StringComparison]::Ordinal)) `
    'Uninstall changed a portable-owned startup value.'
Remove-ItemProperty -Path $runKey -Name 'MediaLock'
$remainingProcesses = @(Get-Process MediaLock -ErrorAction SilentlyContinue)
Assert-Condition ($remainingProcesses.Count -eq 0) `
    'Media Lock process remained after installer transaction smoke.'
Assert-Condition (-not (Test-Path -LiteralPath $installedExe)) `
    'Installed executable remained after portable-ownership uninstall.'

$result = [ordered]@{
    passed = $true
    version = $manifest.version
    sourceCommit = $manifest.sourceCommit
    archiveSha256 = $archiveHash
    installerSha256 = $installerHash
    installerSignature = 'NotSigned'
    installedPayloadMatched = $true
    startMenuShortcutCreated = $true
    installedAppsEntryCreated = $true
    startupDisabledByDefault = $true
    ownedStartupRemoved = $true
    portableStartupPreserved = $true
    userDataRetained = $true
    processCount = $remainingProcesses.Count
}

$resultDirectory = Split-Path -Parent $ResultPath
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
$result | ConvertTo-Json
