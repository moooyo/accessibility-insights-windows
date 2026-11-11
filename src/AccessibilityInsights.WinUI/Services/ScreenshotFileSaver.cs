// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.Windows.Storage.Pickers;

namespace AccessibilityInsights_WinUI.Services;

internal interface IScreenshotFileSaver
{
    Task<bool> SaveAsync(InspectionScreenshot screenshot);
}

internal sealed class ScreenshotFileSaver(AppStrings strings) : IScreenshotFileSaver
{
    public async Task<bool> SaveAsync(InspectionScreenshot screenshot)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        var picker = new FileSavePicker(App.Window.AppWindow.Id)
        {
            SuggestedFileName = CreateFileName(screenshot.Title),
            DefaultFileExtension = ".png",
        };
        picker.FileTypeChoices.Add(strings.Get("ScreenshotFile"), [".png"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        await File.WriteAllBytesAsync(file.Path, screenshot.PngBytes);
        return true;
    }

    private static string CreateFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(title
            .Where(character => !invalid.Contains(character))
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(sanitized)
            ? "AccessibilityInsightsScreenshot"
            : $"{sanitized} accessibility";
    }
}
