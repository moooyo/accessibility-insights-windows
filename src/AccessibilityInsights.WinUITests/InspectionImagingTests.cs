// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Buffers.Binary;
using System.Text;

namespace AccessibilityInsights.WinUITests;

[TestClass]
[DoNotParallelize]
public sealed class InspectionImagingTests
{
    [TestMethod]
    public async Task LoadedSnapshotScreenshotIsReturnedAsOwnedPngWithPixelMetadata()
    {
        InspectionScreenshot? screenshot;
        await using (var service = new InspectionService())
        {
            var snapshot = await service.OpenAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "WildlifeManagerTest.a11ytest"));
            screenshot = await ((IInspectionImagingService)service)
                .CaptureScreenshotAsync(snapshot.ContextId, snapshot.Root.Id);
        }

        Assert.IsNotNull(screenshot);
        var png = screenshot.PngBytes.Span;
        CollectionAssert.AreEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            png[..8].ToArray());
        Assert.AreEqual("IHDR", Encoding.ASCII.GetString(png.Slice(12, 4)));
        Assert.AreEqual(screenshot.PixelWidth, BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4)));
        Assert.AreEqual(screenshot.PixelHeight, BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4)));
        Assert.IsTrue(screenshot.DpiX > 0);
        Assert.IsTrue(screenshot.DpiY > 0);
        Assert.AreEqual(screenshot.PixelWidth, screenshot.ScreenBounds.Width);
        Assert.AreEqual(screenshot.PixelHeight, screenshot.ScreenBounds.Height);
    }

    [TestMethod]
    public async Task LoadedSnapshotCannotCreateNativeHighlight()
    {
        await using var service = new InspectionService();
        var snapshot = await service.OpenAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "WildlifeManagerTest.a11ytest"));

        var highlighted = await ((ILiveInspectionService)service)
            .HighlightAsync(snapshot.ContextId, snapshot.Root.Id);

        Assert.IsFalse(highlighted);
    }

    [TestMethod]
    public async Task LoadedElementDetailsUseStoredDataWithoutRequiringLiveAutomation()
    {
        await using var service = new InspectionService();
        var snapshot = await service.OpenAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "WildlifeManagerTest.a11ytest"));
        var element = snapshot.Root.Children.First();

        var details = await ((ILiveInspectionService)service)
            .GetElementDetailsAsync(snapshot.ContextId, element.Id);

        Assert.IsNotNull(details);
        Assert.IsFalse(details.IsLive);
        Assert.AreEqual(element.Id, details.ElementId);
        Assert.IsTrue(details.Properties.Count >= element.Properties.Count);
    }
}
