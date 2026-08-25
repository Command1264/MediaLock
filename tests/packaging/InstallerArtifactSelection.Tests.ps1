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
    $scriptText = Get-Content -LiteralPath $scriptPath -Raw
    Assert-Condition ($scriptText -notmatch '\[version\]') `
        "$scriptName must not cast release candidates to [version]."
    Assert-Condition ($scriptText -match 'Get-MediaLockArtifactPair') `
        "$scriptName must use the shared explicit artifact-selection seam."
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
    [IO.File]::WriteAllText((Join-Path $directory $installerName), 'installer')
    [ordered]@{
        version = $Version
        installer = [ordered]@{
            fileName = $installerName
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
