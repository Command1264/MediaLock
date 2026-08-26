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
$runningHost = $null
$upgradedPublishRoot = $null

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

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $chrome.NativeHostExecutable
    $startInfo.Arguments = "chrome-extension://$($chrome.ExtensionId)/ --parent-window=0"
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $runningHost = New-Object System.Diagnostics.Process
    $runningHost.StartInfo = $startInfo
    Assert-Condition $runningHost.Start() 'The registration test Native Host did not start.'
    Start-Sleep -Milliseconds 250
    Assert-Condition (!$runningHost.HasExited) `
        'The registration test Native Host exited before the idempotent registration check.'

    $brave = & $registerPath `
        -Browser Brave `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot

    Assert-Condition $chrome.SharedChromiumRegistration `
        'Chrome must report the shared Chromium registration contract.'
    Assert-Condition $brave.SharedChromiumRegistration `
        'Brave must report the shared Chromium registration contract.'
    Assert-Condition $brave.NativeHostPublishReused `
        'The second browser registration must reuse the complete content-addressed Host output.'
    Assert-Condition ($chrome.CommandResponseDelayMilliseconds -eq 0) `
        'The default registration must use a zero command response delay.'
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

    $delayed = & $registerPath `
        -Browser Chrome `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot `
        -CommandResponseDelayMilliseconds 6000
    Assert-Condition (!$runningHost.HasExited) `
        'Registering a delayed Host stopped the already-running Host.'
    Assert-Condition `
        (![string]::Equals(
            $chrome.NativeHostExecutable,
            $delayed.NativeHostExecutable,
            [StringComparison]::OrdinalIgnoreCase)) `
        'A different command response delay must publish to a different executable path.'
    Assert-Condition ($delayed.CommandResponseDelayMilliseconds -eq 6000) `
        'The delayed registration did not report its exact command response delay.'
    $delayedConfigurationPath = Join-Path `
        (Split-Path -Parent $delayed.NativeHostExecutable) `
        'phase16a-native-host.json'
    $delayedConfiguration = Get-Content -LiteralPath $delayedConfigurationPath -Raw | ConvertFrom-Json
    Assert-Condition ($delayedConfiguration.commandResponseDelayMilliseconds -eq 6000) `
        'The delayed Host configuration did not persist its exact command response delay.'

    $nextFingerprint = `
        [Guid]::NewGuid().ToString('N') + `
        [Guid]::NewGuid().ToString('N')
    $upgradedPublishRoot = Join-Path `
        $repositoryRoot `
        "artifacts\phase16a-browser-direct\chromium\native-host\$nextFingerprint"
    $upgraded = & $registerPath `
        -Browser Chrome `
        -RegistryRoot $registryRoot `
        -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot `
        -BuildFingerprint $nextFingerprint
    Assert-Condition (!$runningHost.HasExited) `
        'Registering a new content-addressed Host stopped the already-running Host.'
    Assert-Condition `
        (![string]::Equals(
            $chrome.NativeHostExecutable,
            $upgraded.NativeHostExecutable,
            [StringComparison]::OrdinalIgnoreCase)) `
        'A new Host build fingerprint must publish to a different executable path.'
    $upgradedManifest = Get-Content -LiteralPath $upgraded.NativeHostManifest -Raw | ConvertFrom-Json
    Assert-Condition `
        ([string]::Equals(
            $upgradedManifest.path,
            $upgraded.NativeHostExecutable,
            [StringComparison]::OrdinalIgnoreCase)) `
        'The shared manifest did not switch to the content-addressed Host executable.'
    $upgradedConfigurationPath = Join-Path $upgradedPublishRoot 'phase16a-native-host.json'
    $validUpgradedConfiguration = Get-Content -LiteralPath $upgradedConfigurationPath -Raw
    $invalidCachedConfigurations = @(
        "{ `"extensionId`": `"$($chrome.ExtensionId)`", `"commandResponseDelayMilliseconds`": `"0`" }",
        "{ `"extensionId`": `"$($chrome.ExtensionId)`", `"commandResponseDelayMilliseconds`": 0.4 }",
        "{ `"extensionId`": `"$($chrome.ExtensionId)`", `"commandResponseDelayMilliseconds`": false }"
    )
    foreach ($invalidCachedConfiguration in $invalidCachedConfigurations) {
        [System.IO.File]::WriteAllText(
            $upgradedConfigurationPath,
            $invalidCachedConfiguration,
            [System.Text.UTF8Encoding]::new($false))
        Assert-Throws `
            {
                & $registerPath `
                    -Browser Chrome `
                    -RegistryRoot $registryRoot `
                    -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot `
                    -BuildFingerprint $nextFingerprint
            } `
            'configuration does not match its build identity'
    }
    [System.IO.File]::WriteAllText(
        $upgradedConfigurationPath,
        $validUpgradedConfiguration,
        [System.Text.UTF8Encoding]::new($false))
    Assert-Throws `
        {
            & $registerPath `
                -Browser Chrome `
                -RegistryRoot $registryRoot `
                -ObsoleteBraveRegistryRoot $obsoleteBraveRegistryRoot `
                -BuildFingerprint $nextFingerprint `
                -CommandResponseDelayMilliseconds 1
        } `
        'configuration does not match its build identity'

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
    if ($null -ne $runningHost) {
        if (!$runningHost.HasExited) {
            $runningHost.Kill()
            $runningHost.WaitForExit()
        }
        $runningHost.Dispose()
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
    if ($null -ne $upgradedPublishRoot `
        -and (Test-Path -LiteralPath $upgradedPublishRoot)) {
        Remove-Item -LiteralPath $upgradedPublishRoot -Recurse -Force
    }
}
