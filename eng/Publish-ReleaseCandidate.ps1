[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+-rc\.\d+$')]
    [string] $Version,

    [string] $OutputRoot,

    [switch] $AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-SourceSnapshot {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $commit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not resolve the source Git commit.'
    }

    $status = @(& git -C $RepositoryRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the source Git worktree.'
    }

    $sourcePaths = @(& git -C $RepositoryRoot ls-files --cached --others --exclude-standard) | Sort-Object
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enumerate source files.'
    }

    $entries = foreach ($sourcePath in $sourcePaths) {
        $absolutePath = Join-Path $RepositoryRoot $sourcePath
        $contentHash = if (Test-Path -LiteralPath $absolutePath -PathType Leaf) {
            (& git -C $RepositoryRoot hash-object -- $sourcePath).Trim()
        }
        else {
            '<missing>'
        }

        if ($LASTEXITCODE -ne 0) {
            throw "Could not hash source file: $sourcePath"
        }

        "$sourcePath`t$contentHash"
    }

    $fingerprintBytes = [Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $fingerprint = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($fingerprintBytes)).ToLowerInvariant()
    [pscustomobject]@{
        Commit = $commit
        Dirty = $status.Count -gt 0
        Fingerprint = $fingerprint
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$initialSource = Get-SourceSnapshot -RepositoryRoot $repositoryRoot
$sourceDirty = $initialSource.Dirty
if ($sourceDirty -and -not $AllowDirty) {
    throw 'Release candidates require a clean Git worktree. Commit the intended source or pass -AllowDirty for a disclosed test artifact.'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts'
}
elseif (-not [IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputRoot))
}
else {
    $OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
}

$versionNumbers = $Version.Split('-', 2)[0].Split('.')
$binaryVersion = "$($versionNumbers[0]).$($versionNumbers[1]).$($versionNumbers[2]).0"
$artifactStem = "MediaLock-$Version-win-x64"
$archiveName = "$artifactStem.zip"
$manifestName = "$artifactStem.manifest.json"
$checksumName = "$artifactStem.sha256"
$finalArchivePath = Join-Path $OutputRoot $archiveName
$finalManifestPath = Join-Path $OutputRoot $manifestName
$finalChecksumPath = Join-Path $OutputRoot $checksumName

foreach ($outputPath in @($finalArchivePath, $finalManifestPath, $finalChecksumPath)) {
    if (Test-Path -LiteralPath $outputPath) {
        throw "Release output already exists; choose a new OutputRoot or remove it explicitly: $outputPath"
    }
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$resolvedOutputRoot = (Resolve-Path -LiteralPath $OutputRoot).Path.TrimEnd('\')
$stagingRoot = Join-Path $resolvedOutputRoot ".MediaLock-Publish-$([Guid]::NewGuid().ToString('N'))"
$publishRoot = Join-Path $stagingRoot 'publish'
$stagedArchivePath = Join-Path $stagingRoot $archiveName
$stagedManifestPath = Join-Path $stagingRoot $manifestName
$stagedChecksumPath = Join-Path $stagingRoot $checksumName

try {
    New-Item -ItemType Directory -Path $publishRoot | Out-Null
    $projectPath = Join-Path $repositoryRoot 'src\MediaLock.App\MediaLock.App.csproj'
    & dotnet publish $projectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishRoot `
        -p:PublishProfile=win-x64 `
        -p:Version=$Version `
        -p:AssemblyVersion=$binaryVersion `
        -p:FileVersion=$binaryVersion `
        -p:InformationalVersion=$Version `
        -p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $publishedFiles = @(Get-ChildItem -LiteralPath $publishRoot -File -Recurse)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'MediaLock.exe') {
        $publishedNames = ($publishedFiles | ForEach-Object FullName) -join ', '
        throw "Single-file publish must contain only MediaLock.exe. Found: $publishedNames"
    }

    $executable = $publishedFiles[0]
    if ($executable.VersionInfo.ProductVersion -ne $Version) {
        throw "Published executable ProductVersion '$($executable.VersionInfo.ProductVersion)' does not match '$Version'."
    }

    Compress-Archive -LiteralPath $executable.FullName -DestinationPath $stagedArchivePath -CompressionLevel Optimal
    $archive = Get-Item -LiteralPath $stagedArchivePath
    $archiveHash = (Get-FileHash -LiteralPath $stagedArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $finalSource = Get-SourceSnapshot -RepositoryRoot $repositoryRoot
    if ($finalSource.Commit -ne $initialSource.Commit -or
        $finalSource.Fingerprint -ne $initialSource.Fingerprint) {
        throw 'Source files or HEAD changed during publication; no release output was created.'
    }

    $dotnetSdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not resolve the .NET SDK version.'
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        product = 'Media Lock'
        version = $Version
        runtimeIdentifier = 'win-x64'
        selfContained = $true
        singleFile = $true
        trimmed = $false
        signed = $false
        sourceCommit = $initialSource.Commit
        sourceDirty = $sourceDirty
        dotnetSdkVersion = $dotnetSdkVersion
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        executable = [ordered]@{
            fileName = $executable.Name
            sizeBytes = $executable.Length
            productVersion = $executable.VersionInfo.ProductVersion
            fileVersion = $executable.VersionInfo.FileVersion
        }
        archive = [ordered]@{
            fileName = $archive.Name
            sizeBytes = $archive.Length
            sha256 = $archiveHash
        }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $stagedManifestPath -Encoding utf8NoBOM
    "$archiveHash  $archiveName" | Set-Content -LiteralPath $stagedChecksumPath -Encoding ascii -NoNewline

    Move-Item -LiteralPath $stagedArchivePath -Destination $finalArchivePath
    Move-Item -LiteralPath $stagedManifestPath -Destination $finalManifestPath
    Move-Item -LiteralPath $stagedChecksumPath -Destination $finalChecksumPath

    [pscustomobject]@{
        Archive = $finalArchivePath
        Manifest = $finalManifestPath
        Checksum = $finalChecksumPath
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        $resolvedStagingRoot = (Resolve-Path -LiteralPath $stagingRoot).Path
        if (-not $resolvedStagingRoot.StartsWith(
            $resolvedOutputRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove staging output outside OutputRoot: $resolvedStagingRoot"
        }

        Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
    }
}
