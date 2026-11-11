# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
param(
    [int]$AppPid = 0,
    [string]$TestName,
    [string]$ArtifactDirectory = (Join-Path $PSScriptRoot "TestResults\UI-$([guid]::NewGuid().ToString('N'))")
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$fixture = $null
$launchedApp = $false
$uiStarted = $false

function Invoke-WinAppChecked {
    param([string[]]$Arguments)
    $global:LASTEXITCODE = $null
    $output = @(& winapp @Arguments)
    if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -ne 0) {
        throw "winapp $($Arguments -join ' ') failed: $($output -join "`n")"
    }
    $output
}

function Invoke-Ui {
    param([string[]]$Arguments)
    Invoke-WinAppChecked (@('ui') + $Arguments + @('-w', $script:hwnd))
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    if ($TestName -and $Name -notlike $TestName) { return }
    try {
        & $Script | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
        if ($script:hwnd) {
            $failure = 'failure-' + $results.Count
            Save-Screenshot "$failure.png"
            Invoke-Ui @('inspect', '--json') | Set-Content -LiteralPath (Join-Path $ArtifactDirectory "$failure.json")
        }
    }
}

function Save-Screenshot {
    param([string]$Name)
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw "Choose a new evidence path: $path" }
    Invoke-Ui @('screenshot', '--output', $path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Screenshot was not delivered: $path"
    }
    $screenshots.Add($path)
}

function Get-UiElements {
    param([string]$Root)
    $arguments = @('inspect')
    if ($Root) { $arguments += $Root }
    $tree = Invoke-Ui ($arguments + @('--depth', '8', '--json')) | ConvertFrom-Json
    @(Get-Elements @($tree.windows | ForEach-Object { $_.elements }))
}

function Assert-NoOperationError {
    $elements = @(Get-UiElements)
    if (-not $elements.Count) { throw 'The app returned no UI elements.' }
    if (@($elements | Where-Object automationId -eq 'OperationError').Count) {
        $message = Invoke-Ui @('get-value', 'Message', '--root', 'OperationError', '--json') | ConvertFrom-Json
        Save-Screenshot 'live-operation-error.png'
        throw "Live inspection displayed an operation error: $($message.text)"
    }
}

function Invoke-Fixture {
    param([string[]]$Arguments)
    Invoke-WinAppChecked (@('ui') + $Arguments + @('-a', "$($fixture.Id)"))
}

function Complete-FilePicker {
    param([string]$Path, [string]$Action)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $windows = @(Invoke-WinAppChecked @('ui', 'list-windows', '--json') | ConvertFrom-Json)
        $pickers = @($windows | Where-Object {
            $_.hwnd -and [string]$_.ownerHwnd -eq $script:hwnd -and $_.title -match 'Open|Save'
        })
        if ($pickers.Count -eq 1) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($pickers.Count -ne 1) { throw 'Expected one file picker owned by the tested window.' }
    $picker = [string]$pickers[0].hwnd
    Invoke-WinAppChecked @('ui', 'set-value', 'FileNameControlHost', $Path, '-w', $picker) | Out-Null
    Invoke-WinAppChecked @('ui', 'invoke', $Action, '-w', $picker) | Out-Null
}

function Get-Elements {
    param($Nodes)
    foreach ($node in $Nodes) {
        if ($null -ne $node) {
            $node
            Get-Elements $node.children
        }
    }
}

try {
    $ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
    New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
    $fixturePath = Join-Path $PSScriptRoot '..\AccessibilityInsights.WinUITestTarget\bin\x64\Debug\net10.0-windows\AccessibilityInsights.WinUITestTarget.exe'
    if (-not (Test-Path -LiteralPath $fixturePath)) {
        throw 'Build AccessibilityInsights.WinUITestTarget before running UI tests.'
    }
    $fixture = Start-Process -FilePath $fixturePath -PassThru
    if (-not $fixture.WaitForInputIdle(10000)) { throw 'The test target did not become ready.' }

    if ($AppPid -eq 0) {
        $project = Join-Path $PSScriptRoot '..\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj'
        $config = Join-Path $ArtifactDirectory 'config'
        $data = Join-Path $ArtifactDirectory 'data'
        New-Item -ItemType Directory -Force -Path $config, $data | Out-Null
        $launch = Invoke-WinAppChecked @('run', $project, '--no-restore', '--arch', 'x64', '-p', 'Platform=x64',
            '--detach', '--devtools', 'off', '--json', '--',
            '--ConfigFolder', $config, '--UserDataFolder', $data) | ConvertFrom-Json
        if (-not $launch.ProcessId -or $launch.Error) { throw "App launch failed: $($launch.Error)" }
        $AppPid = [int]$launch.ProcessId
        $launchedApp = $true
    }
    if ($AppPid -le 0) { throw 'AppPid must identify the local WinUI process.' }
    $uiStarted = $true
    $app = Get-Process -Id $AppPid
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $app.Refresh()
        if ($app.HasExited) { throw 'The WinUI app exited before creating its window.' }
        if ($app.MainWindowTitle -eq 'Accessibility Insights for Windows') { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    $windows = @(Invoke-WinAppChecked @('ui', 'list-windows', '-a', "$AppPid", '--json') | ConvertFrom-Json)
    $main = $windows | Where-Object { $_.title -eq 'Accessibility Insights for Windows' } |
        Select-Object -First 1
    if (-not $main) { throw 'No WinUI window found.' }
    $script:hwnd = [string]$main.hwnd

    Test-UI 'Startup resource strings render without a fatal dialog' {
        Start-Sleep -Seconds 3
        Invoke-Ui @('wait-for', 'Unable to start Accessibility Insights for Windows', '--gone', '-t', '3000')
        $elements = @(Get-UiElements)
        if (@($elements | Where-Object automationId -eq 'StartupUpdateDialog').Count) {
            Invoke-Ui @('wait-for', 'StartupUpdateReleaseNotes', '--value', 'View the release notes here.', '-t', '3000')
            Invoke-Ui @('wait-for', 'Update now', '--type', 'Button', '-t', '3000')
        } else {
            Invoke-Ui @('invoke', 'NavContrast')
            Invoke-Ui @('wait-for', 'ContrastRatio', '--value', '21:1', '--contains', '-t', '3000')
            Invoke-Ui @('invoke', 'NavInspect')
        }
        Save-Screenshot '00-startup.png'
    }
    Test-UI 'Inspection starts without a target and cannot run' {
        Invoke-Ui @('invoke', 'NavInspect')
        Invoke-Ui @('invoke', 'RefreshWindows')
        Invoke-Ui @('wait-for', 'InspectWindow', '-p', 'IsEnabled', '--value', 'False', '-t', '3000')
        Save-Screenshot '01-inspect.png'
    }
    Test-UI 'Capture actual target hierarchy and properties' {
        Invoke-Ui @('invoke', 'TargetWindow')
        Invoke-Ui @('invoke', "WinUI migration test target ($($fixture.Id))", '--type', 'ListItem')
        Invoke-Ui @('wait-for', 'InspectWindow', '-p', 'IsEnabled', '--value', 'True', '-t', '3000')
        Invoke-Ui @('invoke', 'InspectWindow')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Inspection complete.', '--contains', '-t', '15000')
        Invoke-Ui @('wait-for', 'window: WinUI migration test target', '--type', 'TreeItem', '-t', '3000')
        Invoke-Ui @('wait-for', 'BoundingRectangle', '--root', 'ElementProperties', '--type', 'Text', '-t', '3000')
        Save-Screenshot '02-captured.png'
    }
    Test-UI 'Live inspection tracks mouse and focus without errors' {
        Invoke-Ui @('invoke', 'NavInspect')
        Invoke-Ui @('invoke', 'RefreshWindows')
        Invoke-Ui @('invoke', 'TargetWindow')
        Invoke-Ui @('invoke', "WinUI migration test target ($($fixture.Id))", '--type', 'ListItem')
        Invoke-Ui @('invoke', 'InspectWindow')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Inspection complete.', '--contains', '-t', '15000')
        Invoke-Ui @('invoke', 'FocusSelection', '--action', 'toggle-on')
        Invoke-Ui @('invoke', 'MouseSelection', '--action', 'toggle-off')
        Invoke-Ui @('invoke', 'ToggleLiveTracking')
        try {
            foreach ($target in @('TargetSave', 'TargetName', 'TargetSave')) {
                Invoke-Fixture @('focus', $target)
                $name = if ($target -eq 'TargetName') { 'edit: Name' } else { 'button: Save' }
                Invoke-Ui @('wait-for', $name, '--type', 'TreeItem', '-t', '5000')
                Assert-NoOperationError
            }
            Invoke-Ui @('invoke', 'MouseSelection', '--action', 'toggle-on')
            foreach ($target in @('TargetSave', 'UnnamedButton', 'Minimize', 'TargetName', 'TargetSave')) {
                Invoke-Fixture @('hover', $target, '--dwell-time', '1000')
                if ($target -eq 'UnnamedButton') {
                    Invoke-Ui @('wait-for', 'UnnamedButton', '--root', 'ElementProperties', '--type', 'Text', '-t', '5000')
                } else {
                    $name = switch ($target) {
                        'TargetName' { 'edit: Name' }
                        'Minimize' { 'button: Minimize' }
                        default { 'button: Save' }
                    }
                    Invoke-Ui @('wait-for', $name, '--type', 'TreeItem', '-t', '5000')
                }
                Assert-NoOperationError
            }
            Invoke-Ui @('invoke', 'ToggleTrackingPause')
            Invoke-Ui @('wait-for', 'StatusText', '--value', 'Live inspection paused.', '-t', '3000')
            Save-Screenshot '08-live-paused.png'
            Invoke-Ui @('invoke', 'MouseSelection', '--action', 'toggle-off')
            Invoke-Ui @('invoke', 'ToggleTrackingPause')
            Invoke-Fixture @('focus', 'TargetName')
            Invoke-Ui @('wait-for', 'edit: Name', '--type', 'TreeItem', '-t', '5000')
            Invoke-Ui @('invoke', 'MouseSelection', '--action', 'toggle-on')
            Invoke-Fixture @('hover', 'TargetName', '--dwell-time', '1000')
            Invoke-Fixture @('hover', 'TargetSave', '--dwell-time', '1000')
            Invoke-Ui @('wait-for', 'button: Save', '--type', 'TreeItem', '-t', '5000')
            Assert-NoOperationError
            Save-Screenshot '09-live-resumed.png'
        } finally {
            Invoke-Ui @('invoke', 'ToggleLiveTracking')
        }
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Live inspection stopped.', '-t', '3000')
        Assert-NoOperationError
    }
    Test-UI 'Real axe-windows failures appear in Test' {
        Invoke-Ui @('invoke', 'RunTests')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'failed checks', '--contains', '-t', '30000')
        Invoke-Ui @('invoke', 'NavTest')
        $tree = Invoke-Ui @('inspect', 'RuleFailures', '--depth', '5', '--json') | ConvertFrom-Json
        $elements = @(Get-Elements @($tree.windows | ForEach-Object { $_.elements }))
        if (-not @($elements | Where-Object { $_.type -eq 'ListItem' }).Count) {
            throw 'The intentionally unnamed fixture button produced no visible failure.'
        }
        Save-Screenshot '03-failures.png'
    }
        Test-UI 'Snapshots save and reopen through owned file pickers' {
            Invoke-Ui @('invoke', 'NavInspect')
            $script:snapshotPath = Join-Path $ArtifactDirectory 'recording.a11ytest'
            Invoke-Ui @('invoke', 'MoreButton')
            Invoke-Ui @('invoke', 'SaveSnapshot')
            Complete-FilePicker $script:snapshotPath 'Save'
            Invoke-Ui @('wait-for', 'StatusText', '--value', 'saved', '--contains', '-t', '10000')
            if (-not (Test-Path -LiteralPath $script:snapshotPath)) { throw 'Snapshot was not written.' }
            Invoke-Ui @('invoke', 'MoreButton')
            Invoke-Ui @('invoke', 'OpenSnapshot')
            Complete-FilePicker $script:snapshotPath 'Open'
            Invoke-Ui @('wait-for', 'StatusText', '--value', 'opened', '--contains', '-t', '10000')
            Invoke-Ui @('wait-for', 'window: WinUI migration test target', '--type', 'TreeItem', '-t', '3000')
        }
        Test-UI 'Live target supports screenshot capture and PNG save' {
            Invoke-Ui @('invoke', 'InspectWindow')
            Invoke-Ui @('wait-for', 'StatusText', '--value', 'Inspection complete.', '--contains', '-t', '15000')
            Invoke-Ui @('invoke', 'MoreButton')
            Invoke-Ui @('invoke', 'CaptureScreenshot')
            Invoke-Ui @('invoke', 'Screenshot', '--type', 'TabItem')
            Invoke-Ui @('wait-for', 'SaveScreenshot', '-p', 'IsEnabled', '--value', 'True', '-t', '10000')
            Invoke-Ui @('invoke', 'SaveScreenshot')
            $path = Join-Path $ArtifactDirectory 'target.png'
            Complete-FilePicker $path 'Save'
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            while (!(Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 100
            }
            Add-Type -AssemblyName System.Drawing
            $image = [Drawing.Image]::FromFile($path)
            try {
                if ($image.Width -le 0 -or $image.Height -le 0 -or $image.HorizontalResolution -le 0) {
                    throw 'The screenshot has invalid dimensions or DPI.'
                }
            } finally { $image.Dispose() }
            $screenshots.Add($path)
        }
        Test-UI 'Tab stops record real target focus transitions and stop cleanly' {
            Invoke-Ui @('invoke', 'NavTabStops')
            Invoke-Ui @('wait-for', 'StartTabStopRecording', '-p', 'IsEnabled', '--value', 'True', '-t', '5000')
            Invoke-Ui @('invoke', 'StartTabStopRecording')
            Invoke-Fixture @('focus', 'TargetName')
            Invoke-Fixture @('send-keys', 'tab', '--via', 'send-input')
            Invoke-Fixture @('send-keys', 'tab', '--via', 'send-input')
            Start-Sleep -Milliseconds 500
            Invoke-Ui @('invoke', 'StopTabStopRecording')
            $items = @(Get-UiElements 'RecordedTabStops' | Where-Object type -eq 'ListItem')
            if ($items.Count -lt 2) { throw 'Expected multiple recorded focus targets.' }
            Invoke-Fixture @('focus', 'TargetName')
            Start-Sleep -Milliseconds 300
            $after = @(Get-UiElements 'RecordedTabStops' | Where-Object type -eq 'ListItem')
            if ($after.Count -ne $items.Count) { throw 'Stopped recording still accepted focus changes.' }
            Save-Screenshot '05-tab-stops.png'
        }
        Test-UI 'Events record target focus changes and persist the legacy file format' {
            Invoke-Ui @('invoke', 'NavEvents')
            Invoke-Ui @('invoke', 'RefreshEventConfiguration')
            Invoke-Ui @('wait-for', 'EventFilter', '-t', '5000')
            Invoke-Ui @('invoke', 'ShowSelectedEventOptionsOnly', '--action', 'toggle-off')
            Invoke-Ui @('set-value', 'EventFilter', 'Focus')
            Invoke-Ui @('scroll-into-view', 'RecordingEvent_20005')
            Invoke-Ui @('invoke', 'RecordingEvent_20005', '--type', 'CheckBox', '--action', 'toggle-on')
            Invoke-Ui @('invoke', 'StartEventRecording')
            Invoke-Fixture @('focus', 'TargetName')
            Invoke-Fixture @('focus', 'TargetSave')
            Start-Sleep -Milliseconds 500
            Invoke-Ui @('invoke', 'StopEventRecording')
            $items = @(Get-UiElements 'RecordedEvents' | Where-Object type -eq 'ListItem')
            if (!$items.Count) { throw 'No real UIA focus events were recorded.' }
            $script:eventPath = Join-Path $ArtifactDirectory 'recording.a11yevent'
            Invoke-Ui @('invoke', 'SaveEventRecords')
            Complete-FilePicker $script:eventPath 'Save'
            Invoke-Ui @('wait-for', 'RecordingStatus', '--value', 'saved', '--contains', '-t', '10000')
            Invoke-Ui @('invoke', 'ClearEventRecords')
            Invoke-Ui @('invoke', 'OpenEventRecords')
            Complete-FilePicker $script:eventPath 'Open'
            Invoke-Ui @('wait-for', 'RecordingStatus', '--value', 'loaded', '--contains', '-t', '10000')
            if (!@(Get-UiElements 'RecordedEvents' | Where-Object type -eq 'ListItem').Count) {
                throw 'Reopened event records are empty.'
            }
            Save-Screenshot '06-events.png'
        }
    Test-UI 'Color contrast retains the original black/white result' {
        Invoke-Ui @('invoke', 'NavContrast')
        Invoke-Ui @('wait-for', 'ContrastRatio', '--value', '21:1', '--contains', '-t', '3000')
        Save-Screenshot '04-contrast.png'
    }
    Test-UI 'Screen picker samples real pixels and supports cancellation' {
        Invoke-Ui @('invoke', 'ScreenPickBackground')
        Invoke-Fixture @('focus', 'TargetName')
        Invoke-Fixture @('click', 'BlackColorSwatch')
        Invoke-Ui @('wait-for', 'ContrastRatio', '--value', '1:1', '--contains', '-t', '5000')
        Invoke-Ui @('invoke', 'ScreenPickBackground')
        Invoke-Fixture @('focus', 'TargetName')
        Invoke-Fixture @('click', 'WhiteColorSwatch')
        Invoke-Ui @('wait-for', 'ContrastRatio', '--value', '21:1', '--contains', '-t', '5000')
        Invoke-Ui @('invoke', 'ScreenPickForeground')
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
        Invoke-Ui @('wait-for', 'ScreenPickStatus', '--value', 'cancel', '--contains', '-t', '5000')
        Save-Screenshot '07-screen-picker.png'
    }
    Test-UI 'App controls have accessible names' {
        Invoke-Ui @('invoke', 'NavInspect')
        $tree = Invoke-Ui @('inspect', '--interactive', '--json') | ConvertFrom-Json
        $elements = @(Get-Elements @($tree.windows | ForEach-Object { $_.elements }))
        $controls = @($elements | Where-Object { $_.type -in @('Button', 'ComboBox', 'Edit') })
        if (-not $controls.Count) { throw 'No controls were inspected.' }
        $missing = @($controls | Where-Object { -not $_.name })
        if ($missing.Count) { throw "Controls lack accessible names: $($missing.automationId -join ', ')" }
    }
} catch {
    $results.Add(@{ name = 'Test setup'; status = 'FAIL'; detail = "$_" })
} finally {
    try {
        if ($fixture -and -not $fixture.HasExited) { Stop-Process -Id $fixture.Id }
        if ($launchedApp -and (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
            $process = Get-Process -Id $AppPid
            $null = $process.CloseMainWindow()
            if (!$process.WaitForExit(10000)) { Stop-Process -Id $AppPid }
        }
        if ($uiStarted) { Invoke-WinAppChecked @('ui', 'yield') | Out-Null }
    } finally {
        $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
    }
}

if (-not $results.Count) { throw "No UI tests matched: $TestName" }
$failed = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
$report = [ordered]@{
    target = 'local'; appPid = $AppPid
    passed = $results.Count - $failed; failed = $failed
    results = @($results.ToArray()); screenshots = @($screenshots.ToArray())
}
$resultPath = Join-Path $ArtifactDirectory 'test-results.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultPath -Encoding utf8
Write-Host "Passed: $($report.passed) | Failed: $failed | Results: $resultPath"
if ($failed) { exit 1 }
