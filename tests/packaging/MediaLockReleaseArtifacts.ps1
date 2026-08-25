Set-StrictMode -Version Latest

function ConvertTo-MediaLockReleaseVersion {
    param(
        [Parameter(Mandatory, Position = 0)]
        [string] $Version
    )

    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-rc\.(\d+))?$') {
        throw "Unsupported Media Lock release version: $Version"
    }

    try {
        $major = [Int64]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
        $minor = [Int64]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)
        $patch = [Int64]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
        $isStable = [string]::IsNullOrEmpty($Matches[4])
        $releaseCandidate = if ($isStable) {
            [Int64]0
        }
        else {
            [Int64]::Parse($Matches[4], [Globalization.CultureInfo]::InvariantCulture)
        }
    }
    catch {
        throw "Unsupported Media Lock release version: $Version"
    }

    [pscustomobject]@{
        Text = $Version
        Major = $major
        Minor = $minor
        Patch = $patch
        IsStable = $isStable
        ReleaseCandidate = $releaseCandidate
    }
}

function Compare-MediaLockReleaseVersion {
    param(
        [Parameter(Mandatory, Position = 0)]
        [string] $Left,

        [Parameter(Mandatory, Position = 1)]
        [string] $Right
    )

    $leftVersion = ConvertTo-MediaLockReleaseVersion $Left
    $rightVersion = ConvertTo-MediaLockReleaseVersion $Right

    foreach ($part in @('Major', 'Minor', 'Patch')) {
        if ($leftVersion.$part -lt $rightVersion.$part) {
            return -1
        }

        if ($leftVersion.$part -gt $rightVersion.$part) {
            return 1
        }
    }

    if ($leftVersion.IsStable -and -not $rightVersion.IsStable) {
        return 1
    }

    if (-not $leftVersion.IsStable -and $rightVersion.IsStable) {
        return -1
    }

    if ($leftVersion.ReleaseCandidate -lt $rightVersion.ReleaseCandidate) {
        return -1
    }

    if ($leftVersion.ReleaseCandidate -gt $rightVersion.ReleaseCandidate) {
        return 1
    }

    return 0
}

function Get-MediaLockReleaseArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $ArtifactRoot,

        [Parameter(Mandatory)]
        [string] $Version
    )

    $null = ConvertTo-MediaLockReleaseVersion $Version
    $matches = @(
        Get-ChildItem -LiteralPath $ArtifactRoot -Filter 'MediaLock-*-win-x64.manifest.json' -Recurse |
            Where-Object { -not $_.PSIsContainer } |
            ForEach-Object {
                $manifest = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                if ($manifest.version -eq $Version) {
                    [pscustomobject]@{
                        Manifest = $manifest
                        ManifestPath = $_.FullName
                        Directory = $_.DirectoryName
                    }
                }
            }
    )

    if ($matches.Count -ne 1) {
        throw "Exactly one manifest is required for $Version; found $($matches.Count)."
    }

    $installerPath = Join-Path $matches[0].Directory $matches[0].Manifest.installer.fileName
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Installer declared by the $Version manifest was not found: $installerPath"
    }

    $matches[0]
}

function Get-MediaLockArtifactPair {
    param(
        [Parameter(Mandatory, Position = 0)]
        [string] $ArtifactRoot,

        [Parameter(Mandatory, Position = 1)]
        [string] $OlderVersion,

        [Parameter(Mandatory, Position = 2)]
        [string] $NewerVersion
    )

    if ((Compare-MediaLockReleaseVersion $OlderVersion $NewerVersion) -ge 0) {
        throw "Older version $OlderVersion must be older than newer version $NewerVersion."
    }

    @(
        Get-MediaLockReleaseArtifact -ArtifactRoot $ArtifactRoot -Version $OlderVersion
        Get-MediaLockReleaseArtifact -ArtifactRoot $ArtifactRoot -Version $NewerVersion
    )
}
