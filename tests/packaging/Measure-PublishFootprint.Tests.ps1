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
$publishProfilePath =
    Join-Path $repositoryRoot 'src\MediaLock.App\Properties\PublishProfiles\win-x64.pubxml'

Assert-Condition (Test-Path -LiteralPath $measurementScript -PathType Leaf) `
    "Footprint measurement script was not found: $measurementScript"

$measurementScriptSource = Get-Content -LiteralPath $measurementScript -Raw
$requiredMarkdownContracts = @(
    'Logical processors:',
    'Total physical memory bytes:',
    'Extraction cache bytes',
    'Fresh p95／min／max',
    'Warm p95／min／max',
    '## Raw startup samples',
    'Fresh-cache samples:',
    'Warm-cache samples:'
)
foreach ($requiredMarkdownContract in $requiredMarkdownContracts) {
    Assert-Condition ($measurementScriptSource.Contains($requiredMarkdownContract)) `
        "Markdown report must include: $requiredMarkdownContract"
}

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
