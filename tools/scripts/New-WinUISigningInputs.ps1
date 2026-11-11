# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $OutputDirectory).Path
$files = @(Get-ChildItem -LiteralPath (Join-Path $root 'layout') -File -Recurse |
    Where-Object Extension -In '.exe', '.dll')
$files += Get-Item -LiteralPath (Join-Path $root 'customaction\AccessibilityInsights.CustomActions.CA.dll')
$document = [Xml.XmlDocument]::new()
$project = $document.CreateElement('Project')
$null = $document.AppendChild($project)
$group = $document.CreateElement('ItemGroup')
$null = $project.AppendChild($group)
foreach ($file in $files) {
    $owned = $file.Name.StartsWith('AccessibilityInsights', [StringComparison]::OrdinalIgnoreCase)
    if (!$owned) {
        $status = (Get-AuthenticodeSignature -LiteralPath $file.FullName).Status
        if ($status -eq 'Valid') { continue }
        if ($status -ne 'NotSigned') { throw "Dependency signature validation failed: $($file.FullName) ($status)" }
    }
    $item = $document.CreateElement('FilesToSign')
    $item.SetAttribute('Include', $file.FullName)
    $authenticode = $document.CreateElement('Authenticode')
    $authenticode.InnerText = $(if ($owned) { 'Microsoft400' } else { '3PartySHA2' })
    $null = $item.AppendChild($authenticode)
    $null = $group.AppendChild($item)
}
$document.Save((Join-Path $root 'SignInputs.props'))
Write-Host "Selected $($group.ChildNodes.Count) publish artifacts for protected signing."
