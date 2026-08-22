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
$publishScript = Join-Path $repositoryRoot 'eng\Publish-ReleaseCandidate.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "MediaLock-Packaging-Test-$([Guid]::NewGuid().ToString('N'))"
$expandedRoot = Join-Path $temporaryRoot 'expanded'
$version = '0.2.0-rc.1'
$artifactStem = "MediaLock-$version-win-x64"

try {
    & $publishScript -Version $version -OutputRoot $temporaryRoot -AllowDirty

    $archivePath = Join-Path $temporaryRoot "$artifactStem.zip"
    $manifestPath = Join-Path $temporaryRoot "$artifactStem.manifest.json"
    $checksumPath = Join-Path $temporaryRoot "$artifactStem.sha256"
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
