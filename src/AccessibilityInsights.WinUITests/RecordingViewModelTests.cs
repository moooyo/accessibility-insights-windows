// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class RecordingViewModelTests
{
    [TestMethod]
    public async Task OpenFileAsyncLoadsStartupEventFileThroughSharedErrorHandling()
    {
        var service = new FakeRecordingService
        {
            Imported = new RecordingBufferSnapshot<RecordedEvent>(
                [CreateEvent()], DroppedCount: 0),
        };
        using var model = CreateModel(service);

        await model.OpenFileAsync("startup.a11yevent");

        Assert.AreEqual("startup.a11yevent", service.ImportedPath);
        Assert.AreEqual(1, model.Events.Count);
        Assert.IsTrue(model.IsLoadedEventFile);
        Assert.AreEqual("Opened 1 events.", model.Status);
        Assert.IsFalse(model.HasError);
    }

    [TestMethod]
    public async Task OpenFileAsyncSurfacesImportFailureWithoutThrowing()
    {
        var service = new FakeRecordingService
        {
            ImportException = new InvalidDataException("Unsupported event file"),
        };
        using var model = CreateModel(service);

        await model.OpenFileAsync("bad.a11yevent");

        Assert.IsTrue(model.HasError);
        Assert.AreEqual("Unsupported event file", model.Error);
        Assert.AreEqual("Recording operation failed.", model.Status);
        Assert.IsFalse(model.IsBusy);
    }

    [TestMethod]
    public async Task RefreshConfigurationSelectsFocusChangedAndExposesStableAutomationId()
    {
        var service = new FakeRecordingService
        {
            Catalog = new EventRecordingCatalog(
                "button 'Save'",
                [new RecordingOption(20005, "AutomationFocusChanged", IsRecommended: true, IsAvailableOnTarget: true)],
                []),
        };
        using var model = CreateModel(service);

        await model.RefreshConfigurationCommand.ExecuteAsync(null);

        var focusChanged = model.EventOptions.Single();
        Assert.AreEqual(20005, focusChanged.Id);
        Assert.AreEqual("RecordingEvent_20005", focusChanged.AutomationId);
        Assert.IsTrue(focusChanged.IsSelected);
    }

    private static RecordingViewModel CreateModel(FakeRecordingService recording)
    {
        var workbench = new WorkbenchViewModel(
            new FakeInspectionService(),
            new CancelledSnapshotPicker(),
            key => key);
        workbench.SelectedElement = new ElementSnapshot
        {
            Id = 7,
            Name = "Save",
            ControlType = "button",
            Properties = [],
            Children = [],
            Failures = [],
        };
        return new RecordingViewModel(
            recording,
            new CancelledRecordingFilePicker(),
            workbench,
            key => key switch
            {
                "EventFileOpened" => "Opened {0} events.",
                "RecordingOperationFailed" => "Recording operation failed.",
                "RecordingReady" => "Ready.",
                "RecordingSelectElement" => "Select an element.",
                "RecordingNoTarget" => "No target.",
                _ => key,
            },
            new ImmediateDispatcher());
    }

    private static RecordedEvent CreateEvent() => new(
        1,
        20005,
        "AutomationFocusChanged",
        "09:58:38.455",
        new RecordedElement(7, "Save", "button", "SaveButton", 100, "[7,100,200]", "button 'Save'", []),
        []);

    private sealed class FakeRecordingService : IRecordingService
    {
        public event EventHandler<RecordedEventEventArgs>? EventRecorded { add { } remove { } }
        public event EventHandler<TabStopRecordedEventArgs>? TabStopRecorded { add { } remove { } }
        public event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged { add { } remove { } }
        public event EventHandler<RecordingFaultedEventArgs>? RecordingFaulted { add { } remove { } }

        public RecordingState State { get; set; } = new(false, false);
        public RecordingBufferSnapshot<RecordedEvent> Imported { get; set; } = new([], 0);
        public EventRecordingCatalog Catalog { get; set; } = new("Target", [], []);
        public Exception? ImportException { get; set; }
        public string? ImportedPath { get; private set; }

        public Task<EventRecordingCatalog> GetEventRecordingCatalogAsync(int elementId) =>
            Task.FromResult(Catalog);

        public Task StartEventRecordingAsync(
            int elementId,
            RecordingScope scope,
            IReadOnlyCollection<int> eventIds,
            IReadOnlyCollection<int> propertyIds) => Task.CompletedTask;

        public Task StopEventRecordingAsync() => Task.CompletedTask;
        public Task ClearEventsAsync() => Task.CompletedTask;
        public Task<RecordingBufferSnapshot<RecordedEvent>> GetEventsAsync() => Task.FromResult(Imported);

        public Task<RecordingBufferSnapshot<RecordedEvent>> ImportEventsAsync(string path)
        {
            ImportedPath = path;
            return ImportException is null
                ? Task.FromResult(Imported)
                : Task.FromException<RecordingBufferSnapshot<RecordedEvent>>(ImportException);
        }

        public Task ExportEventsAsync(string path) => Task.CompletedTask;
        public Task StartTabStopRecordingAsync(int elementId) => Task.CompletedTask;
        public Task StopTabStopRecordingAsync() => Task.CompletedTask;
        public Task ClearTabStopsAsync() => Task.CompletedTask;
        public Task<RecordingBufferSnapshot<TabStopRecord>> GetTabStopsAsync() =>
            Task.FromResult(new RecordingBufferSnapshot<TabStopRecord>([], 0));
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public bool TryEnqueue(Action callback)
        {
            callback();
            return true;
        }

        public Task EnqueueAsync(Action callback)
        {
            callback();
            return Task.CompletedTask;
        }
    }

    private sealed class CancelledRecordingFilePicker : IRecordingFilePicker
    {
        public Task<string?> OpenEventFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveEventFileAsync() => Task.FromResult<string?>(null);
    }

    private sealed class CancelledSnapshotPicker : ISnapshotFilePicker
    {
        public Task<string?> OpenAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync() => Task.FromResult<string?>(null);
    }

    private sealed class FakeInspectionService : IInspectionService
    {
        public IReadOnlyList<TargetWindow> GetWindows() => [];
        public Task<InspectionSnapshot> CaptureAsync(nint window, bool runTests) =>
            Task.FromException<InspectionSnapshot>(new NotSupportedException());
        public Task<InspectionSnapshot> OpenAsync(string path) =>
            Task.FromException<InspectionSnapshot>(new NotSupportedException());
        public Task SaveAsync(string path) => Task.FromException(new NotSupportedException());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
