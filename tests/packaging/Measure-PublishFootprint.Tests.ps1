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

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$measurementScript = Join-Path $repositoryRoot 'eng\Measure-PublishFootprint.ps1'
$markdownRenderer = Join-Path $repositoryRoot 'eng\FootprintMarkdownReport.ps1'
$publishProfilePath =
    Join-Path $repositoryRoot 'src\MediaLock.App\Properties\PublishProfiles\win-x64.pubxml'

Assert-Condition (Test-Path -LiteralPath $measurementScript -PathType Leaf) `
    "Footprint measurement script was not found: $measurementScript"
Assert-Condition (Test-Path -LiteralPath $markdownRenderer -PathType Leaf) `
    "Footprint Markdown renderer was not found: $markdownRenderer"
. $markdownRenderer

$measuredStatistics = [pscustomobject]@{
    SampleCount = 1
    MedianMilliseconds = 10.5
    P95Milliseconds = 12.25
    MinimumMilliseconds = 9.75
    MaximumMilliseconds = 12.25
    Samples = @(
        [pscustomobject]@{
            Iteration = 1
            ElapsedMilliseconds = 10.5
            ExtractionCacheBytes = 222
        }
    )
}
$warmStatistics = [pscustomobject]@{
    SampleCount = 1
    MedianMilliseconds = 20.5
    P95Milliseconds = 22.25
    MinimumMilliseconds = 19.75
    MaximumMilliseconds = 22.25
    Samples = @(
        [pscustomobject]@{
            Iteration = 1
            ElapsedMilliseconds = 20.5
            ExtractionCacheBytes = 333
        }
    )
}
$reportFixture = [pscustomobject]@{
    DotnetSdkVersion = '10.0.test'
    InnoSetupVersion = '6.7.3'
    SourceCommit = 'fixture-commit'
    SourceDirty = $false
    Environment = [pscustomobject]@{
        Cpu = 'Fixture CPU'
        LogicalProcessorCount = 8
        TotalPhysicalMemoryBytes = 123456
        Windows = 'Fixture Windows'
        DisplayVersion = '25H2'
        FullBuild = '26200.1'
        Architecture = 'X64'
    }
    Measurement = [pscustomobject]@{
        ColdStartDefinition = 'Fixture fresh definition.'
        WarmStartDefinition = 'Fixture warm definition.'
        ColdStartIterations = 15
        WarmStartIterations = 15
        StartupTimeoutSeconds = 30
        AlternatingVariantOrder = $true
    }
    Variants = @(
        [pscustomobject]@{
            Name = 'measured'
            ExecutableBytes = 1000
            ArchiveBytes = 800
            InstallerBytes = 700
            ColdStart = $measuredStatistics
            WarmStart = $warmStatistics
        },
        [pscustomobject]@{
            Name = 'skipped'
            ExecutableBytes = 900
            ArchiveBytes = 750
            InstallerBytes = $null
            ColdStart = $null
            WarmStart = $null
        }
    )
    Comparison = [pscustomobject]@{
        ExecutableReductionPercent = 10
        ArchiveReductionPercent = 6.25
        InstallerReductionPercent = $null
        ColdMedianDeltaMilliseconds = 1.5
        ColdMedianRegressionPercent = 2.5
        WarmMedianDeltaMilliseconds = $null
        WarmMedianRegressionPercent = $null
    }
}
$markdownFixture = ConvertTo-FootprintMarkdown -Report $reportFixture
foreach ($expectedMarkdownContent in @(
    '- Logical processors: 8',
    '- Total physical memory bytes: 123456',
    '- Architecture: X64',
    '| measured | 1000 | 800 | 700 | 333 | 10.5 ms | 12.25／9.75／12.25 ms | 20.5 ms | 22.25／19.75／22.25 ms |',
    '| skipped | 900 | 750 | Skipped | Skipped | Skipped | Skipped | Skipped | Skipped |',
    '- Setup reduction: Skipped',
    '- Fresh-cache median delta: 1.5 ms (2.5%)',
    '- Warm-cache median delta: Skipped',
    '## Raw startup samples',
    '- Warm-cache samples:',
    'iteration=1, elapsed=10.5 ms, extraction-cache=222 bytes',
    'iteration=1, elapsed=20.5 ms, extraction-cache=333 bytes'
)) {
    Assert-Condition ($markdownFixture.Contains($expectedMarkdownContent)) `
        "Rendered Markdown did not include: $expectedMarkdownContent"
}
Assert-Condition (-not $markdownFixture.Contains('System.Object[]')) `
    'Rendered Markdown must flatten warm sample lines instead of stringifying a nested array.'

$publishProfile = [xml](Get-Content -LiteralPath $publishProfilePath -Raw)
$compressionSetting =
    $publishProfile.SelectSingleNode('/Project/PropertyGroup/EnableCompressionInSingleFile')?.InnerText
Assert-Condition ($compressionSetting -eq 'true') `
    'The accepted release profile must enable single-file compression.'

$description = @(& $measurementScript -DescribeOnly)
Assert-Condition ($description.Count -eq 2) `
    'Footprint measurement must compare exactly one baseline and one compression candidate.'

$baseline = $description | Where-Object Name -eq 'baseline' | Select-Object -First 1
$compressed = $description | Where-Object Name -eq 'single-file-compressed' | Select-Object -First 1

Assert-Condition ($null -ne $baseline) 'Footprint measurement must include the current baseline.'
Assert-Condition ($null -ne $compressed) `
    'Footprint measurement must include the single-file compression candidate.'

Assert-Condition ($baseline.PublishSelfContained -eq $true) 'Baseline must remain self-contained.'
Assert-Condition ($baseline.PublishSingleFile -eq $true) 'Baseline must remain single-file.'
Assert-Condition ($baseline.EnableCompressionInSingleFile -eq $false) `
    'Baseline must keep single-file compression disabled.'
Assert-Condition ($compressed.PublishSelfContained -eq $true) `
    'Compression candidate must remain self-contained.'
Assert-Condition ($compressed.PublishSingleFile -eq $true) `
    'Compression candidate must remain single-file.'
Assert-Condition ($compressed.EnableCompressionInSingleFile -eq $true) `
    'Compression candidate must enable single-file compression.'

foreach ($variant in $description) {
    Assert-Condition ($variant.IncludeNativeLibrariesForSelfExtract -eq $true) `
        "$($variant.Name) must retain native-library self extraction."
    Assert-Condition ($variant.PublishTrimmed -eq $false) `
        "$($variant.Name) must not enable unsupported WPF trimming."
    Assert-Condition ($variant.PublishReadyToRun -eq $false) `
        "$($variant.Name) must not trade footprint for ReadyToRun images."
    Assert-Condition ($variant.DefaultColdStartIterations -ge 7) `
        "$($variant.Name) must use at least seven fresh-cache startup samples."
    Assert-Condition ($variant.DefaultWarmStartIterations -ge 7) `
        "$($variant.Name) must use at least seven warm-cache startup samples."
}

$localeDescription = @(& $measurementScript -DescribeOnly -IncludeLocaleCandidates)
Assert-Condition ($localeDescription.Count -eq 4) `
    'Locale exploration must add two candidates without replacing the primary comparison.'

$localeCandidates = @($localeDescription | Where-Object { $null -ne $_.SatelliteResourceLanguages })
Assert-Condition ($localeCandidates.Count -eq 2) `
    'Locale exploration must provide compressed and uncompressed candidates.'
foreach ($variant in $localeCandidates) {
    Assert-Condition ($variant.SatelliteResourceLanguages -eq 'zh-Hant;zh-TW') `
        "$($variant.Name) must retain the supported Traditional Chinese resource cultures."
}

$undersampledRunRejected = $false
try {
    & $measurementScript `
        -OutputRoot $repositoryRoot `
        -ColdStartIterations 6 `
        -SkipStartup `
        -SkipInstaller
}
catch {
    $undersampledRunRejected =
        $_.Exception.Message -like '*greater than or equal to 7*'
}
Assert-Condition $undersampledRunRejected `
    'Footprint measurement must reject a primary run with fewer than seven startup samples.'

Write-Output 'Phase 12B footprint measurement contract passed.'
