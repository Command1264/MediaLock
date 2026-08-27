[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$browserRoot = Join-Path $repositoryRoot 'src\MediaLock.Browser'
$register = Join-Path $browserRoot 'Register-BrowserIntegrationCandidate.ps1'
$unregister = Join-Path $browserRoot 'Unregister-BrowserIntegrationCandidate.ps1'
$testId = [Guid]::NewGuid().ToString('N')
$registryTestRoot = "HKCU:\Software\MediaLock\BrowserRegistrationTests\$testId"
$registryRoot = Join-Path $registryTestRoot 'NativeMessagingHosts'
$artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) "MediaLock-BrowserRegistration-$testId"
$registryPath = Join-Path $registryRoot 'com.command1264.medialock.browser'

try {
    $first = & $register -RegistryRoot $registryRoot -ArtifactRoot $artifactRoot
    $second = & $register -RegistryRoot $registryRoot -ArtifactRoot $artifactRoot
    Assert-Condition $first.CurrentUserOnly 'Registration must remain current-user only.'
    Assert-Condition $first.SharedChromiumRegistration 'Chrome and Brave must share one registration.'
    Assert-Condition $second.NativeHostPublishReused 'Repeated registration must reuse the complete build.'
    Assert-Condition (Test-Path -LiteralPath $first.ExtensionRoot -PathType Container) `
        'Registration did not report the unpacked Extension root.'
    Assert-Condition `
        ([string]::Equals(
            [string](Get-Item -LiteralPath $registryPath).GetValue(''),
            $first.NativeHostManifest,
            [StringComparison]::Ordinal)) `
        'The registry value did not round-trip exactly.'
    $manifest = Get-Content -LiteralPath $first.NativeHostManifest -Raw | ConvertFrom-Json
    Assert-Condition `
        ($manifest.allowed_origins.Count -eq 1 -and
            $manifest.allowed_origins[0] -eq
                'chrome-extension://kggfkkiifnclhhmibdglkbdfbacakemn/') `
        'The manifest must authorize only the fixed Media Lock Extension.'

    $removed = & $unregister -RegistryRoot $registryRoot -ArtifactRoot $artifactRoot
    Assert-Condition $removed.OwnedRegistrationRemoved `
        'Unregister did not remove its exact owned registration.'
    $null = New-Item -Path $registryPath -Force
    Set-Item -LiteralPath $registryPath -Value 'C:\foreign\native-host-manifest.json'
    $preserved = & $unregister -RegistryRoot $registryRoot -ArtifactRoot $artifactRoot
    Assert-Condition $preserved.ForeignRegistrationPreserved `
        'Unregister must preserve a foreign registration.'
}
finally {
    if (Test-Path -LiteralPath $registryTestRoot) {
        Remove-Item -LiteralPath $registryTestRoot -Recurse -Force
    }
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedArtifact = [System.IO.Path]::GetFullPath($artifactRoot)
    if (!$resolvedArtifact.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an unexpected test artifact path: $resolvedArtifact"
    }
    if (Test-Path -LiteralPath $resolvedArtifact) {
        Remove-Item -LiteralPath $resolvedArtifact -Recurse -Force
    }
}
