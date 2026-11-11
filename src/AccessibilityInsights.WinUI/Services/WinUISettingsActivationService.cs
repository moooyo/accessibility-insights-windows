// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.Settings;
using Microsoft.UI.Xaml;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class WinUISettingsActivationService : ISettingsActivationService
{
    private readonly WorkbenchViewModel workbench;

    public WinUISettingsActivationService(WorkbenchViewModel workbench)
    {
        this.workbench = workbench;
    }

    public async ValueTask ApplyAsync(
        AppSettings settings,
        SettingsActivationReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        App.Window.IsAlwaysOnTop = settings.AlwaysOnTop;
        App.Window.Shell.RequestedTheme = ElementTheme.Default;
        App.Window.Shell.FontSize = settings.FontSize == FontSize.Small ? 11 : 14;

        LiveSelectionMethods methods =
            (settings.SelectionByMouse ? LiveSelectionMethods.Mouse : LiveSelectionMethods.None) |
            (settings.SelectionByFocus ? LiveSelectionMethods.KeyboardFocus : LiveSelectionMethods.None);
        if (methods == LiveSelectionMethods.None)
        {
            throw new InvalidDataException("At least one live selection method must be enabled.");
        }

        await workbench.ApplyLiveInspectionOptionsAsync(new LiveInspectionOptions(
            settings.IsUnderElementScope ? LiveSelectionScope.Element : LiveSelectionScope.Application,
            methods,
            settings.MouseSelectionDelayMilliSeconds));
    }
}
