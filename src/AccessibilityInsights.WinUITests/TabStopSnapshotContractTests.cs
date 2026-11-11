// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Compression;
using System.Reflection;

namespace AccessibilityInsights.WinUITests;

[TestClass]
[DoNotParallelize]
public sealed class TabStopSnapshotContractTests
{
    [TestMethod]
    public async Task LegacySnapshotRoundTripDoesNotInventTabStopPersistence()
    {
        var input = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WildlifeManagerTest.a11ytest");
        var output = Path.Combine(Path.GetTempPath(), $"WinUITabStops-{Guid.NewGuid():N}.a11ytest");
        try
        {
            await using var service = new InspectionService();
            await service.OpenAsync(input);
            AddTransientTabStop(service);
            var beforeSave = await service.GetTabStopsAsync();
            Assert.AreEqual(1, beforeSave.Items.Count);
            Assert.AreEqual(3, beforeSave.Items[0].Order);
            Assert.AreEqual("button 'Save'", beforeSave.Items[0].Element.Sender);

            await service.SaveAsync(output);
            Assert.AreEqual(1, (await service.GetTabStopsAsync()).Items.Count);
            using (var package = ZipFile.OpenRead(output))
            {
                Assert.IsFalse(package.Entries.Any(entry =>
                    entry.FullName.Contains("tabstop", StringComparison.OrdinalIgnoreCase)));
            }

            await service.OpenAsync(output);
            Assert.AreEqual(0, (await service.GetTabStopsAsync()).Items.Count);
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static void AddTransientTabStop(InspectionService service)
    {
        var field = typeof(InspectionService).GetField(
            "recordedTabStops", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var records = (Queue<TabStopRecord>)field.GetValue(service)!;
        records.Enqueue(new TabStopRecord(
            1,
            3,
            "09:58:38.455",
            new RecordedElement(
                7,
                "Save",
                "button",
                "SaveButton",
                100,
                "[7,100,200]",
                "button 'Save'",
                [])));
    }
}
