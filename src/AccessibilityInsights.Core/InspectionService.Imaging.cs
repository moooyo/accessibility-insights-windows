// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Axe.Windows.Actions;
using Axe.Windows.Actions.Enums;
using Axe.Windows.Core.Bases;
using Axe.Windows.Desktop.Utility;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AccessibilityInsights.Core;

public sealed partial class InspectionService : IInspectionImagingService
{
    public Task<InspectionScreenshot?> CaptureScreenshotAsync(Guid contextId, int selectedElementId) => Enqueue(() =>
    {
        if (currentContext != contextId || !GetDataAction.ExistElementContext(contextId))
        {
            return null;
        }

        var data = GetDataAction.GetElementDataContext(contextId);
        var selectedElement = GetDataAction.GetA11yElementInDataContext(contextId, selectedElementId);
        if (selectedElement is null)
        {
            return null;
        }

        Bitmap source;
        A11yElement captureElement;
        var ownsSource = false;
        if (data.Mode != DataContextMode.Live && data.Screenshot is not null)
        {
            source = data.Screenshot;
            captureElement = GetDataAction.GetA11yElementInDataContext(contextId, data.ScreenshotElementId) ?? data.Element;
        }
        else
        {
            captureElement = data.Element.GetParentWindow() ?? data.Element;
            if (captureElement.ProcessId == Environment.ProcessId || captureElement.BoundingRectangle.IsEmpty)
            {
                throw new InvalidOperationException("The application cannot capture itself or an element without screen bounds.");
            }

            source = CaptureBitmap(captureElement);
            ownsSource = true;
        }

        try
        {
            return EncodeScreenshot(source, captureElement, selectedElement, data.Mode == DataContextMode.Load);
        }
        finally
        {
            if (ownsSource)
            {
                source.Dispose();
            }
        }
    });

    private static Bitmap CaptureBitmap(A11yElement element)
    {
        var bounds = element.BoundingRectangle;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                bounds.X,
                bounds.Y,
                0,
                0,
                bounds.Size,
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static InspectionScreenshot EncodeScreenshot(
        Bitmap source,
        A11yElement captureElement,
        A11yElement selectedElement,
        bool preserveSourceDpi)
    {
        var captureBounds = ToScreenRectangle(captureElement);
        captureBounds = new ScreenRectangle(captureBounds.X, captureBounds.Y, source.Width, source.Height);
        var selectedBounds = ToScreenRectangle(selectedElement).Intersect(captureBounds);
        var selectedInImage = selectedBounds.IsEmpty
            ? default
            : new ScreenRectangle(
                selectedBounds.X - captureBounds.X,
                selectedBounds.Y - captureBounds.Y,
                selectedBounds.Width,
                selectedBounds.Height);
        var (dpiX, dpiY) = preserveSourceDpi
            ? GetBitmapDpi(source)
            : GetScreenDpi(captureElement.NativeWindowHandle, captureBounds);

        using var ownedBitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        ownedBitmap.SetResolution((float)dpiX, (float)dpiY);
        using (var graphics = Graphics.FromImage(ownedBitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        var pngCodec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Png.Guid)
            ?? throw new InvalidOperationException("The PNG image codec is not available.");
        using var stream = new MemoryStream();
        ownedBitmap.Save(stream, pngCodec, null);
        return new InspectionScreenshot(
            stream.ToArray(),
            ownedBitmap.Width,
            ownedBitmap.Height,
            dpiX,
            dpiY,
            captureBounds,
            selectedInImage,
            captureElement.Name ?? string.Empty);
    }

    private static (double DpiX, double DpiY) GetBitmapDpi(Bitmap bitmap)
    {
        var dpiX = IsValidDpi(bitmap.HorizontalResolution) ? bitmap.HorizontalResolution : 96.0;
        var dpiY = IsValidDpi(bitmap.VerticalResolution) ? bitmap.VerticalResolution : 96.0;
        return (dpiX, dpiY);
    }

    private static (double DpiX, double DpiY) GetScreenDpi(nint window, ScreenRectangle bounds)
    {
        if (window != 0)
        {
            var dpi = GetDpiForWindow(window);
            if (dpi > 0)
            {
                return (dpi, dpi);
            }
        }

        var nativeBounds = new NativeRectangle
        {
            Left = bounds.X,
            Top = bounds.Y,
            Right = bounds.X + bounds.Width,
            Bottom = bounds.Y + bounds.Height,
        };
        var monitor = MonitorFromRect(ref nativeBounds, 2);
        if (monitor != 0)
        {
            try
            {
                if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0 &&
                    dpiX > 0 && dpiY > 0)
                {
                    return (dpiX, dpiY);
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        return (96.0, 96.0);
    }

    private static bool IsValidDpi(float dpi) => !float.IsNaN(dpi) && !float.IsInfinity(dpi) && dpi > 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRectangle rectangle, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
