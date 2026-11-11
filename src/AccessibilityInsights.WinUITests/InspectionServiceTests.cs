// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class InspectionServiceTests
{
    [DataTestMethod]
    [DataRow("WildlifeManagerTest.a11ytest")]
    [DataRow("WildlifeManagerTestWithFrameworkIssues.a11ytest")]
    public async Task LegacyWpfSnapshotsKeepPropertiesAndRuleResultsAcrossSaveAndLoad(string filename)
    {
        await using var service = new InspectionService();
        var source = Path.Combine(AppContext.BaseDirectory, "TestFiles", filename);
        var destination = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.a11ytest");
        try
        {
            var original = await service.LoadAsync(source);
            Assert.IsTrue(original.Elements.Count > 0);
            Assert.IsTrue(original.Properties.Count > 0);
            Assert.IsTrue(original.Results.Count > 0);
            var screenshot = await service.GetScreenshotAsync(false);
            Assert.IsNotNull(screenshot);
            using (var stream = new MemoryStream(screenshot))
            using (var image = System.Drawing.Image.FromStream(stream))
            {
                Assert.IsTrue(image.Width > 0 && image.Height > 0);
            }

            await service.SaveAsync(destination);
            var reloaded = await service.LoadAsync(destination);
            Assert.AreEqual(original.Name, reloaded.Name);
            CollectionAssert.AreEqual(original.Properties.ToArray(), reloaded.Properties.ToArray());
            CollectionAssert.AreEqual(original.Elements.ToArray(), reloaded.Elements.ToArray());
            CollectionAssert.AreEqual(original.Results.ToArray(), reloaded.Results.ToArray());
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(service.ScanAsync);
        }

        finally
        {
            File.Delete(destination);
        }
    }

    [TestMethod]
    public async Task LegacyEventsIncludingNullPropertiesRoundTripWithoutLosingDetails()
    {
        await using var service = new InspectionService();
        var source = Path.Combine(AppContext.BaseDirectory, "TestFiles", "WildlifeManagerTest.a11yevent");
        var destination = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.a11yevent");
        try
        {
            var original = await service.LoadEventsAsync(source);
            Assert.IsTrue(original.Count > 0);
            Assert.IsTrue(original.Any(row => row.Event == "20005"));
            await service.SaveEventsAsync(destination);
            var reloaded = await service.LoadEventsAsync(destination);
            CollectionAssert.AreEqual(original.ToArray(), reloaded.ToArray());
        }
        finally { File.Delete(destination); }
    }

    [TestMethod]
    public async Task NullEventEntriesAreRejectedWithoutDiscardingPreviouslyLoadedEvents()
    {
        await using var service = new InspectionService();
        var source = Path.Combine(AppContext.BaseDirectory, "TestFiles", "WildlifeManagerTest.a11yevent");
        var invalid = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.a11yevent");
        var destination = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.a11yevent");
        try
        {
            var original = await service.LoadEventsAsync(source);
            await File.WriteAllTextAsync(invalid, "[null]");
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.LoadEventsAsync(invalid));
            await service.SaveEventsAsync(destination);
            CollectionAssert.AreEqual(original.ToArray(), (await service.LoadEventsAsync(destination)).ToArray());
        }
        finally
        {
            File.Delete(invalid);
            File.Delete(destination);
        }
    }

    [TestMethod]
    public async Task ScanWithoutSelectionReportsAnActionableError()
    {
        await using var service = new InspectionService();
        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(service.ScanAsync);
        StringAssert.Contains(exception.Message, "Select an element");
    }

    [TestMethod]
    public async Task SaveWithoutSelectionDoesNotCreateAnEmptySuccessFile()
    {
        await using var service = new InspectionService();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.a11ytest");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.SaveAsync(path));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task DisposedServiceRejectsNewOperationsAndCanBeDisposedTwice()
    {
        var service = new InspectionService();
        await service.DisposeAsync();
        await service.DisposeAsync();
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(service.ScanAsync);
    }
}
