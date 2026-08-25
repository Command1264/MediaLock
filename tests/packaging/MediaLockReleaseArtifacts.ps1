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

function Assert-MediaLockInstallerArtifact {
    param(
        [Parameter(Mandatory)]
        [psobject] $Artifact,

        [ValidatePattern('^[0-9a-fA-F]{64}$')]
        [string] $ExpectedSha256
    )

    $installerFileName = [string]$Artifact.Manifest.installer.fileName
    $manifestSha256 = [string]$Artifact.Manifest.installer.sha256
    if ([string]::IsNullOrWhiteSpace($installerFileName) -or
        $manifestSha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Installer metadata is incomplete for $($Artifact.Manifest.version)."
    }

    $installerPath = Join-Path $Artifact.Directory $installerFileName
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Installer declared by the $($Artifact.Manifest.version) manifest was not found: $installerPath"
    }

    $actualSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::Equals(
        $actualSha256,
        $manifestSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer SHA-256 does not match the $($Artifact.Manifest.version) manifest."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256) -and
        -not [string]::Equals(
            $actualSha256,
            $ExpectedSha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer SHA-256 does not match the pinned digest for $($Artifact.Manifest.version)."
    }

    [pscustomobject]@{
        Path = $installerPath
        Sha256 = $actualSha256
    }
}

function Get-MediaLockUninstallEntries {
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (-not (Test-Path -LiteralPath $uninstallRoot)) {
        return @()
    }

    @(
        Get-ChildItem -LiteralPath $uninstallRoot |
            ForEach-Object { Get-ItemProperty $_.PSPath } |
            Where-Object { $_.DisplayName -eq 'Media Lock' }
    )
}

function Get-MediaLockFileSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required Media Lock transaction file was not found: $Path"
    }

    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-MediaLockUninstallRegistrationSnapshot {
    $snapshots = @(
        Get-MediaLockUninstallEntries |
            ForEach-Object {
                $entry = $_
                $snapshot = [ordered]@{
                    keyName = [string]$entry.PSChildName
                }

                @($entry.PSObject.Properties |
                    Where-Object { $_.Name -notlike 'PS*' } |
                    Sort-Object Name) |
                    ForEach-Object {
                        $snapshot[$_.Name] = $_.Value
                    }

                [pscustomobject]$snapshot
            } |
            Sort-Object keyName
    )

    ConvertTo-Json -InputObject $snapshots -Depth 8 -Compress
}

function Get-MediaLockInstalledStateSnapshot {
    param(
        [Parameter(Mandatory)]
        [string] $InstalledExe,

        [Parameter(Mandatory)]
        [string] $ShortcutPath,

        [Parameter(Mandatory)]
        [string] $RunKey,

        [Parameter(Mandatory)]
        [string] $SettingsPath,

        [Parameter(Mandatory)]
        [string] $StatePath,

        [Parameter(Mandatory)]
        [string] $RetainedMarkerPath
    )

    $startupProperty = (Get-ItemProperty -Path $RunKey).PSObject.Properties['MediaLock']
    [pscustomobject][ordered]@{
        payloadSha256 = Get-MediaLockFileSha256 -Path $InstalledExe
        shortcutSha256 = Get-MediaLockFileSha256 -Path $ShortcutPath
        uninstallRegistration = Get-MediaLockUninstallRegistrationSnapshot
        startupValue = if ($null -eq $startupProperty) { $null } else { [string]$startupProperty.Value }
        settingsSha256 = Get-MediaLockFileSha256 -Path $SettingsPath
        stateSha256 = Get-MediaLockFileSha256 -Path $StatePath
        retainedMarkerSha256 = Get-MediaLockFileSha256 -Path $RetainedMarkerPath
    }
}

function Assert-MediaLockInstalledStateUnchanged {
    param(
        [Parameter(Mandatory)]
        [psobject] $Expected,

        [Parameter(Mandatory)]
        [psobject] $Actual,

        [Parameter(Mandatory)]
        [string] $Context
    )

    $fields = [ordered]@{
        payloadSha256 = 'installed payload'
        shortcutSha256 = 'Start Menu shortcut'
        uninstallRegistration = 'uninstall registration'
        startupValue = 'login-startup command'
        settingsSha256 = 'settings.json'
        stateSha256 = 'state.json'
        retainedMarkerSha256 = 'retained user-data marker'
    }

    foreach ($field in $fields.Keys) {
        if (-not [string]::Equals(
            [string]$Expected.$field,
            [string]$Actual.$field,
            [StringComparison]::Ordinal)) {
            throw "$Context changed the $($fields[$field])."
        }
    }
}
