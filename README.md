<!-- Copyright (c) Microsoft Corporation. All rights reserved.
     Licensed under the MIT License. -->
## ![Product Logo](./brand/brand-blue-48px.png) Accessibility Insights for Windows

[![Build Status](https://dev.azure.com/accessibility-insights/Accessibility%20Insights/_apis/build/status/Microsoft.accessibility-insights-windows-CI?branchName=main)](https://dev.azure.com/accessibility-insights/Accessibility%20Insights/_build/latest?definitionId=3&branchName=main)
[![Downloads](https://img.shields.io/github/downloads/microsoft/accessibility-insights-windows/total.svg)](https://github.com/Microsoft/accessibility-insights-windows/releases/latest)
[![Release](https://img.shields.io/github/release/microsoft/accessibility-insights-windows.svg)](https://github.com/Microsoft/accessibility-insights-windows/releases/latest)

Accessibility Insights for Windows is the project for Accessibility tools on the Windows platform.

### Installing Accessibility Insights for Windows
You can download and install the application from https://accessibilityinsights.io.

### Building the code
You can find more information on how to set up your development environment [here](./docs/SetUpDevEnv.md).

#### 1. Clone the repository
- Clone the repository using one of the following commands
  ``` bash
  git clone https://github.com/Microsoft/accessibility-insights-windows.git
  ```
  or
  ``` bash
  git clone git@github.com:Microsoft/accessibility-insights-windows.git
  ```
- Select the created directory
  ``` bash
  cd accessibility-insights-windows
  ```

#### 2. Open the solution in visual studio
- Use the `src/AccessibilityInsights.sln` file to open the solution.

#### 3. Build and run unit tests

#### 4. Build the MSI project
The MSI project is used to package all the code into an installer that can be used to install Accessibility Insights for Windows. This is not a required step. By default the MSI project is not built in either configuration.
Note: The MSI can only be built in the `Release` config.
- The MSI project requires WIX. WiX is a modern SDK that restore will automatically download from NuGet. Learn about the Wix toolset from http://wixtoolset.org/.
- To build the MSI locally, set build configuration to `Release`.
- Find the MSI project in the solution explorer under the `packages` folder.
- Right click on the MSI project and select `Build`.
- Once the build is complete, the MSI can be found at `\src\MSI\bin\Release`.

### More Information
Visit the [Overview of Accessibility Insights for Windows](./docs/Overview.md) page.

### WinUI 3 migration preview

The separate `src\AccessibilityInsights.WinUI.slnx` solution contains a packaged
WinUI 3 migration target using .NET 10 and the existing Axe.Windows 2.4.2 engine.
The original WPF solution remains unchanged and is still the production application.

See [the migration progress report](./docs/WinUI3MigrationReport.md) for measured
execution time, completed work, validation results, and remaining blockers.

```powershell
dotnet build .\src\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj -p:Platform=x64 -c Release
dotnet test .\src\AccessibilityInsights.WinUITests\AccessibilityInsights.WinUITests.csproj
winapp run .\src\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj --arch x64 -c Release --no-build --detach --json
```

The preview includes real cross-process focus selection, element properties and
hierarchy, repeatable automated checks, legacy `.a11ytest` snapshot load/save,
screenshots, native nonactivating highlighting, numbered tab-stop recording,
`.a11yevent` recording/import/export, automatic and manual color contrast,
system/light/dark themes, persisted settings, and configurable global shortcuts.
The default shortcuts remain Shift+F5 (selection), Shift+F7 (recording), and
Shift+F8 (automated checks).
Keyboard-focus selection is the default. Mouse-only selection remains experimental:
an intermittent native `CoreMessagingXP.dll` crash has been reproduced and is unresolved.

Set a GitHub repository in Settings, then use the command bar overflow to review an
issue draft. The draft preserves the original issue templates and opens the browser
only after confirmation; it does not automatically submit an issue or upload files.
Selecting an automated-check result includes its rule source, help link, element
glimpse, and remediation details.

The explicit **Import WPF settings** action reads selection mode, element/application
scope, always-on-top, and the three supported shortcuts from an original configuration
JSON file. Review and save the imported values. Other legacy settings are not imported,
and the original configuration is never modified.

Regression tests cover the original WPF snapshot, event, and configuration fixtures.
Run the end-to-end harness on an unlocked interactive desktop:

```powershell
& .\src\AccessibilityInsights.WinUITests\UiTests.ps1 -ArtifactDirectory "$env:TEMP\AccessibilityInsights-WinUI-validation"
```

For the focus-only acceptance subset, add `-FocusOnly`; this explicitly records the
mouse-selection check as skipped and must not be interpreted as full migration acceptance.

The harness launches its own fixture, verifies real scanning, focus order, event
payloads, global shortcuts, system file pickers, issue-draft cancellation, and settings
restoration after restart. It closes the processes it launches and restores the
preview settings file. JSON results, screenshots, and exported test files remain in
the artifact directory.

Build an unsigned x64/ARM64 bundle without installing certificates:

```powershell
winapp package .\src\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj --arch x64 --arch arm64 --no-sign --self-contained
```

This is **not a complete migration**. Azure DevOps authentication and attachments,
the original telemetry/update integrations, pattern/text-range inspectors, extended
recorder configuration, tab-stop screenshot annotations/persistence, and remaining
legacy UI workflows still need migration and parity validation. ARM64 packaging is
validated, but runtime testing is currently x64 only. Production signing and an approved
installation/update transition are also required. Do not replace the WPF installer
with this preview. It deliberately keeps a separate package identity.

### Testing
We use the unit test framework from Visual Studio. Find more information in our [FAQ section](./docs/FAQ.md).

## Contributing
All contributions are welcome! Please read through our guidelines on [contributions](./Contributing.md) to this project.

## FAQ
Please visit our [FAQ section](./docs/FAQ.md) to get answers to common questions.

## Data/Telemetry
By opting into telemetry, you [help the community](https://go.microsoft.com/fwlink/?linkid=2077765) develop inclusive software. We collect anonymized data to identify the top accessibility issues found by users. This will help focus the accessibility tools and standards community to improve guidelines, rules engines, and features.

This project collects usage data and sends it to Microsoft to help improve our products and services. Read our [privacy statement](https://privacy.microsoft.com/en-us/privacystatement) to learn more.

## Reporting security vulnerabilities
If you believe you have found a security vulnerability in this project, please follow [these steps](https://technet.microsoft.com/en-us/security/ff852094.aspx) to report it. For more information on how vulnerabilities are disclosed, see [Coordinated Vulnerability Disclosure](https://technet.microsoft.com/en-us/security/dn467923).
