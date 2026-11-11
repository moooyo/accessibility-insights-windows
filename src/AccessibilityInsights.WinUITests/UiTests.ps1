param(
    [switch]$Fixture,
    [switch]$FocusOnly,
    [int]$ExistingAppId = 0,
    [string]$ArtifactDirectory = (Join-Path ([IO.Path]::GetTempPath()) ("AccessibilityInsights.WinUITests-" + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($Fixture) {
    Add-Type -AssemblyName System.Windows.Forms
    $form = [System.Windows.Forms.Form]::new()
    $form.Text = 'Accessibility Insights migration fixture'
    $form.Width = 500
    $form.Height = 300
    $fixtureInput = [System.Windows.Forms.TextBox]::new()
    $fixtureInput.Name = 'FixtureInput'
    $fixtureInput.AccessibleName = 'Migration test input'
    $fixtureInput.Text = 'Accessible input'
    $fixtureInput.SetBounds(20, 30, 300, 30)
    $button = [System.Windows.Forms.Button]::new()
    $button.Name = 'FixtureButton'
    $button.Text = 'Migration test button'
    $button.SetBounds(20, 90, 200, 30)
    $button.Add_Click({ $fixtureInput.Text = 'Button was invoked' })
    $form.Controls.AddRange(@($fixtureInput, $button))
    $timer = [System.Windows.Forms.Timer]::new()
    $timer.Interval = 300000
    $timer.Add_Tick({ $form.Close() })
    $timer.Start()
    try { [System.Windows.Forms.Application]::Run($form) }
    finally { $timer.Dispose(); $form.Dispose() }
    exit
}

$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appId = $ExistingAppId
$fixtureProcess = $null
$settingsPath = $null
$settingsBackup = $null
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()

function Invoke-WinApp([string[]]$Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $($output -join "`n")" }
    $output
}

function Invoke-Ui([string[]]$Arguments, [int]$Target = $appId) {
    Invoke-WinApp (@('ui') + $Arguments + @('-a', "$Target"))
}

function Test-UI([string]$Name, [scriptblock]$Body) {
    & $Body | Out-Null
    $results.Add(@{ name = $Name; status = 'passed' })
    Write-Output "PASS: $Name"
}

function Save-UiScreenshot([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    Invoke-Ui @('screenshot', '--focus', '--output', $path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "The screenshot was not saved: $path"
    }
    $screenshots.Add($path)
}

function Invoke-SystemPicker([string]$Button, [string]$Path, [string]$Action) {
    $windows = @(Invoke-Ui @('list-windows', '--json') | ConvertFrom-Json)
    $main = @($windows | Where-Object { $_.title -eq 'Accessibility Insights for Windows' })
    if ($main.Count -ne 1) { throw 'Expected one main window before opening a file picker.' }
    Invoke-Ui @('invoke', $Button) | Out-Null
    $picker = @()
    for ($attempt = 0; $attempt -lt 30 -and $picker.Count -eq 0; $attempt++) {
        Start-Sleep -Milliseconds 200
        $allWindows = @(Invoke-WinApp @('ui', 'list-windows', '--json') | ConvertFrom-Json)
        $picker = @($allWindows | Where-Object { $_.hwnd -and [string]$_.ownerHwnd -eq [string]$main[0].hwnd })
    }
    if ($picker.Count -ne 1) { throw 'Expected one file picker owned by the app.' }
    $handle = [string]$picker[0].hwnd
    try {
        Invoke-WinApp @('ui', 'set-value', 'File name', $Path, '--type', 'Edit', '-w', $handle) | Out-Null
        $commit = if ($Action -eq 'Open') { '1' } else { $Action }
        Invoke-WinApp @('ui', 'invoke', $commit, '--type', 'Button', '-w', $handle) | Out-Null
    }
    catch {
        & winapp ui inspect -w $handle --depth 5
        & winapp ui invoke Cancel --type Button -w $handle | Out-Null
        throw
    }
}

try {
    New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
    $project = Join-Path $PSScriptRoot '..\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj'
    if ($appId -eq 0) {
        $launch = Invoke-WinApp @('run', $project, '-c', 'Release', '--arch', 'x64',
            '--no-build', '--detach', '--json', '--devtools', 'off') | ConvertFrom-Json
        $appId = [int]$launch.ProcessId
    }
    if ($appId -le 0) { throw 'Packaged activation did not return an app PID.' }
    Test-UI 'Packaged main window' { Invoke-Ui @('wait-for', 'NavInspect', '-t', '10000') }
    $manifestPath = Join-Path $PSScriptRoot '..\AccessibilityInsights.WinUI\Package.appxmanifest'
    $identity = ([xml](Get-Content -LiteralPath $manifestPath -Raw)).Package.Identity.Name
    $package = Get-AppxPackage -Name $identity
    $settingsPath = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\settings.json"
    if (Test-Path -LiteralPath $settingsPath) { $settingsBackup = [IO.File]::ReadAllBytes($settingsPath) }
    Test-UI 'Settings save validates and persists global shortcut configuration' {
        Invoke-Ui @('invoke', 'NavSettings')
        Invoke-Ui @('set-value', 'PauseHotkey', 'Shift + F5')
        Invoke-Ui @('set-value', 'RecordHotkey', 'Shift + F7')
        Invoke-Ui @('set-value', 'ScanHotkey', 'Shift + F8')
        Invoke-Ui @('set-value', 'GitHubRepository', '')
        Invoke-Ui @('invoke', 'SaveSettings')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Settings saved.', '-t', '5000')
        $stored = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($stored.ScanHotkey -ne 'Shift + F8') { throw 'The scan shortcut was not persisted.' }
    }
    Test-UI 'Live contrast validation and recovery' {
        Invoke-Ui @('invoke', 'NavContrast')
        Invoke-Ui @('set-value', 'ForegroundColor', '#bad')
        Invoke-Ui @('wait-for', 'ContrastValidation', '--value', 'Enter two opaque colors', '--contains', '-t', '3000')
        Invoke-Ui @('set-value', 'ForegroundColor', '#FFFFFF')
        Invoke-Ui @('wait-for', 'ContrastRatio', '--value', '1:1', '-t', '3000')
        Invoke-Ui @('wait-for', 'SmallTextResult', '--value', 'Fail', '-t', '3000')
        Invoke-Ui @('set-value', 'ForegroundColor', '#000000')
        Invoke-Ui @('wait-for', 'ContrastRatio', '--value', '21:1', '-t', '3000')
    }

    $shell = (Get-Process -Id $PID).Path
    $fixtureProcess = Start-Process -FilePath $shell -ArgumentList @(
        '-NoProfile', '-STA', '-File', "`"$PSCommandPath`"", '-Fixture'
    ) -PassThru
    Invoke-Ui @('wait-for', 'FixtureInput', '-t', '10000') $fixtureProcess.Id | Out-Null
    Test-UI 'Cross-process selection and automated checks' {
        Invoke-Ui @('invoke', 'NavSettings')
        Invoke-Ui @('invoke', 'FocusSelection', '--action', 'toggle-on')
        Invoke-Ui @('invoke', 'ApplicationScope', '--action', 'toggle-on')
        Invoke-Ui @('invoke', 'NavInspect')
        Invoke-Ui @('invoke', 'StartSelection')
        Invoke-Ui @('wait-for', 'StartSelection', '--value', 'Pause selection', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'SelectedElementName', '--value',
            'Accessibility Insights migration fixture', '-t', '15000')
        Invoke-Ui @('invoke', 'StartSelection')
        Invoke-Ui @('invoke', 'RunChecks')
        Invoke-Ui @('wait-for', 'CheckResults', '-t', '15000')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'failed checks.', '--contains', '-t', '15000')
        $status = Invoke-Ui @('get-value', 'StatusText')
        if ($status -match '^0 elements|\b0 checks;') { throw 'The scan returned no elements or rules.' }
        Invoke-Ui @('invoke', 'RunChecks')
        Invoke-Ui @('wait-for', 'CheckResults', '-t', '15000')
    }
    Save-UiScreenshot '01-scan.png'
    if ($FocusOnly) {
        $results.Add(@{ name = 'Mouse selection'; status = 'skipped'; detail = 'Explicit focus-only run; intermittent native crash remains unresolved.' })
        Write-Output 'SKIP: Mouse selection (explicit -FocusOnly; known intermittent native crash)'
    }
    else { Test-UI 'Mouse selection targets a control independently of keyboard focus' {
        Invoke-Ui @('invoke', 'NavSettings')
        Invoke-Ui @('invoke', 'FocusSelection', '--action', 'toggle-off')
        Invoke-Ui @('invoke', 'ApplicationScope', '--action', 'toggle-off')
        Invoke-Ui @('invoke', 'NavInspect')
        Invoke-Ui @('invoke', 'StartSelection')
        Invoke-Ui @('wait-for', 'StartSelection', '--value', 'Pause selection', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('hover', 'FixtureButton') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'SelectedElementName', '--value', 'Migration test button', '-t', '10000')
        Invoke-Ui @('invoke', 'StartSelection')
        Invoke-Ui @('invoke', 'NavSettings')
        Invoke-Ui @('invoke', 'FocusSelection', '--action', 'toggle-on')
        Invoke-Ui @('invoke', 'ApplicationScope', '--action', 'toggle-on')
        Invoke-Ui @('invoke', 'NavInspect')
        Invoke-Ui @('invoke', 'StartSelection')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'SelectedElementName', '--value',
            'Accessibility Insights migration fixture', '-t', '10000')
        Invoke-Ui @('invoke', 'StartSelection')
        Invoke-Ui @('invoke', 'RunChecks')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'failed checks.', '--contains', '-t', '15000')
    } }
    Test-UI 'Native highlighting and screenshot capture' {
        Invoke-Ui @('invoke', 'HighlightToggle', '--action', 'toggle-on')
        Invoke-Ui @('invoke', 'HighlightToggle', '--action', 'toggle-off')
        Invoke-Ui @('invoke', 'ScreenshotTab')
        Invoke-Ui @('invoke', 'CaptureScreenshot')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Screenshot captured.', '-t', '15000')
        Invoke-Ui @('wait-for', 'ScreenshotImage', '-t', '3000')
    }
    Test-UI 'Automatic color detection' {
        Invoke-Ui @('invoke', 'NavContrast')
        Invoke-Ui @('invoke', 'DetectColors')
        Invoke-Ui @('wait-for', 'ColorConfidence', '--value', 'Confidence:', '--contains', '-t', '15000')
    }
    Save-UiScreenshot '02-contrast.png'
    Test-UI 'Tab-stop recording preserves cross-process focus order' {
        Invoke-Ui @('invoke', 'NavTest')
        Invoke-Ui @('invoke', 'TabStopsTab')
        Invoke-Ui @('invoke', 'RecordTabStops')
        Invoke-Ui @('wait-for', 'RecordTabStops', '--value', 'Stop tab-stop recording', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'StatusText', '--value', '1 tab stops recorded.', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureButton') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'StatusText', '--value', '2 tab stops recorded.', '-t', '5000')
        Invoke-Ui @('invoke', 'RecordTabStops')
    }
    Save-UiScreenshot '03-tab-stops.png'
    Test-UI 'UI Automation records real cross-process property-change events' {
        Invoke-Ui @('invoke', 'NavEvents')
        Invoke-Ui @('invoke', 'RecordEvents')
        Invoke-Ui @('wait-for', 'RecordEvents', '--value', 'Stop recording', '-t', '5000')
        Invoke-Ui @('send-keys', 'space', '--target', 'FixtureButton', '--via', 'send-input') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'FixtureInput', '--value', 'Button was invoked', '-t', '5000') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'Button was invoked', '--root', 'EventResults', '--type', 'Text', '-t', '10000')
        Invoke-Ui @('invoke', 'RecordEvents')
    }
    Test-UI 'Event files save and open through the real system file pickers' {
        $path = Join-Path $ArtifactDirectory 'UiEvents.a11yevent'
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
        Invoke-SystemPicker 'SaveEvents' $path 'Save'
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Saved UiEvents.a11yevent', '-t', '10000')
        $events = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if (-not ($events | Where-Object { $_.EventId -eq 20004 })) { throw 'The event file is missing recorded property changes.' }
        Invoke-SystemPicker 'OpenEvents' $path 'Open'
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'events.', '--contains', '-t', '5000')
    }
    Test-UI 'Global shortcuts work while another process has keyboard focus' {
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('send-keys', 'shift+f5', '--via', 'send-input') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'StartSelection', '--value', 'Pause selection', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureButton') $fixtureProcess.Id
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'SelectedElementName', '--value',
            'Accessibility Insights migration fixture', '-t', '10000')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('send-keys', 'shift+f5', '--via', 'send-input') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'StartSelection', '--value', 'Start selection', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('send-keys', 'shift+f8', '--via', 'send-input') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'CheckResults', '-t', '15000')
        Invoke-Ui @('invoke', 'NavEvents')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('send-keys', 'shift+f7', '--via', 'send-input') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'RecordEvents', '--value', 'Stop recording', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('send-keys', 'shift+f7', '--via', 'send-input') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'RecordEvents', '--value', 'Start recording', '-t', '5000')
    }
    Test-UI 'GitHub issue drafts use the legacy template and can be cancelled without uploading data' {
        Invoke-Ui @('invoke', 'NavSettings')
        Invoke-Ui @('set-value', 'GitHubRepository', 'https://github.com/owner/repo')
        Invoke-Ui @('invoke', 'NavInspect')
        Invoke-Ui @('invoke', 'MoreButton')
        Invoke-Ui @('invoke', 'FileGitHubIssue')
        Invoke-Ui @('wait-for', 'IssueDraftBody', '--value', 'The following accessibility issue needs investigation.',
            '--contains', '-t', '5000')
        Invoke-Ui @('invoke', 'Cancel', '--type', 'Button')
        Invoke-Ui @('wait-for', 'IssueDraftBody', '--gone', '-t', '5000')
    }
    Test-UI 'Saved settings survive packaged app restart and shortcuts are released on close' {
        Invoke-Ui @('invoke', 'NavSettings')
        Invoke-Ui @('set-value', 'PauseHotkey', 'Control,Shift + F10')
        Invoke-Ui @('invoke', 'SaveSettings')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Settings saved.', '-t', '5000')
        $app = Get-Process -Id $appId
        Invoke-Ui @('invoke', 'Close')
        if (-not $app.WaitForExit(10000)) { throw 'The app did not shut down cleanly.' }
        $launch = Invoke-WinApp @('run', $project, '-c', 'Release', '--arch', 'x64',
            '--no-build', '--detach', '--json', '--devtools', 'off') | ConvertFrom-Json
        $script:appId = [int]$launch.ProcessId
        Invoke-Ui @('wait-for', 'NavSettings', '-t', '10000')
        Invoke-Ui @('invoke', 'NavSettings')
        Invoke-Ui @('wait-for', 'PauseHotkey', '--value', 'Control,Shift + F10', '-t', '5000')
        Invoke-Ui @('focus', 'FixtureInput') $fixtureProcess.Id
        Invoke-Ui @('send-keys', 'ctrl+shift+f10', '--via', 'send-input') $fixtureProcess.Id
        Invoke-Ui @('wait-for', 'StartSelection', '--value', 'Pause selection', '-t', '5000')
    }
    Test-UI 'Legacy snapshot files open through the real system file picker' {
        $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\UITests\TestFiles\WildlifeManagerTest.a11ytest'))
        Invoke-SystemPicker 'OpenSnapshot' $path 'Open'
        Invoke-Ui @('wait-for', 'SelectedElementName', '--value', 'Wildlife', '--contains', '-t', '10000')
        $saved = Join-Path $ArtifactDirectory 'UiSnapshot.a11ytest'
        if (Test-Path -LiteralPath $saved) { Remove-Item -LiteralPath $saved }
        Invoke-SystemPicker 'SaveSnapshot' $saved 'Save'
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'Saved UiSnapshot.a11ytest', '-t', '10000')
        if ((Get-Item -LiteralPath $saved).Length -eq 0) { throw 'The snapshot file is empty.' }
    }
    Test-UI 'Supported WPF settings import into the editor without silently overwriting current settings' {
        Invoke-Ui @('invoke', 'NavSettings')
        $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\AccessibilityInsights.SharedUxTests\Resources\ConfigSettings.json'))
        Invoke-SystemPicker 'ImportWpfSettings' $path 'Open'
        Invoke-Ui @('wait-for', 'PauseHotkey', '--value', 'Alt + F2', '-t', '5000')
        Invoke-Ui @('wait-for', 'StatusText', '--value', 'original file is unchanged.', '--contains', '-t', '5000')
        $stored = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($stored.PauseHotkey -ne 'Control,Shift + F10') { throw 'Import overwrote saved settings before review.' }
    }
    Test-UI 'Dark and system themes switch without restarting the app' {
        Invoke-Ui @('invoke', 'ThemePicker')
        Invoke-Ui @('invoke', 'Dark', '--type', 'ListItem')
        Invoke-Ui @('wait-for', 'ThemePicker', '--value', 'Dark', '-t', '5000')
        Save-UiScreenshot '04-dark-theme.png'
        Invoke-Ui @('invoke', 'ThemePicker')
        Invoke-Ui @('invoke', 'System default', '--type', 'ListItem')
        Invoke-Ui @('wait-for', 'ThemePicker', '--value', 'System default', '-t', '5000')
    }
}
catch {
    if ($appId -gt 0) {
        & winapp ui get-value SelectedElementName -a $appId
        & winapp ui get-value StatusText -a $appId
        & winapp ui inspect ErrorBar -a $appId --depth 3
        & winapp ui inspect TabStopResults -a $appId --depth 4
        & winapp ui invoke NavEvents -a $appId
        & winapp ui inspect EventResults -a $appId --depth 5
    }
    $results.Add(@{ name = 'UI test execution'; status = 'failed'; detail = "$_" })
    throw
}
finally {
    if ($ArtifactDirectory) {
        New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
        $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ArtifactDirectory 'ui-results.json')
        $screenshots | ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'ui-screenshots.json')
    }
    if ($fixtureProcess -and -not $fixtureProcess.HasExited) { Stop-Process -Id $fixtureProcess.Id }
    if ($appId -gt 0 -and (Get-Process -Id $appId -ErrorAction SilentlyContinue)) {
        & winapp ui invoke Close -a $appId | Out-Null
        $app = Get-Process -Id $appId -ErrorAction SilentlyContinue
        if ($app -and -not $app.WaitForExit(10000)) { Stop-Process -Id $appId }
    }
    if ($settingsPath) {
        if ($null -ne $settingsBackup) { [IO.File]::WriteAllBytes($settingsPath, $settingsBackup) }
        elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath }
    }
    & winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
}
