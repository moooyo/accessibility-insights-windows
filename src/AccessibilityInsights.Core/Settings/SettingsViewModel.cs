// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;

namespace AccessibilityInsights.Core.Settings;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService settingsService;
    private readonly Func<string, string> getString;
    private AppSettings baseline;
    private bool applying;
    private ReleaseChannel persistedReleaseChannel;

    public SettingsViewModel(IAppSettingsService settingsService, Func<string, string> getString)
    {
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.getString = getString ?? throw new ArgumentNullException(nameof(getString));
        baseline = settingsService.CreateDefault();
        persistedReleaseChannel = baseline.ReleaseChannel;
        PropertyChanged += (_, args) =>
        {
            if (!applying &&
                args.PropertyName is not nameof(IsDirty) and
                not nameof(IsBusy) and
                not nameof(Status) and
                not nameof(Error) and
                not nameof(HasError) and
                not nameof(HasReleaseChannelChange))
            {
                IsDirty = true;
                HasReleaseChannelChange = ReleaseChannelIndex != (int)persistedReleaseChannel;
            }
        };
        Apply(baseline, dirty: false);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool HasReleaseChannelChange { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string HotKeyForActivatingMainWindow { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForRecord { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForPause { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForSnap { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForMoveToParent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForMoveToPreviousSibling { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForMoveToNextSibling { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForMoveToFirstChild { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotKeyForMoveToLastChild { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SelectionByFocus { get; set; }

    [ObservableProperty]
    public partial bool SelectionByMouse { get; set; }

    [ObservableProperty]
    public partial int MouseSelectionDelayMilliSeconds { get; set; }

    [ObservableProperty]
    public partial bool AlwaysOnTop { get; set; }

    [ObservableProperty]
    public partial int SoundFeedbackIndex { get; set; }

    [ObservableProperty]
    public partial int HighlighterModeIndex { get; set; }

    [ObservableProperty]
    public partial int FontSizeIndex { get; set; }

    [ObservableProperty]
    public partial int TreeViewModeIndex { get; set; }

    [ObservableProperty]
    public partial int ReleaseChannelIndex { get; set; }

    [ObservableProperty]
    public partial bool EnableTelemetry { get; set; }

    [ObservableProperty]
    public partial bool ShowTelemetryDialog { get; set; }

    [ObservableProperty]
    public partial bool ShowWelcomeScreenOnLaunch { get; set; }

    [ObservableProperty]
    public partial bool DisableTestsInSnapMode { get; set; }

    [ObservableProperty]
    public partial bool ShouldTestAllChromiumContent { get; set; }

    [ObservableProperty]
    public partial bool ShowAncestry { get; set; }

    [ObservableProperty]
    public partial bool ShowAllProperties { get; set; }

    [ObservableProperty]
    public partial bool ShowUncertain { get; set; }

    [ObservableProperty]
    public partial bool ShowWhitespaceInTextPatternViewer { get; set; }

    [ObservableProperty]
    public partial bool IsUnderElementScope { get; set; }

    [ObservableProperty]
    public partial string TestReportPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EventRecordPath { get; set; } = string.Empty;

    public Task InitializeAsync() => RunAsync(async () =>
    {
        AppSettings settings = await settingsService.InitializeAsync().ConfigureAwait(true);
        Apply(settings, dirty: false);
        Status = getString("SettingsLoaded");
    });

    private bool CanSave() => !IsBusy && IsDirty;
    private bool CanReset() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var saved = await settingsService.SaveAsync(BuildSettings()).ConfigureAwait(true);
        Apply(saved.Settings, dirty: false);
        Status = getString("SettingsSaved");
    });

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        Apply(settingsService.CreateDefault(), dirty: true);
        Status = getString("SettingsDefaultsReady");
    }

    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            Error = string.Format(CultureInfo.CurrentCulture, getString("SettingsOperationFailed"), exception.Message);
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(AppSettings settings, bool dirty)
    {
        applying = true;
        try
        {
            if (!dirty)
            {
                persistedReleaseChannel = settings.ReleaseChannel;
            }
            baseline = settings.Clone();
            HotKeyForActivatingMainWindow = settings.HotKeyForActivatingMainWindow ?? string.Empty;
            HotKeyForRecord = settings.HotKeyForRecord ?? string.Empty;
            HotKeyForPause = settings.HotKeyForPause ?? string.Empty;
            HotKeyForSnap = settings.HotKeyForSnap ?? string.Empty;
            HotKeyForMoveToParent = settings.HotKeyForMoveToParent ?? string.Empty;
            HotKeyForMoveToPreviousSibling = settings.HotKeyForMoveToPreviousSibling ?? string.Empty;
            HotKeyForMoveToNextSibling = settings.HotKeyForMoveToNextSibling ?? string.Empty;
            HotKeyForMoveToFirstChild = settings.HotKeyForMoveToFirstChild ?? string.Empty;
            HotKeyForMoveToLastChild = settings.HotKeyForMoveToLastChild ?? string.Empty;
            SelectionByFocus = settings.SelectionByFocus;
            SelectionByMouse = settings.SelectionByMouse;
            MouseSelectionDelayMilliSeconds = settings.MouseSelectionDelayMilliSeconds;
            AlwaysOnTop = settings.AlwaysOnTop;
            SoundFeedbackIndex = (int)settings.SoundFeedback;
            HighlighterModeIndex = (int)settings.HighlighterMode;
            FontSizeIndex = (int)settings.FontSize;
            TreeViewModeIndex = (int)settings.TreeViewMode;
            ReleaseChannelIndex = (int)settings.ReleaseChannel;
            EnableTelemetry = settings.EnableTelemetry;
            ShowTelemetryDialog = settings.ShowTelemetryDialog;
            ShowWelcomeScreenOnLaunch = settings.ShowWelcomeScreenOnLaunch;
            DisableTestsInSnapMode = settings.DisableTestsInSnapMode;
            ShouldTestAllChromiumContent = settings.ShouldTestAllChromiumContent;
            ShowAncestry = settings.ShowAncestry;
            ShowAllProperties = settings.ShowAllProperties;
            ShowUncertain = settings.ShowUncertain;
            ShowWhitespaceInTextPatternViewer = settings.ShowWhitespaceInTextPatternViewer;
            IsUnderElementScope = settings.IsUnderElementScope;
            TestReportPath = settings.TestReportPath;
            EventRecordPath = settings.EventRecordPath;
            IsDirty = dirty;
            HasReleaseChannelChange = ReleaseChannelIndex != (int)persistedReleaseChannel;
        }
        finally
        {
            applying = false;
        }
    }

    private AppSettings BuildSettings()
    {
        AppSettings settings = baseline.Clone();
        settings.HotKeyForActivatingMainWindow = HotKeyForActivatingMainWindow;
        settings.HotKeyForRecord = HotKeyForRecord;
        settings.HotKeyForPause = HotKeyForPause;
        settings.HotKeyForSnap = HotKeyForSnap;
        settings.HotKeyForMoveToParent = HotKeyForMoveToParent;
        settings.HotKeyForMoveToPreviousSibling = HotKeyForMoveToPreviousSibling;
        settings.HotKeyForMoveToNextSibling = HotKeyForMoveToNextSibling;
        settings.HotKeyForMoveToFirstChild = HotKeyForMoveToFirstChild;
        settings.HotKeyForMoveToLastChild = HotKeyForMoveToLastChild;
        settings.SelectionByFocus = SelectionByFocus;
        settings.SelectionByMouse = SelectionByMouse;
        settings.MouseSelectionDelayMilliSeconds = MouseSelectionDelayMilliSeconds;
        settings.AlwaysOnTop = AlwaysOnTop;
        settings.SoundFeedback = (SoundFeedbackMode)SoundFeedbackIndex;
        settings.HighlighterMode = (HighlighterMode)HighlighterModeIndex;
        settings.FontSize = (FontSize)FontSizeIndex;
        settings.TreeViewMode = (TreeViewMode)TreeViewModeIndex;
        settings.ReleaseChannel = (ReleaseChannel)ReleaseChannelIndex;
        settings.EnableTelemetry = EnableTelemetry;
        settings.ShowTelemetryDialog = ShowTelemetryDialog;
        settings.ShowWelcomeScreenOnLaunch = ShowWelcomeScreenOnLaunch;
        settings.DisableTestsInSnapMode = DisableTestsInSnapMode;
        settings.ShouldTestAllChromiumContent = ShouldTestAllChromiumContent;
        settings.ShowAncestry = ShowAncestry;
        settings.ShowAllProperties = ShowAllProperties;
        settings.ShowUncertain = ShowUncertain;
        settings.ShowWhitespaceInTextPatternViewer = ShowWhitespaceInTextPatternViewer;
        settings.IsUnderElementScope = IsUnderElementScope;
        settings.TestReportPath = TestReportPath;
        settings.EventRecordPath = EventRecordPath;
        return settings;
    }
}
