@echo off
setlocal
if /i "%~1"=="Enable" goto Confirm
if /i "%~1"=="Disable" goto Confirm
echo Usage: UIAccess.cmd ^<ENABLE^|DISABLE^>
echo Close Accessibility Insights and run from an administrative command prompt.
exit /b 1

:Confirm
echo CAUTION: UIAccess allows interaction with elevated application windows.
echo Enable it only for a trusted, signed installation.
choice /m "Are you sure you want to %~1 UIAccess support?"
if errorlevel 2 exit /b 0
pushd "%~dp0" || exit /b 1
copy /b /y "AccessibilityInsights.UIAccess.%~1d.exe" "AccessibilityInsights.exe" >nul
if errorlevel 1 goto Failed
copy /b /y "UIAccess_%~1d.manifest" "AccessibilityInsights.exe.manifest" >nul
if errorlevel 1 goto Failed
echo UIAccess state updated. Start Accessibility Insights to apply the change.
popd
exit /b 0

:Failed
echo Update failed. Close Accessibility Insights and verify administrative permissions.
popd
exit /b 1
