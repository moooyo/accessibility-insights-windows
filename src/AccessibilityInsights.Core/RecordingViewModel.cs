// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace AccessibilityInsights.Core;

public partial class RecordingViewModel : ObservableObject, IDisposable
{
    private readonly IRecordingService recording;
    private readonly IRecordingFilePicker files;
    private readonly WorkbenchViewModel workbench;
    private readonly Func<string, string> getString;
    private readonly IUiDispatcher dispatcher;
    private int? configurationElementId;
    private bool disposed;

    public RecordingViewModel(
        IRecordingService recording,
        IRecordingFilePicker files,
        WorkbenchViewModel workbench,
        Func<string, string> getString,
        IUiDispatcher dispatcher)
    {
        this.recording = recording;
        this.files = files;
        this.workbench = workbench;
        this.getString = getString;
        this.dispatcher = dispatcher;

        var state = recording.State;
        IsEventRecording = state.IsEventRecording;
        IsTabStopRecording = state.IsTabStopRecording;
        UpdateTarget();

        recording.EventRecorded += OnEventRecorded;
        recording.TabStopRecorded += OnTabStopRecorded;
        recording.RecordingStateChanged += OnRecordingStateChanged;
        recording.RecordingFaulted += OnRecordingFaulted;
        workbench.PropertyChanged += OnWorkbenchPropertyChanged;
        Events.CollectionChanged += OnEventsCollectionChanged;
        TabStops.CollectionChanged += OnTabStopsCollectionChanged;
    }

    public ObservableCollection<RecordingOptionViewModel> EventOptions { get; } = [];
    public ObservableCollection<RecordingOptionViewModel> PropertyOptions { get; } = [];
    public ObservableCollection<RecordingOptionViewModel> VisibleEventOptions { get; } = [];
    public ObservableCollection<RecordingOptionViewModel> VisiblePropertyOptions { get; } = [];
    public ObservableCollection<RecordedEvent> Events { get; } = [];
    public ObservableCollection<RecordedProperty> EventDetails { get; } = [];
    public ObservableCollection<TabStopRecord> TabStops { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEventRecording { get; set; }

    [ObservableProperty]
    public partial bool IsTabStopRecording { get; set; }

    [ObservableProperty]
    public partial bool HasTarget { get; set; }

    [ObservableProperty]
    public partial bool HasEvents { get; set; }

    [ObservableProperty]
    public partial bool HasTabStops { get; set; }

    [ObservableProperty]
    public partial bool IsLoadedEventFile { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TargetName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EventSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PropertySearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowSelectedOptionsOnly { get; set; }

    [ObservableProperty]
    public partial RecordingScope SelectedScope { get; set; } = RecordingScope.Subtree;

    [ObservableProperty]
    public partial RecordedEvent? SelectedEvent { get; set; }

    [ObservableProperty]
    public partial TabStopRecord? SelectedTabStop { get; set; }

    [ObservableProperty]
    public partial int DroppedEventCount { get; set; }

    [ObservableProperty]
    public partial int DroppedTabStopCount { get; set; }

    [ObservableProperty]
    public partial bool HasDroppedEvents { get; set; }

    [ObservableProperty]
    public partial bool HasDroppedTabStops { get; set; }

    [ObservableProperty]
    public partial string EventBufferNotice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TabStopBufferNotice { get; set; } = string.Empty;

    public Task OpenFileAsync(string path) => RunAsync(() => OpenFileCoreAsync(path));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        recording.EventRecorded -= OnEventRecorded;
        recording.TabStopRecorded -= OnTabStopRecorded;
        recording.RecordingStateChanged -= OnRecordingStateChanged;
        recording.RecordingFaulted -= OnRecordingFaulted;
        workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
        Events.CollectionChanged -= OnEventsCollectionChanged;
        TabStops.CollectionChanged -= OnTabStopsCollectionChanged;
        foreach (var option in EventOptions.Concat(PropertyOptions))
        {
            option.PropertyChanged -= OnOptionPropertyChanged;
        }
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();
    partial void OnIsEventRecordingChanged(bool value) => NotifyCommandStates();
    partial void OnIsTabStopRecordingChanged(bool value) => NotifyCommandStates();
    partial void OnHasTargetChanged(bool value) => NotifyCommandStates();
    partial void OnHasEventsChanged(bool value) => NotifyCommandStates();
    partial void OnHasTabStopsChanged(bool value) => NotifyCommandStates();
    partial void OnIsLoadedEventFileChanged(bool value) => NotifyCommandStates();
    partial void OnEventSearchTextChanged(string value) => ApplyEventFilter();
    partial void OnPropertySearchTextChanged(string value) => ApplyPropertyFilter();

    partial void OnDroppedEventCountChanged(int value)
    {
        HasDroppedEvents = value > 0;
        EventBufferNotice = value > 0 ? Format("EventBufferLimitReached", value) : string.Empty;
    }

    partial void OnDroppedTabStopCountChanged(int value)
    {
        HasDroppedTabStops = value > 0;
        TabStopBufferNotice = value > 0 ? Format("TabStopBufferLimitReached", value) : string.Empty;
    }

    partial void OnShowSelectedOptionsOnlyChanged(bool value)
    {
        ApplyEventFilter();
        ApplyPropertyFilter();
    }

    partial void OnSelectedEventChanged(RecordedEvent? value)
    {
        EventDetails.Clear();
        if (value is not null)
        {
            foreach (var property in value.Properties)
            {
                EventDetails.Add(property);
            }

            if (value.Element is not null)
            {
                foreach (var property in value.Element.Properties)
                {
                    EventDetails.Add(property);
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshConfiguration))]
    private Task RefreshConfigurationAsync() => RunAsync(async () =>
    {
        var selected = workbench.SelectedElement;
        if (selected is null)
        {
            await OnUiAsync(() => Status = getString("RecordingSelectElement")).ConfigureAwait(false);
            return;
        }

        var catalog = await recording.GetEventRecordingCatalogAsync(selected.Id).ConfigureAwait(false);
        await OnUiAsync(() => ApplyCatalog(selected.Id, catalog)).ConfigureAwait(false);
    });

    [RelayCommand(CanExecute = nameof(CanStartEvents))]
    private Task StartEventsAsync() => RunAsync(async () =>
    {
        var selected = workbench.SelectedElement
            ?? throw new InvalidOperationException(getString("RecordingSelectElement"));
        var eventIds = EventOptions.Where(option => option.IsSelected).Select(option => option.Id).ToArray();
        var propertyIds = PropertyOptions.Where(option => option.IsSelected).Select(option => option.Id).ToArray();
        var scope = SelectedScope;

        await OnUiAsync(() =>
        {
            Events.Clear();
            SelectedEvent = null;
            DroppedEventCount = 0;
            IsLoadedEventFile = false;
        }).ConfigureAwait(false);
        await recording.StartEventRecordingAsync(
            selected.Id, scope, eventIds, propertyIds).ConfigureAwait(false);
        await OnUiAsync(() => Status = getString("EventRecordingStarted")).ConfigureAwait(false);
    });

    [RelayCommand(CanExecute = nameof(CanStopEvents))]
    private Task StopEventsAsync() => RunAsync(async () =>
    {
        await recording.StopEventRecordingAsync().ConfigureAwait(false);
        await OnUiAsync(() => Status = Format("EventRecordingStopped", Events.Count)).ConfigureAwait(false);
    });

    [RelayCommand(CanExecute = nameof(CanClearEvents))]
    private Task ClearEventsAsync() => RunAsync(async () =>
    {
        await recording.ClearEventsAsync().ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            Events.Clear();
            SelectedEvent = null;
            DroppedEventCount = 0;
            IsLoadedEventFile = false;
            Status = getString("EventRecordsCleared");
        }).ConfigureAwait(false);
    });

    [RelayCommand(CanExecute = nameof(CanOpenEvents))]
    private Task OpenEventsAsync() => RunAsync(async () =>
    {
        var path = await files.OpenEventFileAsync().ConfigureAwait(false);
        if (path is not null)
        {
            await OpenFileCoreAsync(path).ConfigureAwait(false);
        }
    });

    [RelayCommand(CanExecute = nameof(CanSaveEvents))]
    private Task SaveEventsAsync() => RunAsync(async () =>
    {
        var path = await files.SaveEventFileAsync().ConfigureAwait(false);
        if (path is null)
        {
            return;
        }

        await recording.ExportEventsAsync(path).ConfigureAwait(false);
        await OnUiAsync(() => Status = getString("EventFileSaved")).ConfigureAwait(false);
    });

    [RelayCommand]
    private void SelectAllVisibleEvents()
    {
        foreach (var option in VisibleEventOptions)
        {
            option.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearEventSelection()
    {
        foreach (var option in EventOptions)
        {
            option.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAllVisibleProperties()
    {
        foreach (var option in VisiblePropertyOptions)
        {
            option.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearPropertySelection()
    {
        foreach (var option in PropertyOptions)
        {
            option.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartTabStops))]
    private Task StartTabStopsAsync() => RunAsync(async () =>
    {
        var selected = workbench.SelectedElement
            ?? throw new InvalidOperationException(getString("RecordingSelectElement"));
        await OnUiAsync(() =>
        {
            TabStops.Clear();
            SelectedTabStop = null;
            DroppedTabStopCount = 0;
        }).ConfigureAwait(false);
        await recording.StartTabStopRecordingAsync(selected.Id).ConfigureAwait(false);
        await OnUiAsync(() => Status = getString("TabStopRecordingStarted")).ConfigureAwait(false);
    });

    [RelayCommand(CanExecute = nameof(CanStopTabStops))]
    private Task StopTabStopsAsync() => RunAsync(async () =>
    {
        await recording.StopTabStopRecordingAsync().ConfigureAwait(false);
        await OnUiAsync(() => Status = Format("TabStopRecordingStopped", TabStops.Count)).ConfigureAwait(false);
    });

    [RelayCommand(CanExecute = nameof(CanClearTabStops))]
    private Task ClearTabStopsAsync() => RunAsync(async () =>
    {
        await recording.ClearTabStopsAsync().ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            TabStops.Clear();
            SelectedTabStop = null;
            DroppedTabStopCount = 0;
            Status = getString("TabStopsCleared");
        }).ConfigureAwait(false);
    });

    private bool CanRefreshConfiguration() => !IsBusy && HasTarget;
    private bool CanStartEvents() =>
        !IsBusy && HasTarget && !IsLoadedEventFile && !IsEventRecording && !IsTabStopRecording &&
        EventOptions.Concat(PropertyOptions).Any(option => option.IsSelected);
    private bool CanStopEvents() => !IsBusy && IsEventRecording;
    private bool CanClearEvents() => !IsBusy && HasEvents;
    private bool CanOpenEvents() => !IsBusy;
    private bool CanSaveEvents() => !IsBusy && HasEvents;
    private bool CanStartTabStops() =>
        !IsBusy && HasTarget && !IsEventRecording && !IsTabStopRecording;
    private bool CanStopTabStops() => !IsBusy && IsTabStopRecording;
    private bool CanClearTabStops() => !IsBusy && HasTabStops;

    private async Task OpenFileCoreAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var imported = await recording.ImportEventsAsync(path).ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            Events.Clear();
            foreach (var item in imported.Items)
            {
                Events.Add(item);
            }

            SelectedEvent = Events.FirstOrDefault();
            DroppedEventCount = imported.DroppedCount;
            IsLoadedEventFile = true;
            Status = Format("EventFileOpened", Events.Count);
        }).ConfigureAwait(false);
    }

    private async Task RunAsync(Func<Task> operation)
    {
        await OnUiAsync(() =>
        {
            IsBusy = true;
            HasError = false;
            Error = string.Empty;
        }).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await OnUiAsync(() => ReportError(exception)).ConfigureAwait(false);
        }
        finally
        {
            await OnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private void ApplyCatalog(int elementId, EventRecordingCatalog catalog)
    {
        var preserveSelection = configurationElementId == elementId;
        var selectedEvents = preserveSelection
            ? EventOptions.Where(option => option.IsSelected).Select(option => option.Id).ToHashSet()
            : new HashSet<int>();
        var selectedProperties = preserveSelection
            ? PropertyOptions.Where(option => option.IsSelected).Select(option => option.Id).ToHashSet()
            : new HashSet<int>();

        DetachCurrentOptions();
        EventOptions.Clear();
        PropertyOptions.Clear();

        foreach (var option in catalog.Events)
        {
            var item = new RecordingOptionViewModel(
                option,
                preserveSelection ? selectedEvents.Contains(option.Id) : option.IsRecommended,
                "RecordingEvent");
            item.PropertyChanged += OnOptionPropertyChanged;
            EventOptions.Add(item);
        }

        foreach (var option in catalog.Properties)
        {
            var item = new RecordingOptionViewModel(
                option,
                preserveSelection && selectedProperties.Contains(option.Id),
                "RecordingProperty");
            item.PropertyChanged += OnOptionPropertyChanged;
            PropertyOptions.Add(item);
        }

        configurationElementId = elementId;
        TargetName = string.IsNullOrWhiteSpace(catalog.TargetName)
            ? workbench.SelectedElement?.DisplayName ?? string.Empty
            : catalog.TargetName;
        ApplyEventFilter();
        ApplyPropertyFilter();
        Status = Format("RecordingConfigurationLoaded", TargetName);
        NotifyCommandStates();
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RecordingOptionViewModel.IsSelected))
        {
            if (ShowSelectedOptionsOnly)
            {
                ApplyEventFilter();
                ApplyPropertyFilter();
            }

            NotifyCommandStates();
        }
    }

    private void ApplyEventFilter() =>
        ApplyFilter(EventOptions, VisibleEventOptions, EventSearchText);

    private void ApplyPropertyFilter() =>
        ApplyFilter(PropertyOptions, VisiblePropertyOptions, PropertySearchText);

    private void ApplyFilter(
        IEnumerable<RecordingOptionViewModel> source,
        ObservableCollection<RecordingOptionViewModel> destination,
        string searchText)
    {
        destination.Clear();
        foreach (var option in source.Where(option =>
                     (!ShowSelectedOptionsOnly || option.IsSelected) &&
                     (string.IsNullOrWhiteSpace(searchText) ||
                      option.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                      option.Id.ToString(CultureInfo.CurrentCulture)
                          .Contains(searchText, StringComparison.CurrentCultureIgnoreCase))))
        {
            destination.Add(option);
        }
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkbenchViewModel.SelectedElement))
        {
            Dispatch(() =>
            {
                configurationElementId = null;
                DetachCurrentOptions();
                EventOptions.Clear();
                PropertyOptions.Clear();
                VisibleEventOptions.Clear();
                VisiblePropertyOptions.Clear();
                TabStops.Clear();
                SelectedTabStop = null;
                DroppedTabStopCount = 0;
                UpdateTarget();
                NotifyCommandStates();
            });
        }
    }

    private void UpdateTarget()
    {
        var selected = workbench.SelectedElement;
        HasTarget = selected is not null;
        TargetName = selected?.DisplayName ?? getString("RecordingNoTarget");
        Status = HasTarget ? getString("RecordingReady") : getString("RecordingSelectElement");
    }

    private void OnEventRecorded(object? sender, RecordedEventEventArgs args) => Dispatch(() =>
    {
        if (Events.Count == RecordingBufferLimits.Events)
        {
            Events.RemoveAt(0);
            DroppedEventCount++;
        }

        Events.Add(args.Item);
        SelectedEvent = args.Item;
    });

    private void OnTabStopRecorded(object? sender, TabStopRecordedEventArgs args) => Dispatch(() =>
    {
        if (args.IsNew)
        {
            if (TabStops.Count == RecordingBufferLimits.TabStops)
            {
                TabStops.RemoveAt(0);
                DroppedTabStopCount++;
            }

            TabStops.Add(args.Item);
        }

        SelectedTabStop = null;
        SelectedTabStop = TabStops.FirstOrDefault(item => item.Sequence == args.Item.Sequence);
    });

    private void OnRecordingStateChanged(object? sender, RecordingStateChangedEventArgs args) => Dispatch(() =>
    {
        var eventStopped = IsEventRecording && !args.State.IsEventRecording;
        var tabStopsStopped = IsTabStopRecording && !args.State.IsTabStopRecording;
        IsEventRecording = args.State.IsEventRecording;
        IsTabStopRecording = args.State.IsTabStopRecording;
        if (eventStopped)
        {
            Status = Format("EventRecordingStopped", Events.Count);
        }
        else if (tabStopsStopped)
        {
            Status = Format("TabStopRecordingStopped", TabStops.Count);
        }
    });

    private void OnRecordingFaulted(object? sender, RecordingFaultedEventArgs args) =>
        Dispatch(() => ReportError(args.Exception));

    private void OnEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        HasEvents = Events.Count > 0;
        NotifyCommandStates();
    }

    private void OnTabStopsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        HasTabStops = TabStops.Count > 0;
        NotifyCommandStates();
    }

    private void ReportError(Exception exception)
    {
        Trace.TraceError("{0}", exception);
        Error = exception.Message;
        HasError = true;
        Status = getString("RecordingOperationFailed");
    }

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, getString(key), values);

    private void Dispatch(Action callback)
    {
        if (dispatcher.HasThreadAccess)
        {
            callback();
        }
        else if (!dispatcher.TryEnqueue(callback))
        {
            Trace.TraceWarning("The recording UI dispatcher is shutting down; an update was not queued.");
        }
    }

    private Task OnUiAsync(Action callback) => dispatcher.EnqueueAsync(callback);

    private void DetachCurrentOptions()
    {
        foreach (var option in EventOptions.Concat(PropertyOptions))
        {
            option.PropertyChanged -= OnOptionPropertyChanged;
        }
    }

    private void NotifyCommandStates()
    {
        RefreshConfigurationCommand.NotifyCanExecuteChanged();
        StartEventsCommand.NotifyCanExecuteChanged();
        StopEventsCommand.NotifyCanExecuteChanged();
        ClearEventsCommand.NotifyCanExecuteChanged();
        OpenEventsCommand.NotifyCanExecuteChanged();
        SaveEventsCommand.NotifyCanExecuteChanged();
        StartTabStopsCommand.NotifyCanExecuteChanged();
        StopTabStopsCommand.NotifyCanExecuteChanged();
        ClearTabStopsCommand.NotifyCanExecuteChanged();
    }
}
