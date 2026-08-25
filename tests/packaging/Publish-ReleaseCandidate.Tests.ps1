[CmdletBinding()]
param(
    [string] $InnoCompilerPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'

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

function Assert-VersionReachesDirtySourceGuard {
    param(
        [Parameter(Mandatory)]
        [string] $PublishScript,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $OutputRoot,

        [Parameter(Mandatory)]
        [string] $CompilerPath
    )

    $reachedDirtySourceGuard = $false
    try {
        & $PublishScript `
            -Version $Version `
            -OutputRoot $OutputRoot `
            -InnoCompilerPath $CompilerPath
    }
    catch {
        $reachedDirtySourceGuard = $_.Exception.Message -like '*require a clean Git worktree*'
    }

    Assert-Condition $reachedDirtySourceGuard "Publication must accept $Version and reject its dirty source worktree."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "MediaLock-Packaging-Test-$([Guid]::NewGuid().ToString('N'))"
$isolatedSourceRoot = Join-Path $temporaryRoot 'source'
$artifactOutputRoot = Join-Path $temporaryRoot 'output'
$expandedRoot = Join-Path $temporaryRoot 'expanded'
$dirtyMarkerPath = Join-Path $isolatedSourceRoot '.MediaLock-Packaging-Test.tmp'
$version = '0.2.0'
$artifactStem = "MediaLock-$version-win-x64"
$installerStem = "MediaLock-Setup-$version-win-x64"
$isolatedWorktreeCreated = $false

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    & git -C $repositoryRoot worktree add --detach $isolatedSourceRoot HEAD
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the isolated packaging-test worktree.'
    }
    $isolatedWorktreeCreated = $true

    $appProjectPath = Join-Path $isolatedSourceRoot 'src\MediaLock.App\MediaLock.App.csproj'
    $appProject = [xml](Get-Content -LiteralPath $appProjectPath -Raw)
    $declaredVersion = $appProject.SelectSingleNode('/Project/PropertyGroup/Version')?.InnerText
    $declaredInformationalVersion =
        $appProject.SelectSingleNode('/Project/PropertyGroup/InformationalVersion')?.InnerText
    Assert-Condition ($declaredVersion -eq $version) "MediaLock.App Version must be $version."
    Assert-Condition ($declaredInformationalVersion -eq $version) "MediaLock.App InformationalVersion must be $version."

    $publishScript = Join-Path $isolatedSourceRoot 'eng\Publish-ReleaseCandidate.ps1'
    [IO.File]::WriteAllText($dirtyMarkerPath, 'Packaging provenance test marker.')

    Assert-Condition (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf) `
        "Inno Setup compiler was not found: $InnoCompilerPath"
    Assert-VersionReachesDirtySourceGuard `
        $publishScript $version $artifactOutputRoot $InnoCompilerPath
    Assert-VersionReachesDirtySourceGuard `
        $publishScript '0.2.0-rc.99' $artifactOutputRoot $InnoCompilerPath

    $missingCompilerRejected = $false
    try {
        & $publishScript `
            -Version $version `
            -OutputRoot $artifactOutputRoot `
            -InnoCompilerPath (Join-Path $temporaryRoot 'missing\ISCC.exe') `
            -AllowDirty
    }
    catch {
        $missingCompilerRejected =
            $_.Exception.Message -like '*Inno Setup 6.7.3 compiler was not found*'
    }
    Assert-Condition $missingCompilerRejected `
        'Publication must reject a missing pinned Inno Setup compiler with an actionable error.'

    $wrongCompilerRejected = $false
    try {
        & $publishScript `
            -Version $version `
            -OutputRoot $artifactOutputRoot `
            -InnoCompilerPath (Get-Command powershell.exe).Source `
            -AllowDirty
    }
    catch {
        $wrongCompilerRejected =
            $_.Exception.Message -like '*requires Inno Setup 6.7.3*'
    }
    Assert-Condition $wrongCompilerRejected `
        'Publication must reject an unexpected compiler with an actionable error.'

    & $publishScript `
        -Version $version `
        -OutputRoot $artifactOutputRoot `
        -InnoCompilerPath $InnoCompilerPath `
        -AllowDirty

    $archivePath = Join-Path $artifactOutputRoot "$artifactStem.zip"
    $manifestPath = Join-Path $artifactOutputRoot "$artifactStem.manifest.json"
    $checksumPath = Join-Path $artifactOutputRoot "$artifactStem.sha256"
    $installerPath = Join-Path $artifactOutputRoot "$installerStem.exe"
    $installerChecksumPath = Join-Path $artifactOutputRoot "$installerStem.sha256"
    Assert-Condition (Test-Path -LiteralPath $archivePath -PathType Leaf) "Archive was not created: $archivePath"
    Assert-Condition (Test-Path -LiteralPath $manifestPath -PathType Leaf) "Manifest was not created: $manifestPath"
    Assert-Condition (Test-Path -LiteralPath $checksumPath -PathType Leaf) "Checksum was not created: $checksumPath"
    Assert-Condition (Test-Path -LiteralPath $installerPath -PathType Leaf) `
        "Installer was not created: $installerPath"
    Assert-Condition (Test-Path -LiteralPath $installerChecksumPath -PathType Leaf) `
        "Installer checksum was not created: $installerChecksumPath"

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Condition ($manifest.schemaVersion -eq 2) 'Manifest schemaVersion must be 2.'
    Assert-Condition ($manifest.product -eq 'Media Lock') 'Manifest product must be Media Lock.'
    Assert-Condition ($manifest.version -eq $version) "Manifest version must be $version."
    Assert-Condition ($manifest.runtimeIdentifier -eq 'win-x64') 'Manifest runtimeIdentifier must be win-x64.'
    Assert-Condition ($manifest.selfContained -eq $true) 'Manifest must declare a self-contained artifact.'
    Assert-Condition ($manifest.singleFile -eq $true) 'Manifest must declare a single-file artifact.'
    Assert-Condition ($manifest.signed -eq $false) 'Manifest must disclose that the executable is unsigned.'
    Assert-Condition ($manifest.innoSetupVersion -eq '6.7.3') `
        'Manifest must record the pinned Inno Setup version.'
    Assert-Condition ($manifest.sourceDirty -eq $true) 'Manifest must disclose that the test artifact used a dirty source tree.'
    Assert-Condition ($manifest.archive.fileName -eq "$artifactStem.zip") 'Manifest archive name is incorrect.'
    Assert-Condition ($manifest.installer.fileName -eq "$installerStem.exe") `
        'Manifest installer name is incorrect.'

    $expectedHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($manifest.archive.sha256 -eq $expectedHash) 'Manifest SHA-256 does not match the archive.'
    Assert-Condition ($manifest.archive.sizeBytes -eq (Get-Item -LiteralPath $archivePath).Length) 'Manifest archive size is incorrect.'
    Assert-Condition ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -eq "$expectedHash  $artifactStem.zip") 'Checksum file is incorrect.'

    $expectedInstallerHash =
        (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($manifest.installer.sha256 -eq $expectedInstallerHash) `
        'Manifest SHA-256 does not match the installer.'
    Assert-Condition ($manifest.installer.sizeBytes -eq (Get-Item -LiteralPath $installerPath).Length) `
        'Manifest installer size is incorrect.'
    Assert-Condition ((Get-Content -LiteralPath $installerChecksumPath -Raw).Trim() -eq `
        "$expectedInstallerHash  $installerStem.exe") 'Installer checksum file is incorrect.'
    Assert-Condition ($manifest.installer.signed -eq $false) `
        'Manifest must disclose that the installer is unsigned.'
    Assert-Condition ((Get-AuthenticodeSignature -LiteralPath $installerPath).Status -eq `
        [System.Management.Automation.SignatureStatus]::NotSigned) `
        'Installer must actually be unsigned.'
    Assert-Condition ((Get-Item -LiteralPath $installerPath).VersionInfo.ProductVersion.Trim() -eq $version) `
        'Installer ProductVersion must match the requested version.'

    Expand-Archive -LiteralPath $archivePath -DestinationPath $expandedRoot
    $archiveFiles = @(Get-ChildItem -LiteralPath $expandedRoot -File -Recurse)
    Assert-Condition ($archiveFiles.Count -eq 1) 'Release archive must contain exactly one file.'
    Assert-Condition ($archiveFiles[0].Name -eq 'MediaLock.exe') 'Release archive must contain MediaLock.exe.'
    Assert-Condition ($archiveFiles[0].VersionInfo.ProductVersion -eq $version) "Executable ProductVersion must be $version."
    Assert-Condition ($archiveFiles[0].VersionInfo.FileVersion -eq '0.2.0.0') 'Executable FileVersion must be 0.2.0.0.'
    Assert-Condition ($manifest.executable.fileName -eq 'MediaLock.exe') `
        'Manifest payload name is incorrect.'
    Assert-Condition ($manifest.executable.signed -eq $false) `
        'Manifest must disclose that the payload is unsigned.'
    Assert-Condition ($manifest.executable.sha256 -eq `
        (Get-FileHash -LiteralPath $archiveFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()) `
        'Manifest payload SHA-256 does not match the portable executable.'

    $rcVersion = '0.2.0-rc.99'
    $rcInstallerStem = "MediaLock-Setup-$rcVersion-win-x64-test"
    $rcInstallerPath = Join-Path $artifactOutputRoot "$rcInstallerStem.exe"
    $installerScriptPath = Join-Path $isolatedSourceRoot 'installer\MediaLock.iss'
    & $InnoCompilerPath `
        '/Qp' `
        "/DAppVersion=$rcVersion" `
        '/DBinaryVersion=0.2.0.0' `
        "/DPayloadPath=$($archiveFiles[0].FullName)" `
        "/DOutputDirectory=$artifactOutputRoot" `
        "/DOutputBaseName=$rcInstallerStem" `
        $installerScriptPath
    Assert-Condition ($LASTEXITCODE -eq 0) `
        'Installer source must compile a prerelease version.'
    Assert-Condition ((Get-Item -LiteralPath $rcInstallerPath).VersionInfo.ProductVersion.Trim() -eq `
        $rcVersion) 'Prerelease installer ProductVersion must retain the complete release version.'
}
finally {
    [IO.File]::Delete($dirtyMarkerPath)
    if ($isolatedWorktreeCreated) {
        & git -C $repositoryRoot worktree remove $isolatedSourceRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Could not remove the isolated packaging-test worktree: $isolatedSourceRoot"
        }
    }

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
