// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.Windows.Storage.Pickers;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class SnapshotFilePicker(AppStrings strings) : ISnapshotFilePicker
{
    public async Task<string?> OpenAsync()
    {
        var picker = new FileOpenPicker(App.Window.AppWindow.Id);
        picker.FileTypeFilter.Add(".a11ytest");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> SaveAsync()
    {
        var picker = new FileSavePicker(App.Window.AppWindow.Id)
        {
            SuggestedFileName = "AccessibilityInsights",
            DefaultFileExtension = ".a11ytest",
        };
        picker.FileTypeChoices.Add(strings.Get("SnapshotFile"), [".a11ytest"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
