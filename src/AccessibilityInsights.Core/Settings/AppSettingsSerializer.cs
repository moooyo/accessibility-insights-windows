// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccessibilityInsights.Core.Settings;

public static class AppSettingsSerializer
{
    private const string LegacyNextSiblingKey = "HotKeyForMoveToNextSibbling";
    private const string LegacyPreviousSiblingKey = "HotKeyForMoveToPreviousSibbling";
    private const string LegacySoundFeedbackKey = "PlayScanningSound";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static AppSettings Deserialize(string json, SettingsPaths paths, string appVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(paths);

        var root = JsonNode.Parse(json) as JsonObject ??
            throw new JsonException("The settings document must contain a JSON object.");
        if (root.Count == 0)
        {
            return AppSettings.CreateDefault(paths, appVersion);
        }

        Remap(root, LegacyNextSiblingKey, "HotKeyForMoveToNextSibling");
        Remap(root, LegacyPreviousSiblingKey, "HotKeyForMoveToPreviousSibling");

        var settings = new AppSettings
        {
            AdditionalSettings = (JsonObject)root.DeepClone(),
            Version = AppSettings.CurrentVersion,
            AppVersion = appVersion,
            SelectedIssueReporter = ReadGuid(root, "SelectedIssueReporter"),
            IssueReporterSerializedConfigs = ReadString(root, "IssueReporterSerializedConfigs"),
            TestReportPath = ReadString(root, "TestReportPath") ?? string.Empty,
            EventRecordPath = ReadString(root, "EventRecordPath") ?? string.Empty,
            HotKeyForRecord = ReadString(root, "HotKeyForRecord"),
            HotKeyForPause = ReadString(root, "HotKeyForPause"),
            HotKeyForSnap = ReadString(root, "HotKeyForSnap"),
            HotKeyForActivatingMainWindow = ReadString(root, "HotKeyForActivatingMainWindow"),
            HotKeyForMoveToParent = ReadString(root, "HotKeyForMoveToParent"),
            HotKeyForMoveToFirstChild = ReadString(root, "HotKeyForMoveToFirstChild"),
            HotKeyForMoveToLastChild = ReadString(root, "HotKeyForMoveToLastChild"),
            HotKeyForMoveToNextSibling = ReadString(root, "HotKeyForMoveToNextSibling"),
            HotKeyForMoveToPreviousSibling = ReadString(root, "HotKeyForMoveToPreviousSibling"),
            CoreProperties = ReadIntArray(root, "CoreProperties") ?? [],
            CoreTPAttributes = ReadIntArray(root, "CoreTPAttributes") ?? [],
            MouseSelectionDelayMilliSeconds = ReadInt(root, "MouseSelectionDelayMilliSeconds"),
            ShowWelcomeScreenOnLaunch = ReadBool(root, "ShowWelcomeScreenOnLaunch"),
            SelectionByFocus = ReadBool(root, "SelectionByFocus"),
            SelectionByMouse = ReadBool(root, "SelectionByMouse"),
            AlwaysOnTop = ReadBool(root, "AlwaysOnTop"),
            FontSize = ReadEnum(root, "FontSize", FontSize.Standard),
            HighlighterMode = ReadEnum(root, "HighlighterMode", default(HighlighterMode)),
            SoundFeedback = ReadEnum(root, "SoundFeedback", default(SoundFeedbackMode)),
            DisableTestsInSnapMode = ReadBool(root, "DisableTestsInSnapMode"),
            ShouldTestAllChromiumContent = ReadBool(root, "ShouldTestAllChromiumContent"),
            ShowAncestry = ReadBool(root, "ShowAncestry"),
            ShowAllProperties = ReadBool(root, "ShowAllProperties"),
            ShowUncertain = ReadBool(root, "ShowUncertain"),
            ShowWhitespaceInTextPatternViewer = ReadBool(root, "ShowWhitespaceInTextPatternViewer"),
            TreeViewMode = ReadEnum(root, "TreeViewMode", default(TreeViewMode)),
            IsUnderElementScope = ReadBool(root, "IsUnderElementScope"),
            ReleaseChannel = ReadEnum(root, "ReleaseChannel", default(ReleaseChannel)),
            EnableTelemetry = ReadBool(root, "EnableTelemetry"),
            ShowTelemetryDialog = ReadBool(root, "ShowTelemetryDialog"),
        };

        if (!root.ContainsKey("SoundFeedback") &&
            root.TryGetPropertyValue(LegacySoundFeedbackKey, out var legacySound) &&
            legacySound is JsonValue legacyValue &&
            legacyValue.TryGetValue(out bool playSound) &&
            playSound)
        {
            settings.SoundFeedback = SoundFeedbackMode.Always;
        }

        if (ContainsMissingRequiredHotKey(settings))
        {
            string? snapshot = settings.HotKeyForSnap;
            string? record = string.Equals(settings.HotKeyForRecord, AppSettings.OldHotKeyForRecord, StringComparison.Ordinal)
                ? null
                : settings.HotKeyForRecord;
            string? pause = settings.HotKeyForPause;
            string? activate = settings.HotKeyForActivatingMainWindow;

            settings = AppSettings.CreateDefault(paths, appVersion);
            settings.HotKeyForRecord = record ?? settings.HotKeyForRecord;
            settings.HotKeyForPause = pause ?? settings.HotKeyForPause;
            settings.HotKeyForSnap = snapshot;
            settings.HotKeyForActivatingMainWindow = activate;
        }

        if (settings.CoreProperties.Length == 0)
        {
            settings.CoreProperties = [.. AppSettings.DefaultCoreProperties];
        }

        settings.CoreTPAttributes ??= [];
        settings.Version = AppSettings.CurrentVersion;
        settings.AppVersion = appVersion;
        return settings;
    }

    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        JsonObject root = (JsonObject)settings.AdditionalSettings.DeepClone();

        Set(root, "AlwaysOnTop", settings.AlwaysOnTop);
        Set(root, "AppVersion", settings.AppVersion);
        Set(root, "CoreProperties", settings.CoreProperties);
        Set(root, "CoreTPAttributes", settings.CoreTPAttributes);
        Set(root, "DisableTestsInSnapMode", settings.DisableTestsInSnapMode);
        Set(root, "EnableTelemetry", settings.EnableTelemetry);
        Set(root, "EventRecordPath", settings.EventRecordPath);
        Set(root, "FontSize", settings.FontSize.ToString());
        Set(root, "HighlighterMode", settings.HighlighterMode.ToString());
        Set(root, "HotKeyForActivatingMainWindow", settings.HotKeyForActivatingMainWindow);
        Set(root, "HotKeyForMoveToFirstChild", settings.HotKeyForMoveToFirstChild);
        Set(root, "HotKeyForMoveToLastChild", settings.HotKeyForMoveToLastChild);
        Set(root, "HotKeyForMoveToNextSibling", settings.HotKeyForMoveToNextSibling);
        Set(root, "HotKeyForMoveToParent", settings.HotKeyForMoveToParent);
        Set(root, "HotKeyForMoveToPreviousSibling", settings.HotKeyForMoveToPreviousSibling);
        Set(root, "HotKeyForPause", settings.HotKeyForPause);
        Set(root, "HotKeyForRecord", settings.HotKeyForRecord);
        Set(root, "HotKeyForSnap", settings.HotKeyForSnap);
        Set(root, "IssueReporterSerializedConfigs", settings.IssueReporterSerializedConfigs);
        Set(root, "IsUnderElementScope", settings.IsUnderElementScope);
        Set(root, "MouseSelectionDelayMilliSeconds", settings.MouseSelectionDelayMilliSeconds);
        Set(root, "ReleaseChannel", settings.ReleaseChannel.ToString());
        Set(root, "SelectedIssueReporter", settings.SelectedIssueReporter.ToString());
        Set(root, "SelectionByFocus", settings.SelectionByFocus);
        Set(root, "SelectionByMouse", settings.SelectionByMouse);
        Set(root, "ShouldTestAllChromiumContent", settings.ShouldTestAllChromiumContent);
        Set(root, "ShowAllProperties", settings.ShowAllProperties);
        Set(root, "ShowAncestry", settings.ShowAncestry);
        Set(root, "ShowTelemetryDialog", settings.ShowTelemetryDialog);
        Set(root, "ShowUncertain", settings.ShowUncertain);
        Set(root, "ShowWelcomeScreenOnLaunch", settings.ShowWelcomeScreenOnLaunch);
        Set(root, "ShowWhitespaceInTextPatternViewer", settings.ShowWhitespaceInTextPatternViewer);
        Set(root, "SoundFeedback", settings.SoundFeedback.ToString());
        Set(root, "TestReportPath", settings.TestReportPath);
        Set(root, "TreeViewMode", settings.TreeViewMode.ToString());
        Set(root, "Version", AppSettings.CurrentVersion);

        var sorted = new JsonObject();
        foreach (var pair in root.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            sorted[pair.Key] = pair.Value?.DeepClone();
        }

        return sorted.ToJsonString(SerializerOptions);
    }

    internal static IReadOnlySet<string> GetChangedKeys(AppSettings before, AppSettings after)
    {
        var beforeObject = JsonNode.Parse(Serialize(before))!.AsObject();
        var afterObject = JsonNode.Parse(Serialize(after))!.AsObject();
        var keys = new HashSet<string>(beforeObject.Select(pair => pair.Key), StringComparer.Ordinal);
        keys.UnionWith(afterObject.Select(pair => pair.Key));
        keys.RemoveWhere(key => JsonNode.DeepEquals(beforeObject[key], afterObject[key]));
        return keys;
    }

    private static bool ContainsMissingRequiredHotKey(AppSettings settings) =>
        settings.HotKeyForRecord is null ||
        settings.HotKeyForPause is null ||
        settings.HotKeyForSnap is null ||
        settings.HotKeyForActivatingMainWindow is null;

    private static void Remap(JsonObject root, string oldKey, string newKey)
    {
        if (!root.TryGetPropertyValue(oldKey, out var value))
        {
            return;
        }

        if (!root.ContainsKey(newKey))
        {
            root[newKey] = value?.DeepClone();
        }

        root.Remove(oldKey);
    }

    private static string? ReadString(JsonObject root, string name) =>
        root[name] is JsonValue value && value.TryGetValue(out string? result) ? result : null;

    private static bool ReadBool(JsonObject root, string name) =>
        root[name] is JsonValue value && value.TryGetValue(out bool result) && result;

    private static int ReadInt(JsonObject root, string name)
    {
        if (root[name] is not JsonValue value)
        {
            return default;
        }

        if (value.TryGetValue(out int intValue))
        {
            return intValue;
        }

        return value.TryGetValue(out long longValue) && longValue is >= int.MinValue and <= int.MaxValue
            ? (int)longValue
            : default;
    }

    private static int[]? ReadIntArray(JsonObject root, string name)
    {
        if (root[name] is not JsonArray array)
        {
            return null;
        }

        var values = new List<int>(array.Count);
        foreach (var node in array)
        {
            if (node is not JsonValue value || !value.TryGetValue(out int item))
            {
                return null;
            }

            values.Add(item);
        }

        return [.. values];
    }

    private static Guid ReadGuid(JsonObject root, string name) =>
        Guid.TryParse(ReadString(root, name), out Guid value) ? value : Guid.Empty;

    private static T ReadEnum<T>(JsonObject root, string name, T defaultValue) where T : struct, Enum
    {
        if (root[name] is not JsonValue value)
        {
            return defaultValue;
        }

        if (value.TryGetValue(out string? text) &&
            Enum.TryParse(text, ignoreCase: false, out T parsedByName) &&
            Enum.IsDefined(parsedByName))
        {
            return parsedByName;
        }

        int numeric = ReadInt(root, name);
        return Enum.IsDefined(typeof(T), numeric)
            ? (T)Enum.ToObject(typeof(T), numeric)
            : defaultValue;
    }

    private static void Set<T>(JsonObject root, string name, T value) =>
        root[name] = JsonValue.Create(value);
}
