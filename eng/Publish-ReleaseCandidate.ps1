[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-rc\.\d+)?$')]
    [string] $Version,

    [string] $OutputRoot,

    [string] $InnoCompilerPath,

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
        Entries = @($entries)
    }
}

function Resolve-InnoCompiler {
    param(
        [string] $RequestedPath
    )

    $supportedVersion = '6.7.3'
    $candidates = if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        )
    }
    else {
        @($RequestedPath)
    }

    $compilerPath = $candidates |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
    if ($null -eq $compilerPath) {
        throw "Inno Setup $supportedVersion compiler was not found. Install it or pass -InnoCompilerPath."
    }

    $resolvedCompilerPath = (Resolve-Path -LiteralPath $compilerPath).Path
    $uninstaller = Get-ChildItem `
        -LiteralPath (Split-Path -Parent $resolvedCompilerPath) `
        -Filter 'unins*.exe' `
        -File |
        Where-Object { $_.VersionInfo.ProductVersion.Trim() -eq $supportedVersion } |
        Select-Object -First 1
    if ($null -eq $uninstaller) {
        throw "Media Lock release packaging requires Inno Setup ${supportedVersion}: $resolvedCompilerPath"
    }

    [pscustomobject]@{
        Path = $resolvedCompilerPath
        Version = $supportedVersion
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$initialSource = Get-SourceSnapshot -RepositoryRoot $repositoryRoot
$sourceDirty = $initialSource.Dirty
if ($sourceDirty -and -not $AllowDirty) {
    throw 'Release artifacts require a clean Git worktree. Commit the intended source or pass -AllowDirty for a disclosed test artifact.'
}

$innoCompiler = Resolve-InnoCompiler -RequestedPath $InnoCompilerPath

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
$installerStem = "MediaLock-Setup-$Version-win-x64"
$archiveName = "$artifactStem.zip"
$manifestName = "$artifactStem.manifest.json"
$checksumName = "$artifactStem.sha256"
$installerName = "$installerStem.exe"
$installerChecksumName = "$installerStem.sha256"
$finalArchivePath = Join-Path $OutputRoot $archiveName
$finalManifestPath = Join-Path $OutputRoot $manifestName
$finalChecksumPath = Join-Path $OutputRoot $checksumName
$finalInstallerPath = Join-Path $OutputRoot $installerName
$finalInstallerChecksumPath = Join-Path $OutputRoot $installerChecksumName
$finalOutputPaths = @(
    $finalArchivePath,
    $finalManifestPath,
    $finalChecksumPath,
    $finalInstallerPath,
    $finalInstallerChecksumPath)

foreach ($outputPath in $finalOutputPaths) {
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
$stagedInstallerPath = Join-Path $stagingRoot $installerName
$stagedInstallerChecksumPath = Join-Path $stagingRoot $installerChecksumName
$publicationCompleted = $false

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

    $payloadHash = (Get-FileHash -LiteralPath $executable.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Compress-Archive -LiteralPath $executable.FullName -DestinationPath $stagedArchivePath -CompressionLevel Optimal
    $archive = Get-Item -LiteralPath $stagedArchivePath
    $archiveHash = (Get-FileHash -LiteralPath $stagedArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()

    $installerScriptPath = Join-Path $repositoryRoot 'installer\MediaLock.iss'
    & $innoCompiler.Path `
        '/Qp' `
        "/DAppVersion=$Version" `
        "/DBinaryVersion=$binaryVersion" `
        "/DPayloadPath=$($executable.FullName)" `
        "/DOutputDirectory=$stagingRoot" `
        "/DOutputBaseName=$installerStem" `
        $installerScriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $stagedInstallerPath -PathType Leaf)) {
        throw "Inno Setup did not create the expected installer: $stagedInstallerPath"
    }

    $installer = Get-Item -LiteralPath $stagedInstallerPath
    $installerSignature = Get-AuthenticodeSignature -LiteralPath $stagedInstallerPath
    if ($installerSignature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
        throw "Unsigned packaging expected installer signature status NotSigned, found $($installerSignature.Status)."
    }

    if ($installer.VersionInfo.ProductVersion.Trim() -ne $Version) {
        throw "Installer ProductVersion '$($installer.VersionInfo.ProductVersion)' does not match '$Version'."
    }

    $installerHash =
        (Get-FileHash -LiteralPath $stagedInstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $finalSource = Get-SourceSnapshot -RepositoryRoot $repositoryRoot
    if ($finalSource.Commit -ne $initialSource.Commit -or
        $finalSource.Fingerprint -ne $initialSource.Fingerprint) {
        $changedEntries = @(Compare-Object $initialSource.Entries $finalSource.Entries |
            Select-Object -First 10 -ExpandProperty InputObject)
        $changedDescription = if ($changedEntries.Count -gt 0) {
            " Changed entries: $($changedEntries -join ', ')"
        }
        else {
            [string]::Empty
        }
        throw "Source files or HEAD changed during publication; no release output was created.$changedDescription"
    }

    $dotnetSdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not resolve the .NET SDK version.'
    }

    $manifest = [ordered]@{
        schemaVersion = 2
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
        innoSetupVersion = $innoCompiler.Version
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        executable = [ordered]@{
            fileName = $executable.Name
            sizeBytes = $executable.Length
            sha256 = $payloadHash
            productVersion = $executable.VersionInfo.ProductVersion
            fileVersion = $executable.VersionInfo.FileVersion
            signed = $false
        }
        archive = [ordered]@{
            fileName = $archive.Name
            sizeBytes = $archive.Length
            sha256 = $archiveHash
        }
        installer = [ordered]@{
            fileName = $installer.Name
            sizeBytes = $installer.Length
            sha256 = $installerHash
            productVersion = $installer.VersionInfo.ProductVersion.Trim()
            fileVersion = $installer.VersionInfo.FileVersion.Trim()
            signed = $false
        }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $stagedManifestPath -Encoding utf8NoBOM
    "$archiveHash  $archiveName" | Set-Content -LiteralPath $stagedChecksumPath -Encoding ascii -NoNewline
    "$installerHash  $installerName" |
        Set-Content -LiteralPath $stagedInstallerChecksumPath -Encoding ascii -NoNewline

    Move-Item -LiteralPath $stagedArchivePath -Destination $finalArchivePath
    Move-Item -LiteralPath $stagedManifestPath -Destination $finalManifestPath
    Move-Item -LiteralPath $stagedChecksumPath -Destination $finalChecksumPath
    Move-Item -LiteralPath $stagedInstallerPath -Destination $finalInstallerPath
    Move-Item -LiteralPath $stagedInstallerChecksumPath -Destination $finalInstallerChecksumPath
    $publicationCompleted = $true

    [pscustomobject]@{
        Archive = $finalArchivePath
        Manifest = $finalManifestPath
        Checksum = $finalChecksumPath
        Installer = $finalInstallerPath
        InstallerChecksum = $finalInstallerChecksumPath
    }
}
finally {
    if (-not $publicationCompleted) {
        foreach ($outputPath in $finalOutputPaths) {
            if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
                Remove-Item -LiteralPath $outputPath -Force
            }
        }
    }

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
