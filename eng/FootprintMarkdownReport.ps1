Set-StrictMode -Version Latest

function Format-FootprintStartupRange {
    param(
        [AllowNull()]
        $Statistics
    )

    if ($null -eq $Statistics) {
        return 'Skipped'
    }

    return "$($Statistics.P95Milliseconds)／$($Statistics.MinimumMilliseconds)／$($Statistics.MaximumMilliseconds) ms"
}

function Format-FootprintComparison {
    param(
        [AllowNull()]
        $Value,

        [Parameter(Mandatory)]
        [scriptblock] $Formatter
    )

    if ($null -eq $Value) {
        return 'Skipped'
    }

    return & $Formatter $Value
}

function ConvertTo-FootprintSampleSection {
    param(
        [Parameter(Mandatory)]
        [string] $Label,

        [AllowNull()]
        $Statistics
    )

    $lines = @("- ${Label} samples:")
    if ($null -eq $Statistics) {
        return @($lines + 'Skipped')
    }

    $lines += '```text'
    foreach ($sample in $Statistics.Samples) {
        $lines += "iteration=$($sample.Iteration), elapsed=$($sample.ElapsedMilliseconds) ms, extraction-cache=$($sample.ExtractionCacheBytes) bytes"
    }
    $lines += '```'
    return @($lines)
}

function ConvertTo-FootprintMarkdown {
    param(
        [Parameter(Mandatory)]
        $Report
    )

    $markdown = @(
        '# Media Lock Phase 12B footprint measurement',
        '',
        "- CPU: $($Report.Environment.Cpu)",
        "- Logical processors: $($Report.Environment.LogicalProcessorCount)",
        "- Total physical memory bytes: $($Report.Environment.TotalPhysicalMemoryBytes)",
        "- Windows: $($Report.Environment.Windows) $($Report.Environment.DisplayVersion) ($($Report.Environment.FullBuild))",
        "- Architecture: $($Report.Environment.Architecture)",
        "- .NET SDK: $($Report.DotnetSdkVersion)",
        "- Inno Setup: $($Report.InnoSetupVersion)",
        "- Source commit: ``$($Report.SourceCommit)``",
        "- Source dirty: $($Report.SourceDirty)",
        "- Fresh-cache iterations: $($Report.Measurement.ColdStartIterations)",
        "- Warm-cache iterations: $($Report.Measurement.WarmStartIterations)",
        "- Startup timeout: $($Report.Measurement.StartupTimeoutSeconds) seconds",
        "- Alternating variant order: $($Report.Measurement.AlternatingVariantOrder)",
        "- Fresh-cache definition: $($Report.Measurement.ColdStartDefinition)",
        "- Warm-cache definition: $($Report.Measurement.WarmStartDefinition)",
        '',
        '| Variant | EXE bytes | ZIP bytes | Setup bytes | Extraction cache bytes | Fresh median | Fresh p95／min／max | Warm median | Warm p95／min／max |',
        '|---|---:|---:|---:|---:|---:|---:|---:|---:|'
    )

    foreach ($variantResult in $Report.Variants) {
        $installerBytes = if ($null -eq $variantResult.InstallerBytes) {
            'Skipped'
        }
        else {
            $variantResult.InstallerBytes
        }
        $coldMedian = if ($null -eq $variantResult.ColdStart) {
            'Skipped'
        }
        else {
            "$($variantResult.ColdStart.MedianMilliseconds) ms"
        }
        $warmMedian = if ($null -eq $variantResult.WarmStart) {
            'Skipped'
        }
        else {
            "$($variantResult.WarmStart.MedianMilliseconds) ms"
        }
        $startupSamples = @(
            if ($null -ne $variantResult.ColdStart) { $variantResult.ColdStart.Samples }
            if ($null -ne $variantResult.WarmStart) { $variantResult.WarmStart.Samples }
        )
        $extractionCacheBytes = if ($startupSamples.Count -eq 0) {
            'Skipped'
        }
        else {
            ($startupSamples | Measure-Object -Property ExtractionCacheBytes -Maximum).Maximum
        }
        $coldRange = Format-FootprintStartupRange -Statistics $variantResult.ColdStart
        $warmRange = Format-FootprintStartupRange -Statistics $variantResult.WarmStart
        $markdown += "| $($variantResult.Name) | $($variantResult.ExecutableBytes) | $($variantResult.ArchiveBytes) | $installerBytes | $extractionCacheBytes | $coldMedian | $coldRange | $warmMedian | $warmRange |"
    }

    $installerReduction = Format-FootprintComparison `
        -Value $Report.Comparison.InstallerReductionPercent `
        -Formatter { param($value) "$value%" }
    $coldMedianComparison = Format-FootprintComparison `
        -Value $Report.Comparison.ColdMedianDeltaMilliseconds `
        -Formatter {
            param($value)
            "$value ms ($($Report.Comparison.ColdMedianRegressionPercent)%)"
        }
    $warmMedianComparison = Format-FootprintComparison `
        -Value $Report.Comparison.WarmMedianDeltaMilliseconds `
        -Formatter {
            param($value)
            "$value ms ($($Report.Comparison.WarmMedianRegressionPercent)%)"
        }
    $markdown += @(
        '',
        '## Comparison',
        '',
        "- EXE reduction: $($Report.Comparison.ExecutableReductionPercent)%",
        "- ZIP reduction: $($Report.Comparison.ArchiveReductionPercent)%",
        "- Setup reduction: $installerReduction",
        "- Fresh-cache median delta: $coldMedianComparison",
        "- Warm-cache median delta: $warmMedianComparison",
        '',
        '## Raw startup samples'
    )

    foreach ($variantResult in $Report.Variants) {
        $markdown += @('', "### $($variantResult.Name)", '')
        $markdown += @(ConvertTo-FootprintSampleSection -Label 'Fresh-cache' -Statistics $variantResult.ColdStart)
        $markdown += @('', @(ConvertTo-FootprintSampleSection -Label 'Warm-cache' -Statistics $variantResult.WarmStart))
    }

    $markdown += @(
        '',
        '> This benchmark does not flush the Windows file cache. A reboot-based cold-start comparison is preferred manual evidence; an explicitly accepted waiver must be recorded separately.'
    )

    return ($markdown -join [Environment]::NewLine) + [Environment]::NewLine
}
