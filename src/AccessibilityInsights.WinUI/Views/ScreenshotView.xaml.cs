// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class ScreenshotView : UserControl
{
    private InspectionScreenshot? screenshot;

    public ScreenshotView() => InitializeComponent();

    public async Task SetScreenshotAsync(InspectionScreenshot? value)
    {
        screenshot = value;
        if (value is null)
        {
            ScreenshotImage.Source = null;
            Preview.Visibility = Visibility.Collapsed;
            EmptyMessage.Visibility = Visibility.Visible;
            SelectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(value.PngBytes.ToArray());
            await writer.StoreAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        ScreenshotImage.Source = image;
        EmptyMessage.Visibility = Visibility.Collapsed;
        Preview.Visibility = Visibility.Visible;
        UpdateSelectionOutline();
        DispatcherQueue.TryEnqueue(UpdateSelectionOutline);
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs args) => UpdateSelectionOutline();

    private void UpdateSelectionOutline()
    {
        if (screenshot is null || screenshot.SelectionBoundsInImage.IsEmpty ||
            Preview.ActualWidth <= 0 || Preview.ActualHeight <= 0)
        {
            SelectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        var scale = Math.Min(
            Preview.ActualWidth / screenshot.PixelWidth,
            Preview.ActualHeight / screenshot.PixelHeight);
        var renderedWidth = screenshot.PixelWidth * scale;
        var renderedHeight = screenshot.PixelHeight * scale;
        var horizontalOffset = (Preview.ActualWidth - renderedWidth) / 2;
        var verticalOffset = (Preview.ActualHeight - renderedHeight) / 2;
        var bounds = screenshot.SelectionBoundsInImage;

        Canvas.SetLeft(SelectionOutline, horizontalOffset + (bounds.X * scale));
        Canvas.SetTop(SelectionOutline, verticalOffset + (bounds.Y * scale));
        SelectionOutline.Width = Math.Max(1, bounds.Width * scale);
        SelectionOutline.Height = Math.Max(1, bounds.Height * scale);
        SelectionOutline.Visibility = Visibility.Visible;
    }
}
