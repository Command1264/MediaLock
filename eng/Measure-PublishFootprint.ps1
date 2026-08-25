[CmdletBinding(DefaultParameterSetName = 'Measure')]
param(
    [Parameter(ParameterSetName = 'Describe')]
    [switch] $DescribeOnly,

    [Parameter(ParameterSetName = 'Measure')]
    [string] $OutputRoot,

    [Parameter(ParameterSetName = 'Measure')]
    [ValidateRange(7, 50)]
    [int] $ColdStartIterations = 7,

    [Parameter(ParameterSetName = 'Measure')]
    [ValidateRange(7, 50)]
    [int] $WarmStartIterations = 7,

    [Parameter(ParameterSetName = 'Measure')]
    [ValidateRange(5, 120)]
    [int] $StartupTimeoutSeconds = 30,

    [Parameter(ParameterSetName = 'Measure')]
    [string] $InnoCompilerPath,

    [Parameter(ParameterSetName = 'Measure')]
    [switch] $SkipStartup,

    [Parameter(ParameterSetName = 'Measure')]
    [switch] $SkipInstaller,

    [switch] $IncludeLocaleCandidates
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'FootprintMarkdownReport.ps1')

function New-VariantDescription {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [bool] $CompressionEnabled,

        [AllowNull()]
        $SatelliteResourceLanguages
    )

    [pscustomobject]@{
        Name = $Name
        PublishSelfContained = $true
        PublishSingleFile = $true
        IncludeNativeLibrariesForSelfExtract = $true
        EnableCompressionInSingleFile = $CompressionEnabled
        PublishTrimmed = $false
        PublishReadyToRun = $false
        SatelliteResourceLanguages = $SatelliteResourceLanguages
        DefaultColdStartIterations = 7
        DefaultWarmStartIterations = 7
    }
}

function Get-VariantDescriptions {
    param(
        [bool] $IncludeLocales
    )

    $descriptions = @(
        New-VariantDescription `
            -Name 'baseline' `
            -CompressionEnabled $false `
            -SatelliteResourceLanguages $null
        New-VariantDescription `
            -Name 'single-file-compressed' `
            -CompressionEnabled $true `
            -SatelliteResourceLanguages $null
    )

    if ($IncludeLocales) {
        $descriptions += @(
            New-VariantDescription `
                -Name 'supported-locales' `
                -CompressionEnabled $false `
                -SatelliteResourceLanguages 'zh-Hant;zh-TW'
            New-VariantDescription `
                -Name 'supported-locales-single-file-compressed' `
                -CompressionEnabled $true `
                -SatelliteResourceLanguages 'zh-Hant;zh-TW'
        )
    }

    $descriptions
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

    $compiler = $candidates |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            (Test-Path -LiteralPath $_ -PathType Leaf)
        } |
        Select-Object -First 1
    if ($null -eq $compiler) {
        throw "Inno Setup $supportedVersion was not found. Install the pinned compiler, pass -InnoCompilerPath, or use -SkipInstaller."
    }

    $resolvedCompilerPath = (Resolve-Path -LiteralPath $compiler).Path
    $uninstaller = Get-ChildItem `
        -LiteralPath (Split-Path -Parent $resolvedCompilerPath) `
        -Filter 'unins*.exe' `
        -File |
        Where-Object { $_.VersionInfo.ProductVersion.Trim() -eq $supportedVersion } |
        Select-Object -First 1
    if ($null -eq $uninstaller) {
        throw "Footprint comparison requires Inno Setup ${supportedVersion}: $resolvedCompilerPath"
    }

    [pscustomobject]@{
        Path = $resolvedCompilerPath
        Version = $supportedVersion
    }
}

function Get-DirectorySize {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return [long] 0
    }

    $measurement = Get-ChildItem -LiteralPath $Path -File -Recurse |
        Measure-Object -Property Length -Sum
    if ($null -eq $measurement.Sum) {
        return [long] 0
    }

    [long] $measurement.Sum
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)]
        [double[]] $Values,

        [Parameter(Mandatory)]
        [ValidateRange(0, 100)]
        [double] $Percentile
    )

    if ($Values.Count -eq 0) {
        return $null
    }

    $ordered = @($Values | Sort-Object)
    $index = [Math]::Ceiling(($Percentile / 100) * $ordered.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($ordered.Count - 1, $index))
    [Math]::Round($ordered[$index], 2)
}

function Get-StartupStatistics {
    param(
        [Parameter(Mandatory)]
        [object[]] $Samples
    )

    if ($Samples.Count -eq 0) {
        return $null
    }

    [double[]] $milliseconds = @($Samples | ForEach-Object ElapsedMilliseconds)
    [pscustomobject]@{
        SampleCount = $milliseconds.Count
        MedianMilliseconds = Get-Percentile -Values $milliseconds -Percentile 50
        P95Milliseconds = Get-Percentile -Values $milliseconds -Percentile 95
        MinimumMilliseconds = [Math]::Round(($milliseconds | Measure-Object -Minimum).Minimum, 2)
        MaximumMilliseconds = [Math]::Round(($milliseconds | Measure-Object -Maximum).Maximum, 2)
        Samples = @($Samples)
    }
}

function Invoke-StartupSample {
    param(
        [Parameter(Mandatory)]
        [string] $ExecutablePath,

        [Parameter(Mandatory)]
        [string] $ExtractionRoot,

        [Parameter(Mandatory)]
        [int] $TimeoutSeconds,

        [Parameter(Mandatory)]
        [string] $Variant,

        [Parameter(Mandatory)]
        [string] $Kind,

        [Parameter(Mandatory)]
        [int] $Iteration
    )

    $existingProcesses = @(Get-Process -Name MediaLock -ErrorAction SilentlyContinue)
    if ($existingProcesses.Count -gt 0) {
        $processIds = $existingProcesses.Id -join ', '
        throw "Startup measurement requires Media Lock to be closed. Active process IDs: $processIds"
    }

    New-Item -ItemType Directory -Path $ExtractionRoot -Force | Out-Null
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $ExtractionRoot

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start $Variant for the $Kind startup sample."
    }

    try {
        $timeoutMilliseconds = $TimeoutSeconds * 1000
        if (-not $process.WaitForInputIdle($timeoutMilliseconds)) {
            throw "$Variant did not reach an idle UI state within $TimeoutSeconds seconds."
        }

        do {
            $process.Refresh()
            if ($process.HasExited) {
                throw "$Variant exited before its main window became available."
            }

            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                break
            }

            Start-Sleep -Milliseconds 10
        }
        while ($stopwatch.ElapsedMilliseconds -lt $timeoutMilliseconds)

        if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "$Variant did not create a main window within $TimeoutSeconds seconds."
        }

        $stopwatch.Stop()
        [pscustomobject]@{
            Variant = $Variant
            Kind = $Kind
            Iteration = $Iteration
            ElapsedMilliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
            ExtractionCacheBytes = Get-DirectorySize -Path $ExtractionRoot
        }
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit(5000) | Out-Null
        }

        $process.Dispose()
        Start-Sleep -Milliseconds 500
    }
}

$variants = @(Get-VariantDescriptions -IncludeLocales $IncludeLocaleCandidates)
if ($DescribeOnly) {
    $variants
    return
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $timestamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')
    $OutputRoot = Join-Path $repositoryRoot "artifacts\phase-12b-footprint-$timestamp"
}
elseif (-not [IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputRoot))
}
else {
    $OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
}

if (Test-Path -LiteralPath $OutputRoot) {
    throw "Measurement output already exists: $OutputRoot"
}

if (-not $SkipStartup) {
    $activeProcesses = @(Get-Process -Name MediaLock -ErrorAction SilentlyContinue)
    if ($activeProcesses.Count -gt 0) {
        throw 'Close Media Lock before running startup measurements.'
    }
}

$compiler = if ($SkipInstaller) { $null } else { Resolve-InnoCompiler -RequestedPath $InnoCompilerPath }
$projectPath = Join-Path $repositoryRoot 'src\MediaLock.App\MediaLock.App.csproj'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$binaryVersion = $project.SelectSingleNode('/Project/PropertyGroup/FileVersion').InnerText
$installerScript = Join-Path $repositoryRoot 'installer\MediaLock.iss'

New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$variantResults = @()

foreach ($variant in $variants) {
    $variantRoot = Join-Path $OutputRoot $variant.Name
    $publishRoot = Join-Path $variantRoot 'publish'
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

    $publishArguments = @(
        'publish',
        $projectPath,
        '--configuration',
        'Release',
        '--runtime',
        'win-x64',
        '--self-contained',
        'true',
        '--output',
        $publishRoot,
        '-p:PublishProfile=win-x64',
        "-p:EnableCompressionInSingleFile=$($variant.EnableCompressionInSingleFile.ToString().ToLowerInvariant())",
        "-p:Version=$version",
        "-p:AssemblyVersion=$binaryVersion",
        "-p:FileVersion=$binaryVersion",
        "-p:InformationalVersion=$version",
        '-p:DebugType=embedded'
    )
    if (-not [string]::IsNullOrWhiteSpace($variant.SatelliteResourceLanguages)) {
        $escapedLanguages = $variant.SatelliteResourceLanguages.Replace(';', '%3B')
        $publishArguments += "-p:SatelliteResourceLanguages=$escapedLanguages"
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($variant.Name) with exit code $LASTEXITCODE."
    }

    $publishedFiles = @(Get-ChildItem -LiteralPath $publishRoot -File -Recurse)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'MediaLock.exe') {
        $publishedNames = ($publishedFiles | ForEach-Object FullName) -join ', '
        throw "$($variant.Name) must publish exactly one MediaLock.exe. Found: $publishedNames"
    }

    $executable = $publishedFiles[0]
    $archivePath = Join-Path $variantRoot "MediaLock-$($variant.Name).zip"
    Compress-Archive -LiteralPath $executable.FullName -DestinationPath $archivePath -CompressionLevel Optimal

    $installerPath = $null
    if (-not $SkipInstaller) {
        $installerBaseName = "MediaLock-Setup-$($variant.Name)"
        & $compiler.Path `
            '/Qp' `
            "/DAppVersion=$version" `
            "/DBinaryVersion=$binaryVersion" `
            "/DPayloadPath=$($executable.FullName)" `
            "/DOutputDirectory=$variantRoot" `
            "/DOutputBaseName=$installerBaseName" `
            $installerScript
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup failed for $($variant.Name) with exit code $LASTEXITCODE."
        }

        $installerPath = Join-Path $variantRoot "$installerBaseName.exe"
        if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
            throw "Inno Setup did not create the expected installer: $installerPath"
        }
    }

    $variantResults += [pscustomobject]@{
        Name = $variant.Name
        EnableCompressionInSingleFile = $variant.EnableCompressionInSingleFile
        SatelliteResourceLanguages = $variant.SatelliteResourceLanguages
        ExecutablePath = $executable.FullName
        ExecutableBytes = [long] $executable.Length
        ArchivePath = $archivePath
        ArchiveBytes = [long] (Get-Item -LiteralPath $archivePath).Length
        InstallerPath = $installerPath
        InstallerBytes = if ($null -eq $installerPath) { $null } else { [long] (Get-Item -LiteralPath $installerPath).Length }
        ColdStart = $null
        WarmStart = $null
    }
}

if (-not $SkipStartup) {
    $coldSamples = @()
    for ($iteration = 1; $iteration -le $ColdStartIterations; $iteration++) {
        $orderedVariants = if ($iteration % 2 -eq 1) {
            @($variantResults[0], $variantResults[1])
        }
        else {
            @($variantResults[1], $variantResults[0])
        }

        foreach ($variantResult in $orderedVariants) {
            $cacheRoot = Join-Path $OutputRoot "startup-cache\cold-$iteration-$($variantResult.Name)"
            $coldSamples += Invoke-StartupSample `
                -ExecutablePath $variantResult.ExecutablePath `
                -ExtractionRoot $cacheRoot `
                -TimeoutSeconds $StartupTimeoutSeconds `
                -Variant $variantResult.Name `
                -Kind 'fresh-extraction-cache' `
                -Iteration $iteration
        }
    }

    $warmCacheRoots = @{}
    foreach ($variantResult in $variantResults) {
        $warmCacheRoot = Join-Path $OutputRoot "startup-cache\warm-$($variantResult.Name)"
        $warmCacheRoots[$variantResult.Name] = $warmCacheRoot
        $null = Invoke-StartupSample `
            -ExecutablePath $variantResult.ExecutablePath `
            -ExtractionRoot $warmCacheRoot `
            -TimeoutSeconds $StartupTimeoutSeconds `
            -Variant $variantResult.Name `
            -Kind 'warm-up' `
            -Iteration 0
    }

    $warmSamples = @()
    for ($iteration = 1; $iteration -le $WarmStartIterations; $iteration++) {
        $orderedVariants = if ($iteration % 2 -eq 1) {
            @($variantResults[1], $variantResults[0])
        }
        else {
            @($variantResults[0], $variantResults[1])
        }

        foreach ($variantResult in $orderedVariants) {
            $warmSamples += Invoke-StartupSample `
                -ExecutablePath $variantResult.ExecutablePath `
                -ExtractionRoot $warmCacheRoots[$variantResult.Name] `
                -TimeoutSeconds $StartupTimeoutSeconds `
                -Variant $variantResult.Name `
                -Kind 'warm-extraction-cache' `
                -Iteration $iteration
        }
    }

    foreach ($variantResult in $variantResults) {
        $variantResult.ColdStart = Get-StartupStatistics -Samples @(
            $coldSamples | Where-Object Variant -eq $variantResult.Name)
        $variantResult.WarmStart = Get-StartupStatistics -Samples @(
            $warmSamples | Where-Object Variant -eq $variantResult.Name)
    }
}

$baseline = $variantResults | Where-Object Name -eq 'baseline' | Select-Object -First 1
$compressed = $variantResults | Where-Object Name -eq 'single-file-compressed' | Select-Object -First 1
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$operatingSystem = Get-CimInstance Win32_OperatingSystem
$currentVersion = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'

$comparison = [pscustomobject]@{
    ExecutableReductionPercent = [Math]::Round(
        (1 - ($compressed.ExecutableBytes / $baseline.ExecutableBytes)) * 100,
        2)
    ArchiveReductionPercent = [Math]::Round(
        (1 - ($compressed.ArchiveBytes / $baseline.ArchiveBytes)) * 100,
        2)
    InstallerReductionPercent = if ($SkipInstaller) {
        $null
    }
    else {
        [Math]::Round((1 - ($compressed.InstallerBytes / $baseline.InstallerBytes)) * 100, 2)
    }
    ColdMedianDeltaMilliseconds = if ($SkipStartup) {
        $null
    }
    else {
        [Math]::Round(
            $compressed.ColdStart.MedianMilliseconds - $baseline.ColdStart.MedianMilliseconds,
            2)
    }
    ColdMedianRegressionPercent = if ($SkipStartup) {
        $null
    }
    else {
        [Math]::Round(
            (($compressed.ColdStart.MedianMilliseconds / $baseline.ColdStart.MedianMilliseconds) - 1) * 100,
            2)
    }
    WarmMedianDeltaMilliseconds = if ($SkipStartup) {
        $null
    }
    else {
        [Math]::Round(
            $compressed.WarmStart.MedianMilliseconds - $baseline.WarmStart.MedianMilliseconds,
            2)
    }
    WarmMedianRegressionPercent = if ($SkipStartup) {
        $null
    }
    else {
        [Math]::Round(
            (($compressed.WarmStart.MedianMilliseconds / $baseline.WarmStart.MedianMilliseconds) - 1) * 100,
            2)
    }
}

$sourceStatus = @(& git -C $repositoryRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the source Git worktree.'
}

$report = [ordered]@{
    SchemaVersion = 1
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    SourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    SourceDirty = $sourceStatus.Count -gt 0
    DotnetSdkVersion = (& dotnet --version).Trim()
    InnoSetupVersion = if ($SkipInstaller) { $null } else { $compiler.Version }
    Environment = [ordered]@{
        Cpu = $cpu.Name.Trim()
        LogicalProcessorCount = $cpu.NumberOfLogicalProcessors
        TotalPhysicalMemoryBytes = [long] $operatingSystem.TotalVisibleMemorySize * 1KB
        Windows = $operatingSystem.Caption
        DisplayVersion = $currentVersion.DisplayVersion
        FullBuild = "$($currentVersion.CurrentBuild).$($currentVersion.UBR)"
        Architecture = $operatingSystem.OSArchitecture
    }
    Measurement = [ordered]@{
        ColdStartDefinition = 'Main-window readiness with a new DOTNET_BUNDLE_EXTRACT_BASE_DIR for every sample.'
        WarmStartDefinition = 'Main-window readiness after one excluded warm-up using a persistent extraction cache.'
        ColdStartIterations = if ($SkipStartup) { 0 } else { $ColdStartIterations }
        WarmStartIterations = if ($SkipStartup) { 0 } else { $WarmStartIterations }
        StartupTimeoutSeconds = $StartupTimeoutSeconds
        AlternatingVariantOrder = $true
    }
    Variants = @($variantResults)
    Comparison = $comparison
}

$jsonPath = Join-Path $OutputRoot 'phase-12b-footprint.json'
$markdownPath = Join-Path $OutputRoot 'phase-12b-footprint.md'
$json = $report | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($jsonPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

$markdown = ConvertTo-FootprintMarkdown -Report $report
[IO.File]::WriteAllText(
    $markdownPath,
    $markdown,
    [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    OutputRoot = $OutputRoot
    JsonReport = $jsonPath
    MarkdownReport = $markdownPath
    Comparison = $comparison
}
