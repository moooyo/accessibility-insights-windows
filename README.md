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

#### 2. Open the solution in Visual Studio
- Use `src\AccessibilityInsights.WinUI.slnx` for the WinUI 3 application.
- The original `src\AccessibilityInsights.sln` is retained as a compatibility reference.

#### 3. Build and run unit tests
See the WinUI 3 commands below. The application requires the .NET 10 SDK and Windows
App SDK build prerequisites.

#### 4. Build the MSI project
The WinUI MSI uses WiX restored from NuGet. Publish the actual self-contained app and
updater before generating its installer inventory:

```powershell
.\tools\scripts\Publish-WinUI.ps1 -OutputDirectory .\artifacts\winui\local -NuGetSource https://api.nuget.org/v3/index.json
```

Use a new output directory for each publish. The unsigned installer is written to
`msi\<version>\AccessibilityInsights.msi` beneath that directory. The protected release
pipeline signs the published payload before building and signing the MSI. Local
publishing does not install an application or trust a signing certificate.

### More Information
Visit the [Overview of Accessibility Insights for Windows](./docs/Overview.md) page.

### WinUI 3 application

`src\AccessibilityInsights.WinUI.slnx` separates the WinUI desktop shell, UI-independent
inspection and view-model logic, compatibility tests, and the WinUI update helper.
The application uses CommunityToolkit.Mvvm, dependency injection, typed bindings,
and PRI-backed `.resw` resources without a WPF framework dependency.

The inspection workflow includes live mouse/focus selection, highlighting, complete
properties and patterns, tree navigation, screenshots, axe-windows checks, event and
tab-stop recording, and screen-pixel color contrast analysis. Existing `.a11ytest`,
`.a11yevent`, command-line options, and MSI upgrade identity are retained.

Use the .NET 10 SDK, WinApp CLI 0.8+, and Windows with development deployment enabled.
Run these commands from the repository root:

```powershell
dotnet restore .\src\AccessibilityInsights.WinUI.slnx --source https://api.nuget.org/v3/index.json
dotnet build .\src\AccessibilityInsights.WinUI.slnx -p:Platform=x64 --no-restore
winapp run .\src\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj --no-restore --arch x64 -p Platform=x64
dotnet build .\src\AccessibilityInsights.WinUITestTarget\AccessibilityInsights.WinUITestTarget.csproj -c Debug -p:Platform=x64 --no-restore
dotnet test .\src\AccessibilityInsights.WinUITests\AccessibilityInsights.WinUITests.csproj -p:Platform=x64 --no-restore
```

The public source override applies only to that restore; it does not change the
existing solution's Azure Artifacts configuration. The compatibility tests open and
round-trip an existing WPF snapshot. Interactive UI tests use a dedicated test-target
process containing an intentionally unnamed button and exercise real inspection and
rule failures, rather than mock results. The UIA integration tests also require this
fixture's explicit `x64\Debug` output, including when the tests run in Release:

```powershell
dotnet build .\src\AccessibilityInsights.WinUITestTarget\AccessibilityInsights.WinUITestTarget.csproj -p:Platform=x64 --no-restore
.\src\AccessibilityInsights.WinUITests\ui-tests.ps1
```

The UI script runs locally with isolated configuration and data folders and writes
structured results and screenshots under its `TestResults` directory. It closes only
the app and target processes it starts. Use a disposable test desktop when running
interactive validation. Never use real accounts or production issue submission in
automated UI tests.

MSI layouts include both .NET and Windows App SDK runtimes. The installer custom
action retains its existing .NET Framework 4.8 requirement, but neither application
uses the Windows Desktop framework. `UIAccess.cmd ENABLE` / `DISABLE` selects a
pre-signed apphost with the requested embedded privilege, preserving the self-contained
WinUI activation registrations. Close the app first and run the command as administrator.
Enabled UIAccess requires an appropriately signed executable in a trusted installation
location. Production signing, certificate trust, upgrade,
and uninstall acceptance must run on an authorized release test machine.
The self-contained updater runs from the trusted installation and stages its runtime
under `Program Files\AccessibilityInsights\UpdateStaging` after administrator approval,
not in a user-writable temporary directory. The current bootstrap accepts the installed
helper only beneath `Program Files\AccessibilityInsights`; custom installation locations
outside that trusted root are rejected and still need migration acceptance.

On a shared machine where Windows Installer ICE execution is prohibited, use
`-SkipMsiValidation` explicitly for an unsigned diagnostic build. This does not certify
installation readiness; the normal release build keeps ICE enabled. The read-only
`tools\scripts\Test-WinUIInstaller.ps1` verifies the MSI payload against its publish
layout without installing it.

#### Migration acceptance status

The WinUI implementation and unsigned MSI build are available, but migration acceptance
is not complete. The last full desktop UI batch passed 6 of 10 scenarios. Fixes for the
live-to-test context, command overflow selectors, and recording layout were subsequently
built and covered where possible by focused tests, but that desktop batch was not rerun
after reaching the UI workflow's retry limit. Production signing, Windows Installer ICE,
installation, upgrade, and uninstall still require an authorized disposable test machine.
Do not treat an unsigned diagnostic MSI or a successful build as release approval.

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
