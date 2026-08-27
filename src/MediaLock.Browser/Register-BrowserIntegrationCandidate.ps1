[CmdletBinding()]
param(
    [Parameter(DontShow)]
    [string] $RegistryRoot = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts',

    [Parameter(DontShow)]
    [string] $ArtifactRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$hostName = 'com.command1264.medialock.browser'
$extensionId = 'kggfkkiifnclhhmibdglkbdfbacakemn'
$pipeName = 'Command1264.MediaLock.Browser.v1'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repositoryRoot 'artifacts\browser-integration-candidate'
}
$ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
$projectPath = Join-Path $repositoryRoot 'src\MediaLock.BrowserHost\MediaLock.BrowserHost.csproj'
$fingerprintFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src\MediaLock.Browser') -File -Recurse |
        Where-Object {
            $_.Extension -in '.cs', '.csproj', '.json' -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        }
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src\MediaLock.BrowserHost') -File -Recurse |
        Where-Object {
            $_.Extension -in '.cs', '.csproj' -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        }
)
$fingerprintLines = foreach ($file in $fingerprintFiles | Sort-Object FullName) {
    $relativePath = [System.IO.Path]::GetRelativePath($repositoryRoot, $file.FullName)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    "$relativePath`:$hash"
}
$fingerprintBytes = [System.Text.Encoding]::UTF8.GetBytes(
    [string]::Join("`n", $fingerprintLines))
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $fingerprint = [Convert]::ToHexString(
        $sha256.ComputeHash($fingerprintBytes)).ToLowerInvariant()
}
finally {
    $sha256.Dispose()
}

$publishRoot = Join-Path (Join-Path $ArtifactRoot 'native-host') $fingerprint
$manifestPath = Join-Path $ArtifactRoot 'native-host-manifest.json'
$registryPath = Join-Path $RegistryRoot $hostName
if (Test-Path -LiteralPath $registryPath) {
    $existing = [string](Get-Item -LiteralPath $registryPath).GetValue('')
    if (![string]::Equals($existing, $manifestPath, [StringComparison]::Ordinal)) {
        throw "The Native Messaging host is already registered to a different manifest: $existing"
    }
}

$hostExecutable = Join-Path $publishRoot 'MediaLock.BrowserHost.exe'
$configurationPath = Join-Path $publishRoot 'browser-host.json'
$publishReused = Test-Path -LiteralPath $hostExecutable -PathType Leaf
if (!$publishReused) {
    dotnet publish $projectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        --output $publishRoot |
        Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Media Lock Browser Host publish failed with exit code $LASTEXITCODE."
    }
    $configuration = [ordered]@{
        extensionId = $extensionId
        pipeName = $pipeName
    }
    [System.IO.File]::WriteAllText(
        $configurationPath,
        (($configuration | ConvertTo-Json -Depth 2) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
}
elseif (!(Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
    throw "The cached Browser Host output is incomplete: $publishRoot"
}
$existingConfiguration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
$configurationProperties = @($existingConfiguration.PSObject.Properties.Name)
if ($configurationProperties.Count -ne 2 -or
    $configurationProperties -notcontains 'extensionId' -or
    $configurationProperties -notcontains 'pipeName' -or
    ![string]::Equals($existingConfiguration.extensionId, $extensionId, [StringComparison]::Ordinal) -or
    ![string]::Equals($existingConfiguration.pipeName, $pipeName, [StringComparison]::Ordinal)) {
    throw "The cached Browser Host configuration does not match its build identity: $publishRoot"
}

$hostExecutable = (Resolve-Path -LiteralPath $hostExecutable).Path
$manifest = [ordered]@{
    name = $hostName
    description = 'Media Lock page-level browser media bridge candidate'
    path = $hostExecutable
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}
$null = New-Item -ItemType Directory -Path $ArtifactRoot -Force
[System.IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 4) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))
$null = New-Item -Path $registryPath -Force
Set-Item -LiteralPath $registryPath -Value $manifestPath

[pscustomobject]@{
    ExtensionId = $extensionId
    ExtensionRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'Extension')).Path
    NativeHostExecutable = $hostExecutable
    NativeHostManifest = $manifestPath
    NativeHostBuildFingerprint = $fingerprint
    NativeHostPublishReused = $publishReused
    RegistryPath = $registryPath
    SharedChromiumRegistration = $true
    CurrentUserOnly = $true
    CandidateOnly = $true
}
