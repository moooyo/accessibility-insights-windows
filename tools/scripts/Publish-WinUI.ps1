# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#requires -Version 7.0
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$Version = ('1.1.{0}.1' -f ([DateTime]::Today - [DateTime]::new(2017, 1, 1)).Days),
    [string]$NuGetSource,
    [switch]$LayoutOnly,
    [switch]$MsiOnly,
    [switch]$SkipMsiValidation
)
$ErrorActionPreference = 'Stop'
if ($LayoutOnly -and $MsiOnly) { throw 'LayoutOnly and MsiOnly cannot be combined.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository 'artifacts\winui' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$layout = Join-Path $output 'layout'
$customActions = Join-Path $output 'customaction'
$inventory = Join-Path $output 'PublishedFiles.wxs'
$null = [IO.Directory]::CreateDirectory($output)
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}
$restoreArguments = @()
if ($NuGetSource) { $restoreArguments += "-p:RestoreSources=$NuGetSource" }
if (!$MsiOnly) {
    if (Test-Path -LiteralPath $layout) {
        throw "Publish into a new output directory to avoid harvesting stale files: $layout"
    }
    $publishArguments = @('-c', $Configuration, '-p:Platform=x64', '-p:MsiLayout=true',
        "-p:Version=$Version", "-p:FileVersion=$Version", "-p:AssemblyVersion=$Version") + $restoreArguments
    Invoke-DotNet (@('publish', (Join-Path $repository 'src\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj'),
        '-o', $layout) + $publishArguments)
    Invoke-DotNet (@('publish', (Join-Path $repository 'src\AccessibilityInsights.VersionSwitcher.WinUI\AccessibilityInsights.VersionSwitcher.WinUI.csproj'),
        '-o', (Join-Path $layout 'VersionSwitcher')) + $publishArguments)
    Invoke-DotNet (@('build', (Join-Path $repository 'src\AccessibilityInsights.CustomActions.WinUI\CustomActions.WinUI.csproj'),
        '-c', $Configuration, '-p:Platform=x86', '-o', $customActions) + $restoreArguments)
    $mtCommand = Get-Command mt.exe -ErrorAction SilentlyContinue
    $mt = if ($mtCommand) { $mtCommand.Source } else {
        Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Filter mt.exe -Recurse |
            Where-Object FullName -Match '\\x64\\mt\.exe$' |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (!$mt) { throw 'Windows SDK manifest tool mt.exe is required to publish UIAccess apphosts.' }
    $executable = Join-Path $layout 'AccessibilityInsights.exe'
    $fullManifest = Join-Path $output 'WinUI.manifest'
    & $mt -nologo "-inputresource:$executable;#1" "-out:$fullManifest"
    if ($LASTEXITCODE) { throw 'Could not extract the self-contained WinUI activation manifest.' }
    foreach ($state in @('Enabled', 'Disabled')) {
        [xml]$manifest = Get-Content -LiteralPath $fullManifest -Raw
        $privilege = $manifest.SelectSingleNode("//*[local-name()='requestedExecutionLevel']")
        if (!$privilege) {
            $parent = $manifest.DocumentElement
            foreach ($name in @('trustInfo', 'security', 'requestedPrivileges', 'requestedExecutionLevel')) {
                $element = $manifest.CreateElement($name, 'urn:schemas-microsoft-com:asm.v3')
                $null = $parent.AppendChild($element)
                $parent = $element
            }
            $privilege = $parent
        }
        $privilege.SetAttribute('level', 'asInvoker')
        $privilege.SetAttribute('uiAccess', $(if ($state -eq 'Enabled') { 'true' } else { 'false' }))
        $stateManifest = Join-Path $layout "UIAccess_$state.manifest"
        $manifest.Save($stateManifest)
        $apphost = Join-Path $layout "AccessibilityInsights.UIAccess.$state.exe"
        Copy-Item -LiteralPath $executable -Destination $apphost
        & $mt -nologo -manifest $stateManifest "-outputresource:$apphost;#1"
        if ($LASTEXITCODE) { throw "Could not embed the $state UIAccess activation manifest." }
    }
    Copy-Item -LiteralPath (Join-Path $layout 'AccessibilityInsights.UIAccess.Disabled.exe') -Destination $executable -Force
    Copy-Item -LiteralPath (Join-Path $layout 'UIAccess_Disabled.manifest') -Destination (Join-Path $layout 'AccessibilityInsights.exe.manifest')
    Copy-Item -LiteralPath (Join-Path $repository 'src\AccessibilityInsights.WinUI\Scripts\UIAccess.cmd') -Destination $layout
    & (Join-Path $PSScriptRoot 'New-WinUISigningInputs.ps1') -OutputDirectory $output
}
if (!$LayoutOnly) {
    & (Join-Path $PSScriptRoot 'New-WinUIInventory.ps1') -LayoutDirectory $layout -OutputFile $inventory
    $project = Join-Path $repository 'src\MSI.WinUI\MSI.WinUI.wixproj'
    $msiArguments = @("-p:LayoutDir=$layout", "-p:CustomActionDir=$customActions",
        "-p:InventoryFile=$inventory", "-p:BuildVer=$Version", '-p:Platform=x64')
    if ($SkipMsiValidation) {
        Write-Warning 'Windows Installer ICE validation is explicitly skipped. Validate on an authorized release machine.'
        $msiArguments += '-p:SuppressValidation=true'
    }
    Invoke-DotNet (@('build', $project, '-c', $Configuration, '-o', (Join-Path $output "msi\$Version")) + $msiArguments + $restoreArguments)
    Write-Host "Unsigned installer: $(Join-Path $output "msi\$Version\AccessibilityInsights.msi")"
}
Write-Host "WinUI publish layout: $layout"
