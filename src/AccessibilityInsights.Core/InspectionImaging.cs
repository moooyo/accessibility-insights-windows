// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public readonly record struct ScreenRectangle(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public ScreenRectangle Intersect(ScreenRectangle other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(X + Width, other.X + other.Width);
        var bottom = Math.Min(Y + Height, other.Y + other.Height);
        return right > left && bottom > top
            ? new ScreenRectangle(left, top, right - left, bottom - top)
            : default;
    }
}

public sealed class InspectionScreenshot
{
    private readonly byte[] pngBytes;

    public InspectionScreenshot(
        byte[] pngBytes,
        int pixelWidth,
        int pixelHeight,
        double dpiX,
        double dpiY,
        ScreenRectangle screenBounds,
        ScreenRectangle selectionBoundsInImage,
        string title)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            throw new ArgumentException("PNG data cannot be empty.", nameof(pngBytes));
        }

        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Image dimensions must be positive.");
        }

        this.pngBytes = (byte[])pngBytes.Clone();
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiX = dpiX;
        DpiY = dpiY;
        ScreenBounds = screenBounds;
        SelectionBoundsInImage = selectionBoundsInImage;
        Title = title ?? string.Empty;
    }

    public ReadOnlyMemory<byte> PngBytes => pngBytes;
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double DpiX { get; }
    public double DpiY { get; }
    public ScreenRectangle ScreenBounds { get; }
    public ScreenRectangle SelectionBoundsInImage { get; }
    public string Title { get; }
}

public interface IInspectionImagingService
{
    Task<InspectionScreenshot?> CaptureScreenshotAsync(Guid contextId, int selectedElementId);
}
