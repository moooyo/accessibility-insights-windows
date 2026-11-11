// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace AccessibilityInsights.Core;

public partial class WorkbenchViewModel : ObservableObject
{
    private readonly IInspectionService inspection;
    private readonly ILiveInspectionService? liveInspection;
    private readonly IInspectionImagingService? imaging;
    private readonly ISnapshotFilePicker files;
    private readonly Func<string, string> getString;
    private Guid? activeContextId;
    private InspectionKind currentInspectionKind;
    private long selectionVersion;
    private bool suppressSelectionRefresh;

    public WorkbenchViewModel(IInspectionService inspection, ISnapshotFilePicker files, Func<string, string> getString)
    {
        this.inspection = inspection;
        liveInspection = inspection as ILiveInspectionService;
        imaging = inspection as IInspectionImagingService;
        this.files = files;
        this.getString = getString;
        Status = getString("SelectTarget");
    }

    public ObservableCollection<TargetWindow> Windows { get; } = [];
    public ObservableCollection<ElementSnapshot> Roots { get; } = [];
    public ObservableCollection<ElementProperty> Properties { get; } = [];
    public ObservableCollection<ElementPattern> Patterns { get; } = [];
    public ObservableCollection<RuleFailure> Failures { get; } = [];

    public Guid? ActiveContextId
    {
        get => activeContextId;
        private set
        {
            if (SetProperty(ref activeContextId, value))
            {
                OnPropertyChanged(nameof(HasLiveSnapshot));
                UpdateLiveCommandStates();
            }
        }
    }

    public InspectionKind CurrentInspectionKind
    {
        get => currentInspectionKind;
        private set
        {
            if (SetProperty(ref currentInspectionKind, value))
            {
                OnPropertyChanged(nameof(HasLiveSnapshot));
                UpdateLiveCommandStates();
            }
        }
    }

    public bool HasLiveSnapshot => ActiveContextId.HasValue && CurrentInspectionKind == InspectionKind.Live;

    public LiveInspectionOptions LiveOptions => new(
        SelectionScope,
        (TrackMouse ? LiveSelectionMethods.Mouse : LiveSelectionMethods.None) |
        (TrackKeyboardFocus ? LiveSelectionMethods.KeyboardFocus : LiveSelectionMethods.None),
        MouseSelectionDelayMilliseconds);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshWindowsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTrackingCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleHighlightCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureScreenshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    public partial TargetWindow? SelectedWindow { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleHighlightCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureScreenshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand))]
    public partial ElementSnapshot? SelectedElement { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool HasSnapshot { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleTrackingCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    public partial bool IsTracking { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial bool TrackMouse { get; set; } = true;

    [ObservableProperty]
    public partial bool TrackKeyboardFocus { get; set; } = true;

    [ObservableProperty]
    public partial LiveSelectionScope SelectionScope { get; set; } = LiveSelectionScope.Element;

    [ObservableProperty]
    public partial int MouseSelectionDelayMilliseconds { get; set; } = 100;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleHighlightCommand))]
    public partial bool IsHighlightEnabled { get; set; } = true;

    [ObservableProperty]
    public partial InspectionScreenshot? Screenshot { get; set; }

    [ObservableProperty]
    public partial bool HasScreenshot { get; set; }

    private bool CanRun() => !IsBusy;
    private bool CanCapture() => !IsBusy && SelectedWindow is not null;
    private bool CanTest() => !IsBusy && (SelectedWindow is not null || HasLiveSnapshot);
    private bool CanSave() => !IsBusy && HasSnapshot;
    private bool CanToggleTracking() => !IsBusy && liveInspection is not null;
    private bool CanTogglePause() => !IsBusy && IsTracking && liveInspection is not null;
    private bool CanToggleHighlight() =>
        !IsBusy && liveInspection is not null && SelectedElement is not null &&
        ActiveContextId.HasValue && CurrentInspectionKind != InspectionKind.Loaded;
    private bool CanCaptureScreenshot() =>
        !IsBusy && imaging is not null && SelectedElement is not null && ActiveContextId.HasValue;
    private bool CanNavigate(TreeNavigationDirection direction) =>
        !IsBusy && liveInspection is not null && SelectedElement is not null && HasLiveSnapshot;

    partial void OnSelectedElementChanged(ElementSnapshot? value)
    {
        var version = Interlocked.Increment(ref selectionVersion);
        Properties.Clear();
        Patterns.Clear();
        Screenshot = null;
        HasScreenshot = false;
        if (value is not null)
        {
            foreach (var property in value.Properties)
            {
                Properties.Add(property);
            }
        }

        if (!suppressSelectionRefresh && value is not null && ActiveContextId is Guid contextId &&
            liveInspection is not null)
        {
            _ = RefreshSelectedElementAsync(value, contextId, version);
        }
        else if (!suppressSelectionRefresh && value is null && liveInspection is not null)
        {
            _ = ClearHighlightSafelyAsync();
        }
    }

    public async Task SelectElementAsync(ElementSnapshot? element)
    {
        suppressSelectionRefresh = true;
        try
        {
            SelectedElement = element;
        }
        finally
        {
            suppressSelectionRefresh = false;
        }

        if (element is not null && ActiveContextId is Guid contextId && liveInspection is not null)
        {
            await RefreshSelectedElementAsync(element, contextId, Volatile.Read(ref selectionVersion));
        }
    }

    public void ApplyLiveSnapshot(InspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsTracking)
        {
            return;
        }

        ApplySnapshot(snapshot);
        Status = getString("LiveSelectionUpdated");
    }

    public Task ApplyLiveInspectionOptionsAsync(LiveInspectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            options.Validate();
        }
        catch (Exception exception)
        {
            ReportError(exception);
            return Task.CompletedTask;
        }

        if (!IsTracking)
        {
            ApplyLiveOptions(options);
            return Task.CompletedTask;
        }

        if (liveInspection is null)
        {
            ReportError(new InvalidOperationException("Live inspection is not available."));
            return Task.CompletedTask;
        }

        var scopeChanged = options.Scope != SelectionScope;
        return RunAsync(async () =>
        {
            await liveInspection.ConfigureLiveInspectionAsync(options);
            ApplyLiveOptions(options);
            if (scopeChanged)
            {
                ClearSnapshot();
            }

            Status = getString("LiveSettingsUpdated");
        });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void RefreshWindows()
    {
        try
        {
            var previous = SelectedWindow?.Handle;
            Windows.Clear();
            foreach (var window in inspection.GetWindows())
            {
                Windows.Add(window);
            }

            SelectedWindow = Windows.FirstOrDefault(window => window.Handle == previous);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task InspectAsync() => CaptureWindowAsync(false);

    [RelayCommand(CanExecute = nameof(CanTest))]
    private Task TestAsync()
    {
        if (HasLiveSnapshot && ActiveContextId is Guid contextId && liveInspection is not null)
        {
            return RunAsync(async () =>
            {
                IsTracking = false;
                IsPaused = false;
                ClearScreenshot();
                var snapshot = await liveInspection.TestCurrentAsync(contextId);
                ApplySnapshot(snapshot);
                Status = snapshot.IsTruncated
                    ? getString("Truncated")
                    : string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        getString("CaptureComplete"),
                        Failures.Count);
            });
        }

        return CaptureWindowAsync(true);
    }

    private Task CaptureWindowAsync(bool runTests)
    {
        var target = SelectedWindow ?? throw new InvalidOperationException(getString("SelectTarget"));
        return RunAsync(async () =>
        {
            IsTracking = false;
            IsPaused = false;
            ClearSnapshot();
            var snapshot = await inspection.CaptureAsync(target.Handle, runTests);
            ApplySnapshot(snapshot);
            Status = snapshot.IsTruncated
                ? getString("Truncated")
                : runTests
                    ? string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        getString("CaptureComplete"),
                        Failures.Count)
                    : getString("InspectComplete");
        });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task OpenAsync() => RunAsync(async () =>
    {
        var path = await files.OpenAsync();
        if (path is not null)
        {
            await OpenFileCoreAsync(path);
        }
    });

    public Task OpenFileAsync(string path) => RunAsync(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await OpenFileCoreAsync(path);
    });

    private async Task OpenFileCoreAsync(string path)
    {
        IsTracking = false;
        IsPaused = false;
        ClearSnapshot();
        ApplySnapshot(await inspection.OpenAsync(path));
        Status = getString("Opened");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var path = await files.SaveAsync();
        if (path is null)
        {
            return;
        }

        await inspection.SaveAsync(path);
        Status = getString("Saved");
    });

    [RelayCommand(CanExecute = nameof(CanToggleTracking))]
    private Task ToggleTrackingAsync()
    {
        if (liveInspection is null)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            if (IsTracking)
            {
                await liveInspection.StopLiveInspectionAsync();
                IsTracking = false;
                IsPaused = false;
                Status = getString("TrackingStopped");
            }
            else
            {
                var options = LiveOptions;
                options.Validate();
                ClearSnapshot();
                IsTracking = true;
                try
                {
                    await liveInspection.StartLiveInspectionAsync(options);
                    Status = getString("TrackingStarted");
                }
                catch
                {
                    IsTracking = false;
                    throw;
                }
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private Task TogglePauseAsync()
    {
        if (liveInspection is null)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var paused = !IsPaused;
            await liveInspection.SetLiveInspectionPausedAsync(paused);
            IsPaused = paused;
            Status = getString(paused ? "TrackingPaused" : "TrackingResumed");
        });
    }

    [RelayCommand(CanExecute = nameof(CanToggleHighlight))]
    private Task ToggleHighlightAsync()
    {
        if (liveInspection is null)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            IsHighlightEnabled = !IsHighlightEnabled;
            if (!IsHighlightEnabled)
            {
                await liveInspection.ClearHighlightAsync();
            }
            else if (ActiveContextId is Guid contextId && SelectedElement is ElementSnapshot element)
            {
                await liveInspection.HighlightAsync(contextId, element.Id);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCaptureScreenshot))]
    private Task CaptureScreenshotAsync()
    {
        if (imaging is null || ActiveContextId is not Guid contextId || SelectedElement is not ElementSnapshot element)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var screenshot = await imaging.CaptureScreenshotAsync(contextId, element.Id)
                ?? throw new InvalidOperationException(getString("ScreenshotUnavailable"));
            Screenshot = screenshot;
            HasScreenshot = true;
            Status = getString("ScreenshotCaptured");
        });
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task NavigateAsync(TreeNavigationDirection direction)
    {
        if (liveInspection is null || ActiveContextId is not Guid contextId)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var snapshot = await liveInspection.NavigateAsync(contextId, direction)
                ?? throw new InvalidOperationException(getString("TreeNavigationUnavailable"));
            ApplySnapshot(snapshot);
            Status = getString("LiveSelectionUpdated");
        });
    }

    public void ReportError(Exception exception)
    {
        Trace.TraceError("{0}", exception.ToString());
        Error = exception.Message;
        HasError = true;
        Status = getString("OperationFailed");
    }

    private async Task RefreshSelectedElementAsync(ElementSnapshot element, Guid contextId, long version)
    {
        if (liveInspection is null)
        {
            return;
        }

        try
        {
            var details = await liveInspection.GetElementDetailsAsync(contextId, element.Id);
            if (details is null || version != Volatile.Read(ref selectionVersion) ||
                ActiveContextId != contextId || SelectedElement?.Id != element.Id)
            {
                return;
            }

            Properties.Clear();
            foreach (var property in details.Properties)
            {
                Properties.Add(property);
            }

            Patterns.Clear();
            foreach (var pattern in details.Patterns)
            {
                Patterns.Add(pattern);
            }

            if (IsHighlightEnabled && CurrentInspectionKind != InspectionKind.Loaded)
            {
                await liveInspection.HighlightAsync(contextId, element.Id);
            }
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref selectionVersion) &&
                ActiveContextId == contextId && SelectedElement?.Id == element.Id)
            {
                ReportError(exception);
            }
        }
    }

    private async Task ClearHighlightSafelyAsync()
    {
        try
        {
            await liveInspection!.ClearHighlightAsync();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
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
            ReportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSnapshot()
    {
        Interlocked.Increment(ref selectionVersion);
        HasSnapshot = false;
        Roots.Clear();
        Properties.Clear();
        Patterns.Clear();
        Failures.Clear();
        SelectedElement = null;
        ActiveContextId = null;
        CurrentInspectionKind = InspectionKind.Live;
        ClearScreenshot();
    }

    private void ApplySnapshot(InspectionSnapshot snapshot)
    {
        suppressSelectionRefresh = true;
        try
        {
            SelectedElement = null;
        }
        finally
        {
            suppressSelectionRefresh = false;
        }

        Roots.Clear();
        Properties.Clear();
        Patterns.Clear();
        Failures.Clear();
        ClearScreenshot();
        ActiveContextId = snapshot.ContextId;
        CurrentInspectionKind = snapshot.Kind;
        Roots.Add(snapshot.Root);
        SelectedElement = snapshot.Root;
        foreach (var failure in snapshot.Failures)
        {
            Failures.Add(failure);
        }

        HasSnapshot = !snapshot.IsTruncated;
        UpdateLiveCommandStates();
    }

    private void ApplyLiveOptions(LiveInspectionOptions options)
    {
        SelectionScope = options.Scope;
        TrackMouse = (options.Methods & LiveSelectionMethods.Mouse) != 0;
        TrackKeyboardFocus = (options.Methods & LiveSelectionMethods.KeyboardFocus) != 0;
        MouseSelectionDelayMilliseconds = options.MouseSelectionDelayMilliseconds;
    }

    private void ClearScreenshot()
    {
        Screenshot = null;
        HasScreenshot = false;
    }

    private void UpdateLiveCommandStates()
    {
        TestCommand.NotifyCanExecuteChanged();
        ToggleHighlightCommand.NotifyCanExecuteChanged();
        CaptureScreenshotCommand.NotifyCanExecuteChanged();
        NavigateCommand.NotifyCanExecuteChanged();
    }
}
