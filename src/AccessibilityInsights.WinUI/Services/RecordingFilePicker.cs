// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.Windows.Storage.Pickers;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class RecordingFilePicker(
    AppStrings strings,
    DispatcherQueueService dispatcher) : IRecordingFilePicker
{
    public Task<string?> OpenEventFileAsync() => dispatcher.EnqueueAsync(async () =>
    {
        var picker = new FileOpenPicker(App.Window.AppWindow.Id);
        picker.FileTypeFilter.Add(".a11yevent");
        return (await picker.PickSingleFileAsync())?.Path;
    });

    public Task<string?> SaveEventFileAsync() => dispatcher.EnqueueAsync(async () =>
    {
        var picker = new FileSavePicker(App.Window.AppWindow.Id)
        {
            SuggestedFileName = "AccessibilityInsightsEvents",
            DefaultFileExtension = ".a11yevent",
        };
        picker.FileTypeChoices.Add(strings.Get("EventRecordFile"), [".a11yevent"]);
        return (await picker.PickSaveFileAsync())?.Path;
    });
}
