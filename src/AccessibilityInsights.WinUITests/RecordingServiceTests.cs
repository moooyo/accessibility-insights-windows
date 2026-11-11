// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;

namespace AccessibilityInsights.WinUITests;

[TestClass]
[DoNotParallelize]
public sealed class RecordingServiceTests
{
    private const string LegacyEvents = """
        [
          {
            "EventId": 0,
            "TimeStamp": "09:58:37.859",
            "Properties": [
              {
                "Key": "Message",
                "Value": "Succeeded to register an event listener"
              },
              {
                "Key": "Event Id",
                "Value": 20005
              }
            ],
            "Element": null
          },
          {
            "EventId": 20005,
            "TimeStamp": "09:58:38.455",
            "Properties": null,
            "Element": {
              "Orientation": 0,
              "Glimpse": "button 'Save'",
              "UniqueId": 42,
              "Properties": {
                "30000": {
                  "Value": [ 7, 100, 200 ],
                  "Id": 30000,
                  "Name": "RuntimeId"
                },
                "30002": {
                  "Value": 100,
                  "Id": 30002,
                  "Name": "ProcessId"
                },
                "30003": {
                  "Value": 50000,
                  "Id": 30003,
                  "Name": "ControlType"
                },
                "30004": {
                  "Value": "button",
                  "Id": 30004,
                  "Name": "LocalizedControlType"
                },
                "30005": {
                  "Value": "Save",
                  "Id": 30005,
                  "Name": "Name"
                },
                "30011": {
                  "Value": "SaveButton",
                  "Id": 30011,
                  "Name": "AutomationId"
                }
              },
              "PlatformProperties": {},
              "Patterns": [],
              "Children": [],
              "TreeWalkerMode": 0,
              "IsAncestorOfSelected": false,
              "ScanResults": null,
              "IssueDisplayText": null
            }
          }
        ]
        """;

    [TestMethod]
    public async Task LegacyEventFileRoundTripsWithoutChangingItsPayload()
    {
        var input = Path.Combine(Path.GetTempPath(), $"WinUIEventsInput-{Guid.NewGuid():N}.a11yevent");
        var output = Path.Combine(Path.GetTempPath(), $"WinUIEventsOutput-{Guid.NewGuid():N}.a11yevent");
        try
        {
            await File.WriteAllTextAsync(input, LegacyEvents);
            await using var service = new InspectionService();

            var imported = await service.ImportEventsAsync(input);

            Assert.AreEqual(2, imported.Items.Count);
            Assert.AreEqual(0, imported.Items[0].EventId);
            Assert.AreEqual("09:58:38.455", imported.Items[1].TimeStamp);
            Assert.AreEqual("AutomationFocusChanged", imported.Items[1].EventName);
            Assert.AreEqual("button 'Save'", imported.Items[1].Sender);
            Assert.AreEqual("SaveButton", imported.Items[1].Element!.AutomationId);

            await service.ExportEventsAsync(output);
            var originalJson = JsonNode.Parse(LegacyEvents);
            var exportedJson = JsonNode.Parse(await File.ReadAllTextAsync(output));
            Assert.IsTrue(JsonNode.DeepEquals(originalJson, exportedJson));
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [TestMethod]
    public async Task InvalidEventFileDoesNotReplaceExistingRecordsOrBreakWorker()
    {
        var valid = Path.Combine(Path.GetTempPath(), $"WinUIEventsValid-{Guid.NewGuid():N}.a11yevent");
        var invalid = Path.Combine(Path.GetTempPath(), $"WinUIEventsInvalid-{Guid.NewGuid():N}.a11yevent");
        try
        {
            await File.WriteAllTextAsync(valid, LegacyEvents);
            await File.WriteAllTextAsync(invalid, "{}");
            await using var service = new InspectionService();
            await service.ImportEventsAsync(valid);

            await Assert.ThrowsExceptionAsync<Newtonsoft.Json.JsonSerializationException>(
                () => service.ImportEventsAsync(invalid));

            var remaining = await service.GetEventsAsync();
            Assert.AreEqual(2, remaining.Items.Count);
        }
        finally
        {
            File.Delete(valid);
            File.Delete(invalid);
        }
    }

    [TestMethod]
    public async Task ImportKeepsNewestRecordsWithinBoundedBuffer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"WinUIEventsBounded-{Guid.NewGuid():N}.a11yevent");
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                Enumerable.Range(0, RecordingBufferLimits.Events + 3)
                    .Select(index => new
                    {
                        EventId = 20005,
                        TimeStamp = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Properties = (object?)null,
                        Element = (object?)null,
                    }));
            await File.WriteAllTextAsync(path, json);
            await using var service = new InspectionService();

            var imported = await service.ImportEventsAsync(path);

            Assert.AreEqual(RecordingBufferLimits.Events, imported.Items.Count);
            Assert.AreEqual(3, imported.DroppedCount);
            Assert.AreEqual("3", imported.Items[0].TimeStamp);
            Assert.AreEqual((RecordingBufferLimits.Events + 2).ToString(
                System.Globalization.CultureInfo.InvariantCulture), imported.Items[^1].TimeStamp);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
