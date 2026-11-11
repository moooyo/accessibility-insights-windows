// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class SettingsMigrationTests
{
    private const string CurrentFixture = """
        {
            "AlwaysOnTop": false,
            "AppVersion": "1.1.700.1",
            "CoreProperties": [30005,30003,30004,30009,30001,30007,30006,30013,30102,30101],
            "CoreTPAttributes": [],
            "DisableTestsInSnapMode": false,
            "EnableTelemetry": false,
            "EventRecordPath": "C:\\blah\\AccessibilityInsightsEventFiles",
            "FontSize": "Small",
            "HighlighterMode": "HighlighterTooltip",
            "HotKeyForActivatingMainWindow": "Alt + F4",
            "HotKeyForMoveToFirstChild": "Alt + F6",
            "HotKeyForMoveToLastChild": "Alt + F7",
            "HotKeyForMoveToNextSibling": "Alt + F8",
            "HotKeyForMoveToParent": "Alt + F5",
            "HotKeyForMoveToPreviousSibling": "Alt + F9",
            "HotKeyForPause": "Alt + F2",
            "HotKeyForRecord": "Alt + F1",
            "HotKeyForSnap": "Alt + F3",
            "IssueReporterSerializedConfigs": "{\"27f21dff-2fb3-4833-be55-25787fce3e17\":\"hello world\"}",
            "IsUnderElementScope": true,
            "MouseSelectionDelayMilliSeconds": 200,
            "PlayScanningSound": true,
            "ReleaseChannel": "Canary",
            "SelectionByFocus": true,
            "SelectionByMouse": true,
            "SelectedIssueReporter": "{27f21dff-2fb3-4833-be55-25787fce3e17}",
            "SerializedCachedConnections": "[]",
            "SerializedSavedConnection": "",
            "ShouldTestAllChromiumContent": true,
            "ShowAllProperties": false,
            "ShowAncestry": true,
            "ShowTelemetryDialog": false,
            "ShowUncertain": false,
            "ShowWelcomeScreenOnLaunch": true,
            "ShowWhitespaceInTextPatternViewer": false,
            "TestReportPath": "C:\\blah\\AccessibilityInsightsTestFiles",
            "TreeViewMode": "Content",
            "Version": "1.1.10"
        }
        """;

    private const string LegacyFixture = """
        {
            "ZoomLevel": 350,
            "SerializedSavedConnection": "",
            "SerializedCachedConnections": "[]",
            "TestReportPath": "C:\\blah\\AccessibilityInsightsTestFiles",
            "EventRecordPath": "C:\\blah\\AccessibilityInsightsEventFiles",
            "HotKeyForRecord": "Alt + F1",
            "HotKeyForPause": "Alt + F2",
            "HotKeyForSnap": "Alt + F3",
            "HotKeyForActivatingMainWindow": "Alt + F4",
            "HotKeyForMoveToParent": "Alt + F5",
            "HotKeyForMoveToFirstChild": "Alt + F6",
            "HotKeyForMoveToLastChild": "Alt + F7",
            "HotKeyForMoveToNextSibbling": "Alt + F8",
            "HotKeyForMoveToPreviousSibbling": "Alt + F9",
            "CoreProperties": [30005,30003,30004,30009,30001,30007,30006,30013,30102,30101],
            "CoreTPAttributes": [],
            "MouseSelectionDelayMilliSeconds": 200,
            "ShowWelcomeScreenOnLaunch": true,
            "SelectionByFocus": true,
            "SelectionByMouse": true,
            "AlwaysOnTop": false,
            "FontSize": 0,
            "HighlighterMode": 2,
            "PlayScanningSound": false,
            "DisableTestsInSnapMode": false,
            "ShowAncestry": true,
            "ShowAllProperties": false,
            "ShowUncertain": false,
            "ShowWhitespaceInTextPatternViewer": false,
            "TreeViewMode": 2,
            "IsUnderElementScope": true,
            "EnableTelemetry": false,
            "ShowTelemetryDialog": false,
            "Version": "1.1.9",
            "AppVersion": "1.1.700.1"
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DefaultsPreserveOriginalPathsValuesAndEnumOrdinals()
    {
        string root = CreateTestRoot();
        var options = new LaunchOptions
        {
            ConfigFolder = Path.Combine(root, "config"),
            UserDataFolder = Path.Combine(root, "user"),
        };
        var paths = new SettingsPaths(options);
        AppSettings settings = AppSettings.CreateDefault(paths, "9.8.7");

        Assert.AreEqual(Path.GetFullPath(options.ConfigFolder!), paths.ConfigurationFolder);
        Assert.AreEqual(Path.GetFullPath(options.UserDataFolder!), settings.TestReportPath);
        Assert.AreEqual("Shift + F7", settings.HotKeyForRecord);
        Assert.AreEqual("Control,Shift + F8", settings.HotKeyForMoveToNextSibling);
        Assert.AreEqual(100, settings.MouseSelectionDelayMilliSeconds);
        Assert.AreEqual(1, (int)settings.FontSize);
        Assert.AreEqual(0, (int)settings.HighlighterMode);
        Assert.AreEqual(0, (int)settings.SoundFeedback);
        Assert.AreEqual(1, (int)settings.TreeViewMode);
        Assert.AreEqual(0, (int)settings.ReleaseChannel);
        CollectionAssert.AreEqual(AppSettings.DefaultCoreProperties, settings.CoreProperties);
    }

    [TestMethod]
    public void CurrentFixtureRoundTripsIssueConfigAndUnknownLegacyFields()
    {
        var paths = CreatePaths();
        AppSettings settings = AppSettingsSerializer.Deserialize(CurrentFixture, paths, "2.0.0");

        Assert.AreEqual(FontSize.Small, settings.FontSize);
        Assert.AreEqual(HighlighterMode.HighlighterTooltip, settings.HighlighterMode);
        Assert.AreEqual(SoundFeedbackMode.Always, settings.SoundFeedback);
        Assert.AreEqual(TreeViewMode.Content, settings.TreeViewMode);
        Assert.AreEqual(ReleaseChannel.Canary, settings.ReleaseChannel);
        Assert.AreEqual("Alt + F8", settings.HotKeyForMoveToNextSibling);
        Assert.AreEqual(new Guid("27f21dff-2fb3-4833-be55-25787fce3e17"), settings.SelectedIssueReporter);

        JsonObject saved = JsonNode.Parse(AppSettingsSerializer.Serialize(settings))!.AsObject();
        Assert.AreEqual("[]", saved["SerializedCachedConnections"]!.GetValue<string>());
        Assert.AreEqual(string.Empty, saved["SerializedSavedConnection"]!.GetValue<string>());
        Assert.AreEqual(AppSettings.CurrentVersion, saved["Version"]!.GetValue<string>());
        Assert.AreEqual("2.0.0", saved["AppVersion"]!.GetValue<string>());
    }

    [TestMethod]
    public void LegacyFixtureRemapsMisspelledKeysAndNumericEnums()
    {
        AppSettings settings = AppSettingsSerializer.Deserialize(LegacyFixture, CreatePaths(), "2.0.0");

        Assert.AreEqual("Alt + F8", settings.HotKeyForMoveToNextSibling);
        Assert.AreEqual("Alt + F9", settings.HotKeyForMoveToPreviousSibling);
        Assert.AreEqual(FontSize.Small, settings.FontSize);
        Assert.AreEqual(HighlighterMode.HighlighterTooltip, settings.HighlighterMode);
        Assert.AreEqual(TreeViewMode.Content, settings.TreeViewMode);
        Assert.AreEqual(SoundFeedbackMode.Auto, settings.SoundFeedback);

        JsonObject saved = JsonNode.Parse(AppSettingsSerializer.Serialize(settings))!.AsObject();
        Assert.IsFalse(saved.ContainsKey("HotKeyForMoveToNextSibbling"));
        Assert.IsFalse(saved.ContainsKey("HotKeyForMoveToPreviousSibbling"));
        Assert.AreEqual("Alt + F8", saved["HotKeyForMoveToNextSibling"]!.GetValue<string>());
        Assert.AreEqual(350, saved["ZoomLevel"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task ServiceUsesOverridesAndBacksUpInvalidConfiguration()
    {
        SettingsPaths paths = CreatePaths();
        Directory.CreateDirectory(paths.ConfigurationFolder);
        await File.WriteAllTextAsync(paths.ConfigurationFile, "{invalid");
        using var service = new AppSettingsService(paths, "2.0.0");

        AppSettings settings = await service.InitializeAsync();

        Assert.AreEqual(paths.UserDataFolder, settings.TestReportPath);
        Assert.IsTrue(File.Exists(paths.ConfigurationFile));
        Assert.AreEqual(1, Directory.GetFiles(paths.ConfigurationFolder, "Configuration.json.*.bak").Length);
    }

    [TestMethod]
    public async Task ServiceRejectsDuplicateShortcutsWithoutTouchingDisk()
    {
        SettingsPaths paths = CreatePaths();
        using var service = new AppSettingsService(paths, "2.0.0");
        AppSettings original = await service.InitializeAsync();
        string before = await File.ReadAllTextAsync(paths.ConfigurationFile);
        original.HotKeyForPause = original.HotKeyForRecord;

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.SaveAsync(original));

        Assert.AreEqual(before, await File.ReadAllTextAsync(paths.ConfigurationFile));
    }

    [TestMethod]
    public async Task LayoutVersionMismatchRestoresOriginalDefaults()
    {
        SettingsPaths paths = CreatePaths();
        Directory.CreateDirectory(paths.ConfigurationFolder);
        await File.WriteAllTextAsync(paths.LayoutFile, """
            {
              "Version": "1.0.0",
              "Top": 999,
              "Left": 999,
              "Width": 1,
              "Height": 1,
              "WinState": 2
            }
            """);
        var service = new WindowLayoutSettingsService(paths, "2.0.0");

        WindowLayoutSettings settings = await service.LoadAsync(12, 34);

        Assert.AreEqual(WindowLayoutSettings.CurrentVersion, settings.Version);
        Assert.AreEqual(12, settings.Top);
        Assert.AreEqual(34, settings.Left);
        Assert.AreEqual(870, settings.Width);
        Assert.AreEqual(720, settings.Height);
        Assert.AreEqual(SavedWindowState.Normal, settings.WinState);
        Assert.AreEqual(460, settings.LayoutLive.Width);
        Assert.AreEqual("2.0.0", settings.LayoutLive.AppVersion);
        Assert.AreEqual(260, settings.LayoutSnapshot.RowTestHeight);
    }

    [TestMethod]
    public async Task RecorderSettingsMigrateLegacyPathAndMergeNewEvents()
    {
        SettingsPaths paths = CreatePaths();
        Directory.CreateDirectory(paths.UserDataFolder);
        string legacyPath = Path.Combine(paths.UserDataFolder, SettingsPaths.EventConfigurationFileName);
        await File.WriteAllTextAsync(legacyPath, """
            {
              "Events": [],
              "Properties": [],
              "IsListeningFocusChangedEvent": false,
              "IsListeningAllEvents": true,
              "ListenScope": 1
            }
            """);
        var service = new RecorderSettingsService(paths, "2.0.0");

        RecorderSettings settings = await service.LoadAsync();

        Assert.IsFalse(File.Exists(legacyPath));
        Assert.IsTrue(File.Exists(paths.EventConfigurationFile));
        Assert.IsTrue(settings.Events.Count > 0);
        Assert.AreEqual(0, settings.Properties.Count);
        Assert.IsFalse(settings.IsListeningFocusChangedEvent);
        Assert.IsFalse(settings.IsListeningAllEvents);
        Assert.AreEqual(RecorderListenScope.Element, settings.ListenScope);
    }

    [TestMethod]
    public async Task RecorderDefaultsPreserveOriginalScopeAndCatalogs()
    {
        SettingsPaths paths = CreatePaths();
        var service = new RecorderSettingsService(paths, "2.0.0");

        RecorderSettings settings = await service.LoadAsync();

        Assert.IsTrue(settings.Events.Count > 0);
        Assert.IsTrue(settings.Properties.Count > 0);
        Assert.IsTrue(settings.IsListeningFocusChangedEvent);
        Assert.IsFalse(settings.IsListeningAllEvents);
        Assert.AreEqual(RecorderListenScope.Subtree, settings.ListenScope);
    }

    private SettingsPaths CreatePaths()
    {
        string root = CreateTestRoot();
        return new SettingsPaths(Path.Combine(root, "config"), Path.Combine(root, "user"));
    }

    private string CreateTestRoot()
    {
        string path = Path.Combine(
            TestContext.TestRunDirectory!,
            nameof(SettingsMigrationTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
