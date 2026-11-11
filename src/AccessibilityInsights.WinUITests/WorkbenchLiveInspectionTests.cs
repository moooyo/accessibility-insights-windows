// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class WorkbenchLiveInspectionTests
{
    [TestMethod]
    public async Task OpenFileAsyncUsesSharedBusyErrorAndSnapshotFlow()
    {
        var completion = new TaskCompletionSource<InspectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LiveFakeInspectionService
        {
            Open = path =>
            {
                Assert.AreEqual("startup.a11ytest", path);
                return completion.Task;
            },
        };
        var model = CreateModel(service);

        var opening = model.OpenFileAsync("startup.a11ytest");

        Assert.IsTrue(model.IsBusy);
        completion.SetResult(CreateSnapshot(InspectionKind.Loaded));
        await opening;
        Assert.IsFalse(model.IsBusy);
        Assert.AreEqual(InspectionKind.Loaded, model.CurrentInspectionKind);
        Assert.AreEqual("Root", model.Roots.Single().Name);
        Assert.AreEqual("Opened", model.Status);
    }

    [TestMethod]
    public async Task OpenFileAsyncFailureUsesSharedErrorState()
    {
        var service = new LiveFakeInspectionService
        {
            Open = _ => Task.FromException<InspectionSnapshot>(
                new InvalidDataException("Invalid snapshot")),
        };
        var model = CreateModel(service);

        await model.OpenFileAsync("broken.a11ytest");

        Assert.IsFalse(model.IsBusy);
        Assert.IsTrue(model.HasError);
        Assert.AreEqual("Invalid snapshot", model.Error);
        Assert.AreEqual("OperationFailed", model.Status);
        Assert.AreEqual(0, model.Roots.Count);
    }

    [TestMethod]
    public async Task SelectedElementDetailsIgnoreStaleCompletion()
    {
        var first = new TaskCompletionSource<ElementDetails?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ElementDetails?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LiveFakeInspectionService
        {
            Details = (_, elementId) => elementId switch
            {
                2 => first.Task,
                3 => second.Task,
                _ => Task.FromResult<ElementDetails?>(null),
            },
        };
        var model = CreateModel(service);
        var snapshot = CreateSnapshot(
            InspectionKind.Live,
            [
                CreateElement(2, "First", "Cached first"),
                CreateElement(3, "Second", "Cached second"),
            ]);
        model.IsTracking = true;
        model.ApplyLiveSnapshot(snapshot);

        var firstSelection = model.SelectElementAsync(snapshot.Root.Children[0]);
        var secondSelection = model.SelectElementAsync(snapshot.Root.Children[1]);
        second.SetResult(CreateDetails(snapshot.ContextId, 3, "Second live"));
        await secondSelection;
        first.SetResult(CreateDetails(snapshot.ContextId, 2, "First live"));
        await firstSelection;

        Assert.AreEqual(3, model.SelectedElement!.Id);
        Assert.AreEqual("Second live", model.Properties.Single().Value);
        Assert.AreEqual(3, service.LastHighlightedElement);
    }

    [TestMethod]
    public void LiveSnapshotReplacesPriorTreeInsteadOfAccumulatingRoots()
    {
        var model = CreateModel(new LiveFakeInspectionService());
        model.IsTracking = true;
        model.ApplyLiveSnapshot(CreateSnapshot(InspectionKind.Live));
        model.ApplyLiveSnapshot(CreateSnapshot(
            InspectionKind.Live,
            [CreateElement(4, "Child", "Value")]));

        Assert.AreEqual(1, model.Roots.Count);
        Assert.AreEqual(1, model.Roots.Single().Children.Count);
    }

    private static WorkbenchViewModel CreateModel(IInspectionService service) =>
        new(service, new CancelledFilePicker(), key => key);

    private static InspectionSnapshot CreateSnapshot(
        InspectionKind kind,
        IReadOnlyList<ElementSnapshot>? children = null)
    {
        var context = Guid.NewGuid();
        return new InspectionSnapshot(context, new ElementSnapshot
        {
            Id = 1,
            Name = "Root",
            ControlType = "Window",
            Properties = [new("Name", "Root")],
            Children = children ?? [],
            Failures = [],
        }, false)
        {
            Kind = kind,
        };
    }

    private static ElementSnapshot CreateElement(int id, string name, string value) => new()
    {
        Id = id,
        Name = name,
        ControlType = "Button",
        Properties = [new("Name", value)],
        Children = [],
        Failures = [],
    };

    private static ElementDetails CreateDetails(Guid context, int id, string value) => new(
        context,
        id,
        [new ElementProperty("Name", value)],
        [],
        default,
        10,
        true,
        true);

    private sealed class CancelledFilePicker : ISnapshotFilePicker
    {
        public Task<string?> OpenAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync() => Task.FromResult<string?>(null);
    }

    private sealed class LiveFakeInspectionService :
        IInspectionService,
        ILiveInspectionService,
        IInspectionImagingService
    {
        public Func<string, Task<InspectionSnapshot>> Open { get; set; } =
            _ => Task.FromResult(CreateSnapshot(InspectionKind.Loaded));
        public Func<Guid, int, Task<ElementDetails?>> Details { get; set; } =
            (_, _) => Task.FromResult<ElementDetails?>(null);
        public int LastHighlightedElement { get; private set; }

        public event EventHandler<LiveSelectionChangedEventArgs>? LiveSelectionChanged;
        public event EventHandler<LiveInspectionErrorEventArgs>? LiveInspectionError;

        public IReadOnlyList<TargetWindow> GetWindows() => [];
        public Task<InspectionSnapshot> CaptureAsync(nint window, bool runTests) =>
            Task.FromResult(CreateSnapshot(runTests ? InspectionKind.Test : InspectionKind.Live));
        public Task<InspectionSnapshot> OpenAsync(string path) => Open(path);
        public Task SaveAsync(string path) => Task.CompletedTask;
        public Task StartLiveInspectionAsync(LiveInspectionOptions options) => Task.CompletedTask;
        public Task StopLiveInspectionAsync() => Task.CompletedTask;
        public Task SetLiveInspectionPausedAsync(bool paused) => Task.CompletedTask;
        public Task ConfigureLiveInspectionAsync(LiveInspectionOptions options) => Task.CompletedTask;
        public Task<InspectionSnapshot?> NavigateAsync(Guid contextId, TreeNavigationDirection direction) =>
            Task.FromResult<InspectionSnapshot?>(null);
        public Task<ElementDetails?> GetElementDetailsAsync(Guid contextId, int elementId) =>
            Details(contextId, elementId);
        public Task<bool> HighlightAsync(Guid contextId, int elementId)
        {
            LastHighlightedElement = elementId;
            return Task.FromResult(true);
        }

        public Task ClearHighlightAsync() => Task.CompletedTask;
        public Task<InspectionSnapshot> TestCurrentAsync(Guid contextId) =>
            Task.FromResult(CreateSnapshot(InspectionKind.Test));
        public Task<InspectionScreenshot?> CaptureScreenshotAsync(Guid contextId, int selectedElementId) =>
            Task.FromResult<InspectionScreenshot?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseSelection(InspectionSnapshot snapshot) =>
            LiveSelectionChanged?.Invoke(this, new LiveSelectionChangedEventArgs(snapshot));

        public void RaiseError(Exception exception) =>
            LiveInspectionError?.Invoke(this, new LiveInspectionErrorEventArgs(exception));
    }
}
