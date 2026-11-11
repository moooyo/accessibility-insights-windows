// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
[DoNotParallelize]
public sealed class InspectionServiceTests
{
    [TestMethod]
    public async Task ExistingWpfSnapshotCanBeOpenedSavedAndReopenedWithoutLosingResults()
    {
        var input = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WildlifeManagerTest.a11ytest");
        var output = Path.Combine(Path.GetTempPath(), $"WinUIMigration-{Guid.NewGuid():N}.a11ytest");
        try
        {
            await using var service = new InspectionService();
            var original = await service.OpenAsync(input);
            Assert.IsFalse(string.IsNullOrWhiteSpace(original.Root.Name));
            Assert.IsTrue(original.Root.Children.Count > 0);
            Assert.IsTrue(original.Failures.Any());
            await service.SaveAsync(output);
            var reopened = await service.OpenAsync(output);
            Assert.AreEqual(original.Root.Name, reopened.Root.Name);
            Assert.AreEqual(original.Root.Children.Count, reopened.Root.Children.Count);
            Assert.AreEqual(original.Failures.Count(), reopened.Failures.Count());
        }
        finally
        {
            File.Delete(output);
        }
    }

    [TestMethod]
    public async Task InvalidCaptureReportsErrorAndWorkerRemainsUsable()
    {
        await using var service = new InspectionService();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.CaptureAsync(0, false));
        var snapshot = await service.OpenAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "WildlifeManagerTest.a11ytest"));
        Assert.IsNotNull(snapshot.Root);
    }

    [DataTestMethod]
    [DataRow(0, 0, 0, 21.0)]
    [DataRow(255, 255, 255, 1.0)]
    [DataRow(255, 0, 0, 3.9984767707539985)]
    [DataRow(0, 0, 255, 8.592471358428805)]
    public void ContrastRetainsWpfCalculationAndSymmetry(int red, int green, int blue, double expected)
    {
        var ratio = ColorContrast.Calculate((byte)red, (byte)green, (byte)blue, 255, 255, 255);
        Assert.AreEqual(expected, ratio, 0.0000001);
        Assert.AreEqual(ratio, ColorContrast.Calculate(255, 255, 255, (byte)red, (byte)green, (byte)blue));
    }
}
