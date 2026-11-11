# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$LayoutDirectory,
    [Parameter(Mandatory)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $LayoutDirectory).Path
$files = @(Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName)
if (!$files.Count -or !(Test-Path -LiteralPath (Join-Path $root 'AccessibilityInsights.exe'))) {
    throw 'The publish layout must contain AccessibilityInsights.exe.'
}
if ($files.Name -contains 'PresentationFramework.dll' -or $files.Name -contains 'PresentationCore.dll') {
    throw 'WPF assemblies cannot be shipped in the WinUI publish layout.'
}
function Get-StableHash([string]$Value) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant()))).Substring(0, 32)
}
$document = [Xml.XmlDocument]::new()
$namespace = 'http://wixtoolset.org/schemas/v4/wxs'
$wix = $document.CreateElement('Wix', $namespace)
$null = $document.AppendChild($wix)
$fragment = $document.CreateElement('Fragment', $namespace)
$null = $wix.AppendChild($fragment)
$directoryRoot = $document.CreateElement('DirectoryRef', $namespace)
$directoryRoot.SetAttribute('Id', 'INSTALLFOLDER')
$null = $fragment.AppendChild($directoryRoot)
$group = $document.CreateElement('ComponentGroup', $namespace)
$group.SetAttribute('Id', 'PublishedFiles')
$null = $fragment.AppendChild($group)
$directories = @{ '' = $directoryRoot }
function Get-DirectoryNode([string]$RelativePath) {
    if ($directories.ContainsKey($RelativePath)) { return $directories[$RelativePath] }
    $parentPath = [IO.Path]::GetDirectoryName($RelativePath)
    if ($null -eq $parentPath) { $parentPath = '' }
    $parent = Get-DirectoryNode $parentPath
    $node = $document.CreateElement('Directory', $namespace)
    $node.SetAttribute('Id', 'D' + (Get-StableHash $RelativePath))
    $node.SetAttribute('Name', [IO.Path]::GetFileName($RelativePath))
    $null = $parent.AppendChild($node)
    $directories[$RelativePath] = $node
    return $node
}
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
    $hash = Get-StableHash $relative
    $parentPath = [IO.Path]::GetDirectoryName($relative)
    if ($null -eq $parentPath) { $parentPath = '' }
    $parent = Get-DirectoryNode $parentPath
    $component = $document.CreateElement('Component', $namespace)
    $component.SetAttribute('Id', "C$hash")
    $component.SetAttribute('Guid', [Guid]::ParseExact($hash, 'N').ToString('D'))
    $entry = $document.CreateElement('File', $namespace)
    $entry.SetAttribute('Id', $(if ($relative -eq 'AccessibilityInsights.exe') { 'FileExe' } else { "F$hash" }))
    $entry.SetAttribute('Source', $file.FullName)
    $entry.SetAttribute('KeyPath', 'yes')
    $null = $component.AppendChild($entry)
    $null = $parent.AppendChild($component)
    $reference = $document.CreateElement('ComponentRef', $namespace)
    $reference.SetAttribute('Id', "C$hash")
    $null = $group.AppendChild($reference)
}
$output = [IO.Path]::GetFullPath($OutputFile)
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$writer = [Xml.XmlWriter]::Create($output, $settings)
try { $document.Save($writer) } finally { $writer.Dispose() }
Write-Host "Inventoried $($files.Count) published files: $output"
