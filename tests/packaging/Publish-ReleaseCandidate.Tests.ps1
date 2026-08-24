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

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "MediaLock-Packaging-Test-$([Guid]::NewGuid().ToString('N'))"
$isolatedSourceRoot = Join-Path $temporaryRoot 'source'
$artifactOutputRoot = Join-Path $temporaryRoot 'output'
$expandedRoot = Join-Path $temporaryRoot 'expanded'
$dirtyMarkerPath = Join-Path $isolatedSourceRoot '.MediaLock-Packaging-Test.tmp'
$version = '0.2.0-rc.3'
$artifactStem = "MediaLock-$version-win-x64"
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

    $rejectedDirtySource = $false
    try {
        & $publishScript -Version $version -OutputRoot $artifactOutputRoot
    }
    catch {
        $rejectedDirtySource = $_.Exception.Message -like '*require a clean Git worktree*'
    }
    Assert-Condition $rejectedDirtySource 'Formal publication must reject a dirty source worktree.'

    & $publishScript -Version $version -OutputRoot $artifactOutputRoot -AllowDirty

    $archivePath = Join-Path $artifactOutputRoot "$artifactStem.zip"
    $manifestPath = Join-Path $artifactOutputRoot "$artifactStem.manifest.json"
    $checksumPath = Join-Path $artifactOutputRoot "$artifactStem.sha256"
    Assert-Condition (Test-Path -LiteralPath $archivePath -PathType Leaf) "Archive was not created: $archivePath"
    Assert-Condition (Test-Path -LiteralPath $manifestPath -PathType Leaf) "Manifest was not created: $manifestPath"
    Assert-Condition (Test-Path -LiteralPath $checksumPath -PathType Leaf) "Checksum was not created: $checksumPath"

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Condition ($manifest.schemaVersion -eq 1) 'Manifest schemaVersion must be 1.'
    Assert-Condition ($manifest.product -eq 'Media Lock') 'Manifest product must be Media Lock.'
    Assert-Condition ($manifest.version -eq $version) "Manifest version must be $version."
    Assert-Condition ($manifest.runtimeIdentifier -eq 'win-x64') 'Manifest runtimeIdentifier must be win-x64.'
    Assert-Condition ($manifest.selfContained -eq $true) 'Manifest must declare a self-contained artifact.'
    Assert-Condition ($manifest.singleFile -eq $true) 'Manifest must declare a single-file artifact.'
    Assert-Condition ($manifest.sourceDirty -eq $true) 'Manifest must disclose that the test artifact used a dirty source tree.'
    Assert-Condition ($manifest.archive.fileName -eq "$artifactStem.zip") 'Manifest archive name is incorrect.'

    $expectedHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($manifest.archive.sha256 -eq $expectedHash) 'Manifest SHA-256 does not match the archive.'
    Assert-Condition ($manifest.archive.sizeBytes -eq (Get-Item -LiteralPath $archivePath).Length) 'Manifest archive size is incorrect.'
    Assert-Condition ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -eq "$expectedHash  $artifactStem.zip") 'Checksum file is incorrect.'

    Expand-Archive -LiteralPath $archivePath -DestinationPath $expandedRoot
    $archiveFiles = @(Get-ChildItem -LiteralPath $expandedRoot -File -Recurse)
    Assert-Condition ($archiveFiles.Count -eq 1) 'Release archive must contain exactly one file.'
    Assert-Condition ($archiveFiles[0].Name -eq 'MediaLock.exe') 'Release archive must contain MediaLock.exe.'
    Assert-Condition ($archiveFiles[0].VersionInfo.ProductVersion -eq $version) "Executable ProductVersion must be $version."
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
