<!-- Copyright (c) Microsoft Corporation. All rights reserved.
     Licensed under the MIT License. -->

# WinUI 3 Migration Progress Report

Report date: November 11, 2026.

## Conclusion

**The migration is incomplete.** The current result is a separate, packaged
WinUI 3 preview with working core inspection workflows, not a feature-complete
replacement for the production WPF application.

The original WPF solution and installer remain unchanged. Do not replace the
production installer with this preview.

## Recorded execution time

All timestamps below use UTC+08:00. Durations are rounded to the nearest millisecond.

| Execution interval | Start | End | Duration |
|---|---|---|---|
| Initial migration attempt | 2026-11-11 15:32:20.7623376 | 2026-11-11 15:50:43.4205252 | 00:18:22.658 |
| Resumed migration attempt | 2026-11-11 16:59:05.6584314 | 2026-11-11 17:58:59.3862905 | 00:59:53.728 |
| Total active execution, excluding the pause | | | **01:18:16.386** |
| Wall-clock elapsed time, including the pause | | | 02:26:38.624 |

These are execution times up to the end of the recorded partial migration
attempts. **There is no full-migration completion time**, because full migration
has not been achieved. Subsequent discussion, this report, and commit/push work
are not included in the migration execution measurement.

## Completed work

| Area | Implemented result |
|---|---|
| Application target | Separate `src\AccessibilityInsights.WinUI.slnx` and packaged WinUI 3 application, targeting .NET 10 and using the existing Axe.Windows 2.4.2 engine |
| Selection and inspection | Real cross-process keyboard-focus selection, element properties, hierarchy, and selection-state reset |
| Automated checks | Repeatable live scans and failure-first rule results |
| Snapshots | Legacy `.a11ytest` import/export, preserving snapshot mode and embedded screenshots |
| Screenshots and highlighting | Live screenshot capture/display and native nonactivating, click-through highlighting without WPF rendering |
| Color contrast | Manual contrast input and automatic color detection |
| Tab stops | Numbered focus-order recording and highlighting of recorded elements |
| Events | Live UIA event recording and legacy `.a11yevent` import/export, including real cross-process property-change payload validation |
| Global shortcuts | Configurable shortcuts, registration-conflict reporting/rollback, cleanup on close, and restoration after restart |
| Settings | Atomic JSON persistence, system/light/dark themes, selection scope, and always-on-top |
| Legacy configuration | Explicit import of selection mode, scope, always-on-top, and three shortcuts; review/save is required and the source file is not modified |
| GitHub issues | Draft generation using original templates and rule metadata, with confirmation before opening the browser |
| Resources | WinUI `.resw` UI strings and injectable resources for view-model tests |
| Tests | Focused regression tests and an interactive desktop harness with its own fixture and cleanup |
| Packaging | Unsigned, self-contained x64/ARM64 MSIX bundle |

The original WPF production application was not replaced. Existing untracked
`.agent` skill files are not part of this migration source submission.

## Validation results

The migration validation recorded before this report produced:

| Check | Result | Scope or limitation |
|---|---|---|
| x64 Release build | Passed; 0 warnings, 0 errors | WinUI application |
| Regression tests | 42 passed, 0 failed | Models, view models, services, original file fixtures, configuration, and GitHub drafts |
| Application formatting | Passed | Workspace loading emitted a warning; verification exited successfully |
| Test-project formatting | Passed | No formatting diagnostics |
| Focus-only interactive acceptance | 15 passed, 1 explicitly skipped | Mouse-selection check was skipped; this is not full acceptance |
| Full interactive acceptance | Failed | Intermittent native mouse-selection crash |
| WPF dependency check | No matching WPF references found in the WinUI target | Checked `System.Windows.`, `PresentationCore`, `PresentationFramework`, and `UseWPF` |
| x64/ARM64 packaging | Passed | Architecture manifests, unsigned status, file length, and SHA256 verified; ARM64 runtime not tested |
| Git whitespace check | Passed | `git diff --check` |

The x64 Release build and all 42 regression tests were repeated successfully
before committing this report. An initial build with `--no-restore` failed because
the preceding ARM64 packaging had left assets without the x64 target. Rerunning
the documented build with restore enabled regenerated the x64 assets and passed
with 0 warnings and 0 errors; no source change was needed.

Build, regression, and formatting commands:

```powershell
dotnet build .\src\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj -p:Platform=x64 -c Release
dotnet test .\src\AccessibilityInsights.WinUITests\AccessibilityInsights.WinUITests.csproj --no-restore --logger 'console;verbosity=minimal'
dotnet format .\src\AccessibilityInsights.WinUI\AccessibilityInsights.WinUI.csproj --no-restore --verify-no-changes --severity warn
dotnet format .\src\AccessibilityInsights.WinUITests\AccessibilityInsights.WinUITests.csproj --no-restore --verify-no-changes --severity warn
git --no-pager diff --check
```

The interactive acceptance commands below reproduce the two validation modes;
artifact directories are chosen by the caller. Their results above were recorded
during the migration attempts, not rerun as part of writing this report.

```powershell
& .\src\AccessibilityInsights.WinUITests\UiTests.ps1 -FocusOnly -ArtifactDirectory "$env:TEMP\AccessibilityInsights-WinUI-focus-validation"
& .\src\AccessibilityInsights.WinUITests\UiTests.ps1 -ArtifactDirectory "$env:TEMP\AccessibilityInsights-WinUI-full-validation"
```

The harness covers packaged activation, real focus selection and scanning,
screenshots/highlighting, contrast, tab order, event payloads, system file pickers,
global shortcuts, GitHub draft cancellation, settings restoration, legacy file
compatibility, configuration import, and themes. It closes the processes it
launches and restores the preview settings.

A native-debugger run passed all 16 interactive checks on an earlier source
revision. Ordinary execution reproduced the mouse crash, so the debugger result
does **not** establish that the defect is fixed or that the final source passes
full acceptance.

## Package and local evidence

The verified package is retained in the session artifacts, not committed to Git:

| Property | Value |
|---|---|
| File | `AccessibilityInsights.WinUI.preview.verified.msixbundle` |
| Architectures | x64 and ARM64 |
| Length | 222,938,463 bytes |
| SHA256 | `1509DC407873CEFD19AFDAE8303611FA6922ED44E7A2C685A1A28B9CF20EFD62` |
| Signing | Unsigned; no production certificate installed |
| Runtime | .NET and Windows App SDK self-contained |
| Runtime acceptance | x64 only |

Additional local session artifacts include `winui-migration-results.json`,
`winui-migration-resume-results.json`, and the `focus-only-ui-tests` directory
containing result JSON, screenshots, and exported snapshot/event files.
These artifacts are not included in the source commit.

## Remaining work

| Area | Outstanding work |
|---|---|
| Mouse selection | Diagnose and fix the intermittent native crash, then pass ordinary full interactive acceptance |
| Azure DevOps | Authentication, issue forms, and attachments |
| Telemetry and updates | Original consent/instrumentation, update behavior, and release integration |
| Advanced inspection | Pattern and TextRange inspectors |
| Event recording | Extended recorder configuration |
| Tab stops | Screenshot annotations and persistence |
| Legacy settings | Import the settings beyond the explicitly supported subset |
| GitHub workflow | The preview only prepares a draft and opens the browser after confirmation; it does not automatically submit issues or upload attachments |
| UI parity | Finish and validate remaining original workflows; no complete feature-equivalence claim has been established |
| Platform acceptance | Native ARM64 execution and elevated-target testing |
| Production delivery | Approved package identity/signing and installation/update transition |

## Why full migration was not completed

1. Unlocking the desktop enabled real testing, which exposed an intermittent
   native crash during mouse selection. The observed module was
   `CoreMessagingXP.dll`, with exception code `0xc000027b`. The root cause is
   unconfirmed; the evidence does not establish whether WinUI, engine interaction,
   or another component is responsible. Keyboard-focus selection is the default,
   and mouse mode is explicitly marked experimental.
2. Implementation prioritized the core inspection workflows and crash
   investigation, then stopped with a preview and documented gaps. The remaining
   modules were not all ported. This is incomplete execution of the original
   request, not evidence that the mouse crash blocks every other module.
3. Full parity, ARM64 runtime testing, and production deployment validation were
   not completed. Signing and an installation/update transition require an
   approved release process; they are separate from implementing the remaining
   application features.

The appropriate conclusion is **partial migration with a known stability blocker
and outstanding feature work**. Passing builds, 42 regression tests, and the
focus-only acceptance subset do not make this a completed or production-ready
migration.
