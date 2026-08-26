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

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $ExpectedMessage
    )

    $actualMessage = $null
    try {
        & $Action
    }
    catch {
        $actualMessage = $_.Exception.Message
    }

    Assert-Condition `
        ($actualMessage -like "*$ExpectedMessage*") `
        "Expected failure containing '$ExpectedMessage', found '$actualMessage'."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$probeRoot = Join-Path $repositoryRoot 'experiments\Phase16A.BrowserDirectProbe'
$registerPath = Join-Path $probeRoot 'Register-Phase16AProbe.ps1'
$unregisterPath = Join-Path $probeRoot 'Unregister-Phase16AProbe.ps1'
$hostName = 'com.command1264.medialock.phase16a'
$testRoot = "HKCU:\Software\MediaLock\Phase16ARegistrationTests\$([Guid]::NewGuid().ToString('N'))"
$registryRoot = Join-Path $testRoot 'NativeMessagingHosts'
$registryPath = Join-Path $registryRoot $hostName
$obsoleteBraveRegistryRoot = Join-Path $testRoot 'ObsoleteBraveNativeMessagingHosts'
$obsoleteBraveRegistryPath = Join-Path $obsoleteBraveRegistryRoot $hostName

try {
    $legacyBraveManifest = Join-Path `
        $repositoryRoot `
        'artifacts\phase16a-browser-direct\brave\native-host-manifest.json'
    $null = New-Item -Path $obsoleteBraveRegistryPath -Force
    Set-Item -LiteralPath $obsoleteBraveRegistryPath -Value $legacyBraveManifest

    $chrome = & $registerPath `
        -Browser Chrome `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot
    $brave = & $registerPath `
        -Browser Brave `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot

    Assert-Condition $chrome.SharedChromiumRegistration `
        'Chrome must report the shared Chromium registration contract.'
    Assert-Condition $brave.SharedChromiumRegistration `
        'Brave must report the shared Chromium registration contract.'
    Assert-Condition `
        ([string]::Equals($chrome.RegistryPath, $brave.RegistryPath, [StringComparison]::Ordinal)) `
        'Chrome and Brave must use the same Native Messaging registry path.'
    Assert-Condition `
        ([string]::Equals(
            $chrome.NativeHostManifest,
            $brave.NativeHostManifest,
            [StringComparison]::Ordinal)) `
        'Chrome and Brave must use the same Native Messaging manifest.'
    Assert-Condition $chrome.ObsoleteBraveRegistrationRemoved `
        'Register must remove its exact obsolete Brave-specific registration.'
    Assert-Condition (!(Test-Path -LiteralPath $obsoleteBraveRegistryPath)) `
        'The exact obsolete Brave-specific registration remained after migration.'

    $removed = & $unregisterPath `
        -Browser Brave `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot
    Assert-Condition $removed.SharedChromiumRegistration `
        'Unregister must report the shared Chromium registration contract.'
    Assert-Condition $removed.OwnedRegistrationRemoved `
        'Unregister must remove the exact shared registration.'
    Assert-Condition (!(Test-Path -LiteralPath $registryPath)) `
        'The shared registration remained after unregister.'

    $legacyManifest = Join-Path `
        $repositoryRoot `
        'artifacts\phase16a-browser-direct\chrome\native-host-manifest.json'
    $null = New-Item -Path $registryPath -Force
    Set-Item -LiteralPath $registryPath -Value $legacyManifest

    $migrated = & $registerPath `
        -Browser Brave `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot
    Assert-Condition $migrated.LegacyRegistrationMigrated `
        'Register must explicitly report migration of its legacy browser-specific manifest.'
    Assert-Condition `
        ([string]::Equals(
            [string](Get-Item -LiteralPath $registryPath).GetValue(''),
            $migrated.NativeHostManifest,
            [StringComparison]::Ordinal)) `
        'Register did not migrate the legacy owned manifest to the shared manifest.'

    $null = & $unregisterPath `
        -Browser Chrome `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot
    $null = New-Item -Path $registryPath -Force
    Set-Item -LiteralPath $registryPath -Value 'C:\foreign\native-host-manifest.json'
    Assert-Throws `
        {
            & $registerPath `
                -Browser Chrome `
                -RegistryRoot $registryRoot `
                -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot
        } `
        'already registered to a different manifest'

    $preserved = & $unregisterPath `
        -Browser Chrome `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot
    Assert-Condition $preserved.ForeignRegistrationPreserved `
        'Unregister must preserve a foreign registration.'
    Assert-Condition (Test-Path -LiteralPath $registryPath) `
        'Unregister removed a foreign registration.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
