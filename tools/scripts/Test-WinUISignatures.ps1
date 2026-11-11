# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$InstallerOnly
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $OutputDirectory).Path
if ($InstallerOnly) {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $root 'msi') -Filter 'AccessibilityInsights.msi' -File -Recurse)
} else {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $root 'layout') -File -Recurse |
        Where-Object Extension -In '.exe', '.dll')
    $files += Get-Item -LiteralPath (Join-Path $root 'customaction\AccessibilityInsights.CustomActions.CA.dll')
}
if (!$files.Count) { throw 'No artifacts were available for signature validation.' }
foreach ($file in $files) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid') {
        throw "The artifact does not have a valid trusted signature: $($file.FullName) ($($signature.Status))"
    }
}
Write-Host "Validated $($files.Count) production Authenticode signatures."
