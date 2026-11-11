// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Text.Json.Nodes;

namespace AccessibilityInsights.Core.Settings;

public enum FontSize
{
    Small,
    Standard,
}

public enum HighlighterMode
{
    HighlighterBeakerTooltip,
    HighlighterBeaker,
    HighlighterTooltip,
    Highlighter,
    Tooltip,
}

public enum SoundFeedbackMode
{
    Auto,
    Always,
    Never,
}

public enum TreeViewMode
{
    Raw,
    Control,
    Content,
}

public enum ReleaseChannel
{
    Production,
    Insider,
    Canary,
}

public sealed class AppSettings
{
    public const string CurrentVersion = "1.1.10";
    public const int MinimumSelectionDelayMilliseconds = 50;
    public const int DefaultSelectionDelayMilliseconds = 100;

    public const string DefaultHotKeyForActivatingMainWindow = "Shift + F9";
    public const string DefaultHotKeyForMoveToFirstChild = "Control,Shift + F7";
    public const string DefaultHotKeyForMoveToLastChild = "Control,Shift + F9";
    public const string DefaultHotKeyForMoveToNextSibling = "Control,Shift + F8";
    public const string DefaultHotKeyForMoveToParent = "Control,Shift + F6";
    public const string DefaultHotKeyForMoveToPreviousSibling = "Control,Shift + F5";
    public const string DefaultHotKeyForPause = "Shift + F5";
    public const string DefaultHotKeyForRecord = "Shift + F7";
    public const string DefaultHotKeyForSnap = "Shift + F8";
    internal const string OldHotKeyForRecord = "Shift + F6";

    public static readonly int[] DefaultCoreProperties =
    [
        30005,
        30003,
        30004,
        30009,
        30001,
        30007,
        30006,
        30013,
        30102,
        30101,
    ];

    public string Version { get; set; } = CurrentVersion;
    public string AppVersion { get; set; } = string.Empty;
    public Guid SelectedIssueReporter { get; set; }
    public string? IssueReporterSerializedConfigs { get; set; }
    public string TestReportPath { get; set; } = string.Empty;
    public string EventRecordPath { get; set; } = string.Empty;
    public string? HotKeyForRecord { get; set; }
    public string? HotKeyForPause { get; set; }
    public string? HotKeyForSnap { get; set; }
    public string? HotKeyForActivatingMainWindow { get; set; }
    public string? HotKeyForMoveToParent { get; set; }
    public string? HotKeyForMoveToFirstChild { get; set; }
    public string? HotKeyForMoveToLastChild { get; set; }
    public string? HotKeyForMoveToNextSibling { get; set; }
    public string? HotKeyForMoveToPreviousSibling { get; set; }
    public int[] CoreProperties { get; set; } = [];
    public int[] CoreTPAttributes { get; set; } = [];
    public int MouseSelectionDelayMilliSeconds { get; set; }
    public bool ShowWelcomeScreenOnLaunch { get; set; }
    public bool SelectionByFocus { get; set; }
    public bool SelectionByMouse { get; set; }
    public bool AlwaysOnTop { get; set; }
    public FontSize FontSize { get; set; }
    public HighlighterMode HighlighterMode { get; set; }
    public SoundFeedbackMode SoundFeedback { get; set; }
    public bool DisableTestsInSnapMode { get; set; }
    public bool ShouldTestAllChromiumContent { get; set; }
    public bool ShowAncestry { get; set; }
    public bool ShowAllProperties { get; set; }
    public bool IsHighlighterOn { get; set; } = true;
    public bool ShowUncertain { get; set; }
    public bool ShowWhitespaceInTextPatternViewer { get; set; }
    public TreeViewMode TreeViewMode { get; set; }
    public bool IsUnderElementScope { get; set; }
    public ReleaseChannel ReleaseChannel { get; set; }
    public bool EnableTelemetry { get; set; }
    public bool ShowTelemetryDialog { get; set; }

    internal JsonObject AdditionalSettings { get; set; } = [];

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Version = Version,
            AppVersion = AppVersion,
            SelectedIssueReporter = SelectedIssueReporter,
            IssueReporterSerializedConfigs = IssueReporterSerializedConfigs,
            TestReportPath = TestReportPath,
            EventRecordPath = EventRecordPath,
            HotKeyForRecord = HotKeyForRecord,
            HotKeyForPause = HotKeyForPause,
            HotKeyForSnap = HotKeyForSnap,
            HotKeyForActivatingMainWindow = HotKeyForActivatingMainWindow,
            HotKeyForMoveToParent = HotKeyForMoveToParent,
            HotKeyForMoveToFirstChild = HotKeyForMoveToFirstChild,
            HotKeyForMoveToLastChild = HotKeyForMoveToLastChild,
            HotKeyForMoveToNextSibling = HotKeyForMoveToNextSibling,
            HotKeyForMoveToPreviousSibling = HotKeyForMoveToPreviousSibling,
            CoreProperties = [.. CoreProperties],
            CoreTPAttributes = [.. CoreTPAttributes],
            MouseSelectionDelayMilliSeconds = MouseSelectionDelayMilliSeconds,
            ShowWelcomeScreenOnLaunch = ShowWelcomeScreenOnLaunch,
            SelectionByFocus = SelectionByFocus,
            SelectionByMouse = SelectionByMouse,
            AlwaysOnTop = AlwaysOnTop,
            FontSize = FontSize,
            HighlighterMode = HighlighterMode,
            SoundFeedback = SoundFeedback,
            DisableTestsInSnapMode = DisableTestsInSnapMode,
            ShouldTestAllChromiumContent = ShouldTestAllChromiumContent,
            ShowAncestry = ShowAncestry,
            ShowAllProperties = ShowAllProperties,
            IsHighlighterOn = IsHighlighterOn,
            ShowUncertain = ShowUncertain,
            ShowWhitespaceInTextPatternViewer = ShowWhitespaceInTextPatternViewer,
            TreeViewMode = TreeViewMode,
            IsUnderElementScope = IsUnderElementScope,
            ReleaseChannel = ReleaseChannel,
            EnableTelemetry = EnableTelemetry,
            ShowTelemetryDialog = ShowTelemetryDialog,
            AdditionalSettings = (JsonObject)AdditionalSettings.DeepClone(),
        };
    }

    public static AppSettings CreateDefault(SettingsPaths paths, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new AppSettings
        {
            AppVersion = appVersion,
            Version = CurrentVersion,
            TestReportPath = paths.UserDataFolder,
            EventRecordPath = paths.UserDataFolder,
            HotKeyForRecord = DefaultHotKeyForRecord,
            HotKeyForPause = DefaultHotKeyForPause,
            HotKeyForSnap = DefaultHotKeyForSnap,
            HotKeyForActivatingMainWindow = DefaultHotKeyForActivatingMainWindow,
            HotKeyForMoveToParent = DefaultHotKeyForMoveToParent,
            HotKeyForMoveToFirstChild = DefaultHotKeyForMoveToFirstChild,
            HotKeyForMoveToLastChild = DefaultHotKeyForMoveToLastChild,
            HotKeyForMoveToNextSibling = DefaultHotKeyForMoveToNextSibling,
            HotKeyForMoveToPreviousSibling = DefaultHotKeyForMoveToPreviousSibling,
            CoreProperties = [.. DefaultCoreProperties],
            CoreTPAttributes = [],
            MouseSelectionDelayMilliSeconds = DefaultSelectionDelayMilliseconds,
            SelectionByFocus = true,
            SelectionByMouse = true,
            ShowWelcomeScreenOnLaunch = true,
            AlwaysOnTop = true,
            SoundFeedback = SoundFeedbackMode.Auto,
            DisableTestsInSnapMode = false,
            IsHighlighterOn = true,
            ShowUncertain = false,
            TreeViewMode = TreeViewMode.Control,
            FontSize = FontSize.Standard,
            HighlighterMode = HighlighterMode.HighlighterBeakerTooltip,
            ShowAncestry = true,
            EnableTelemetry = false,
            ShowTelemetryDialog = true,
            IsUnderElementScope = true,
            IssueReporterSerializedConfigs = null,
            SelectedIssueReporter = Guid.Empty,
            ReleaseChannel = ReleaseChannel.Production,
        };
    }
}
