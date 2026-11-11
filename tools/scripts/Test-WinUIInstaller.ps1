# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$LayoutDirectory
)
$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$layout = (Resolve-Path -LiteralPath $LayoutDirectory).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
function Read-MsiRows([string]$Query, [int]$Columns) {
    try { $view = $database.OpenView($Query) }
    catch { throw "Cannot query MSI table ($Query): $($_.Exception.Message)" }
    try {
        $null = $view.Execute()
        while ($record = $view.Fetch()) {
            try {
                $row = @(for ($column = 1; $column -le $Columns; $column++) {
                    $record.StringData($column)
                })
                ,$row
            } finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    } finally {
        $null = $view.Close()
        $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}
try {
    $database = $installer.OpenDatabase($msi, 0)
    $upgrade = @(Read-MsiRows "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'UpgradeCode'" 1)
    if ($upgrade.Count -ne 1 -or $upgrade[0][0] -ne '{0D760959-F713-46C4-9A3D-4E73619EE3B5}') {
        throw "The original MSI upgrade identity was not preserved: $($upgrade | ConvertTo-Json -Depth 4 -Compress)"
    }
    $directories = @{}
    foreach ($row in (Read-MsiRows 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`' 3)) {
        $directories[$row[0]] = @{ parent = $row[1]; name = ($row[2].Split(':')[0].Split('|')[-1]) }
    }
    function Get-RelativeDirectory([string]$Id) {
        $parts = [Collections.Generic.List[string]]::new()
        $visited = [Collections.Generic.HashSet[string]]::new()
        while ($Id -ne 'INSTALLFOLDER') {
            if (!$visited.Add($Id) -or !$directories.ContainsKey($Id)) {
                throw "Invalid installed directory reference: $Id"
            }
            $directory = $directories[$Id]
            if ($directory.name -ne '.') { $parts.Insert(0, $directory.name) }
            $Id = $directory.parent
        }
        $parts -join [IO.Path]::DirectorySeparatorChar
    }
    $components = @{}
    foreach ($row in (Read-MsiRows 'SELECT `Component`, `Directory_` FROM `Component`' 2)) {
        $components[$row[0]] = $row[1]
    }
    $actual = @{}
    foreach ($row in (Read-MsiRows 'SELECT `File`, `Component_`, `FileName`, `FileSize` FROM `File`' 4)) {
        $folder = Get-RelativeDirectory $components[$row[1]]
        $name = $row[2].Split('|')[-1]
        $relative = if ($folder) { Join-Path $folder $name } else { $name }
        $file = Get-Item -LiteralPath (Join-Path $layout $relative)
        if ($file.Length -ne [long]$row[3] -or $actual.ContainsKey($relative)) {
            throw "MSI payload size or uniqueness mismatch: $relative"
        }
        $actual[$relative] = $row[0]
    }
    $published = @(Get-ChildItem -LiteralPath $layout -Recurse -File)
    if ($actual.Count -ne $published.Count) { throw 'MSI and publish payload file counts differ.' }
    foreach ($file in $published) {
        $relative = [IO.Path]::GetRelativePath($layout, $file.FullName)
        if (!$actual.ContainsKey($relative)) { throw "Published file is not installed: $relative" }
    }
    if ($actual['AccessibilityInsights.exe'] -ne 'FileExe') { throw 'The executable integration identity changed.' }
    $registry = @(Read-MsiRows 'SELECT `Key`, `Name`, `Value` FROM `Registry`' 3)
    foreach ($association in @(
        @{ extension = '.a11ytest'; progId = 'A11y.Test' },
        @{ extension = '.a11yevent'; progId = 'A11y.Event' }
    )) {
        $extensionRows = @($registry | Where-Object {
            $_[0] -eq $association.extension -and $_[2] -eq $association.progId
        })
        $verbRows = @($registry | Where-Object {
            $_[0] -eq "$($association.progId)\shell\open\command" -and
            $_[2].Contains('[#FileExe]') -and $_[2].Contains('"%1"')
        })
        if ($extensionRows.Count -ne 1 -or $verbRows.Count -ne 1) {
            throw "Missing file association or quoted open verb: $($association.extension)"
        }
    }
    Write-Host "MSI database validation passed: $($actual.Count) files, original UpgradeCode, executable identity and file associations."
    Write-Host 'This read-only inspection does not replace Windows Installer ICE or install/upgrade/uninstall acceptance.'
} finally {
    if ($database) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
