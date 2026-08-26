[CmdletBinding()]
param()

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

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $ExpectedMessage
    )

    $actualMessage = $null
    try {
        & $Action
    }
    catch {
        $actualMessage = $_.Exception.Message
    }

    Assert-Condition `
        ($actualMessage -like "*$ExpectedMessage*") `
        "Expected failure containing '$ExpectedMessage', found '$actualMessage'."
}

$helperPath = Join-Path $PSScriptRoot 'MediaLockReleaseArtifacts.ps1'
. $helperPath

foreach ($scriptName in @(
    'WindowsSandbox-InstallerUpgradeSmoke.ps1',
    'WindowsSandbox-InstallerCancellationSmoke.ps1')) {
    $scriptPath = Join-Path $PSScriptRoot $scriptName
    $command = Get-Command -Name $scriptPath
    Assert-Condition $command.Parameters.ContainsKey('OlderVersion') `
        "$scriptName must require an explicit OlderVersion."
    Assert-Condition $command.Parameters.ContainsKey('NewerVersion') `
        "$scriptName must require an explicit NewerVersion."
    Assert-Condition $command.Parameters.ContainsKey('ExpectedOlderInstallerSha256') `
        "$scriptName must require the expected older-installer SHA-256."
    $scriptText = Get-Content -LiteralPath $scriptPath -Raw
    Assert-Condition ($scriptText -notmatch '\[version\]') `
        "$scriptName must not cast release candidates to [version]."
    Assert-Condition ($scriptText -match 'Get-MediaLockArtifactPair') `
        "$scriptName must use the shared explicit artifact-selection seam."
    foreach ($resultField in @(
        'payloadUnchanged',
        'registrationUnchanged',
        'shortcutUnchanged',
        'settingsUnchanged',
        'stateUnchanged')) {
        Assert-Condition ($scriptText -match $resultField) `
            "$scriptName must report $resultField."
    }
    if ($scriptName -eq 'WindowsSandbox-InstallerUpgradeSmoke.ps1') {
        Assert-Condition ($scriptText -match '\$repair\s*=\s*Invoke-Installer') `
            'The upgrade smoke must execute the newer installer again for same-version repair.'
        Assert-Condition ($scriptText -match 'repairExitCode') `
            'The upgrade-smoke result must report repairExitCode.'
        Assert-Condition ($scriptText -match 'Assert-MediaLockInstalledStateUnchanged') `
            'The upgrade smoke must compare the complete installed-state snapshot after blocked downgrade.'
    }
    else {
        Assert-Condition ($scriptText -match '\$preparationPath') `
            'The cancellation smoke must persist a preparation snapshot across its two phases.'
        Assert-Condition ($scriptText -match 'Assert-MediaLockInstalledStateUnchanged') `
            'The cancellation smoke must compare the complete installed-state snapshot after cancellation.'
    }
}

$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "MediaLock-Artifact-Selection-Test-$([Guid]::NewGuid().ToString('N'))"

function Add-TestArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $Version,

        [string] $DirectoryName = $Version
    )

    $directory = Join-Path $temporaryRoot $DirectoryName
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $installerName = "MediaLock-Setup-$Version-win-x64.exe"
    $installerPath = Join-Path $directory $installerName
    [IO.File]::WriteAllText($installerPath, "installer-$Version")
    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        version = $Version
        installer = [ordered]@{
            fileName = $installerName
            sha256 = $installerHash
        }
    } |
        ConvertTo-Json -Depth 3 |
        Set-Content `
            -LiteralPath (Join-Path $directory "MediaLock-$Version-win-x64.manifest.json") `
            -Encoding utf8
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    Add-TestArtifact -Version '0.2.0'
    Add-TestArtifact -Version '0.3.0-rc.1'
    Add-TestArtifact -Version '0.3.0'

    $pair = @(Get-MediaLockArtifactPair `
        -ArtifactRoot $temporaryRoot `
        -OlderVersion '0.2.0' `
        -NewerVersion '0.3.0-rc.1')
    Assert-Condition ($pair.Count -eq 2) 'The pair must contain exactly two artifacts.'
    Assert-Condition ($pair[0].Manifest.version -eq '0.2.0') 'The explicitly selected older artifact was not first.'
    Assert-Condition ($pair[1].Manifest.version -eq '0.3.0-rc.1') `
        'The explicitly selected prerelease artifact was not second.'

    $stablePair = @(Get-MediaLockArtifactPair `
        -ArtifactRoot $temporaryRoot `
        -OlderVersion '0.3.0-rc.1' `
        -NewerVersion '0.3.0')
    Assert-Condition ($stablePair.Count -eq 2) `
        'The prerelease-to-stable pair must contain exactly two artifacts.'
    Assert-Condition ($stablePair[0].Manifest.version -eq '0.3.0-rc.1') `
        'The explicitly selected release candidate was not first.'
    Assert-Condition ($stablePair[1].Manifest.version -eq '0.3.0') `
        'The explicitly selected stable release was not second.'

    $validatedRcInstaller = Assert-MediaLockInstallerArtifact `
        -Artifact $stablePair[0] `
        -ExpectedSha256 $stablePair[0].Manifest.installer.sha256
    Assert-Condition ($validatedRcInstaller.Sha256 -eq $stablePair[0].Manifest.installer.sha256) `
        'The pinned release-candidate installer digest was not returned.'
    Assert-Throws `
        { Assert-MediaLockInstallerArtifact -Artifact $stablePair[0] -ExpectedSha256 ('0' * 64) } `
        'does not match the pinned digest'
    $rcInstallerPath = Join-Path `
        $stablePair[0].Directory `
        $stablePair[0].Manifest.installer.fileName
    [IO.File]::AppendAllText($rcInstallerPath, '-tampered')
    Assert-Throws `
        { Assert-MediaLockInstallerArtifact -Artifact $stablePair[0] } `
        'does not match the 0.3.0-rc.1 manifest'
    [IO.File]::WriteAllText($rcInstallerPath, 'installer-0.3.0-rc.1')

    Assert-Condition `
        ((Compare-MediaLockReleaseVersion -Left '0.3.0-rc.1' -Right '0.3.0') -lt 0) `
        'A release candidate must sort before the stable release with the same base version.'
    Assert-Condition `
        ((Compare-MediaLockReleaseVersion -Left '0.3.0-rc.1' -Right '0.3.0-rc.2') -lt 0) `
        'Release-candidate numbers must sort numerically.'

    Assert-Throws `
        { Get-MediaLockArtifactPair $temporaryRoot '0.3.0' '0.3.0-rc.1' } `
        'must be older than'
    Assert-Throws `
        { Compare-MediaLockReleaseVersion '0.3.0-preview.1' '0.3.0' } `
        'Unsupported Media Lock release version'

    Add-TestArtifact -Version '0.3.0-rc.1' -DirectoryName 'duplicate-rc'
    Assert-Throws `
        { Get-MediaLockArtifactPair $temporaryRoot '0.2.0' '0.3.0-rc.1' } `
        'Exactly one manifest is required for 0.3.0-rc.1'
}
finally {
    $resolvedTemporaryParent = (Resolve-Path ([IO.Path]::GetTempPath())).Path.TrimEnd('\')
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = (Resolve-Path -LiteralPath $temporaryRoot).Path
        if (-not $resolvedTemporaryRoot.StartsWith(
            $resolvedTemporaryParent + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove test output outside the temporary directory: $resolvedTemporaryRoot"
        }

        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
