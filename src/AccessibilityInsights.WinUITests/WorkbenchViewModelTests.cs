// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class WorkbenchViewModelTests
{
    [TestMethod]
    public async Task CaptureDisablesCommandsAndPublishesTreePropertiesAndFailures()
    {
        var completion = new TaskCompletionSource<InspectionSnapshot>();
        var service = new FakeInspectionService { Capture = () => completion.Task };
        var model = CreateModel(service);
        Assert.IsFalse(model.InspectCommand.CanExecute(null));
        model.SelectedWindow = new TargetWindow(10, 123, "Target");
        var capture = model.TestCommand.ExecuteAsync(null);
        Assert.IsTrue(model.IsBusy);
        Assert.IsFalse(model.InspectCommand.CanExecute(null));
        Assert.IsFalse(model.OpenCommand.CanExecute(null));

        var snapshot = CreateSnapshot();
        completion.SetResult(snapshot);
        await capture;

        Assert.IsFalse(model.IsBusy);
        Assert.AreSame(snapshot.Root, model.Roots.Single());
        Assert.AreEqual("Name", model.Properties.Single().Name);
        Assert.AreEqual("Failure", model.Failures.Single().Description);
        Assert.IsTrue(model.SaveCommand.CanExecute(null));
        Assert.IsTrue(service.RunTests);
    }

    [TestMethod]
    public async Task FailedRecaptureClearsStaleResultsAndSurfacesFailure()
    {
        var service = new FakeInspectionService();
        var model = CreateModel(service);
        model.SelectedWindow = new TargetWindow(10, 123, "Target");
        await model.TestCommand.ExecuteAsync(null);
        service.Capture = () => Task.FromException<InspectionSnapshot>(new InvalidOperationException("Window closed"));
        await model.TestCommand.ExecuteAsync(null);
        Assert.IsTrue(model.HasError);
        Assert.AreEqual("Window closed", model.Error);
        Assert.IsFalse(model.IsBusy);
        Assert.IsFalse(model.SaveCommand.CanExecute(null));
        Assert.AreEqual(0, model.Roots.Count);
        Assert.AreEqual(0, model.Failures.Count);
    }

    [TestMethod]
    public async Task TruncatedCaptureCannotBeSaved()
    {
        var service = new FakeInspectionService
        {
            Capture = () => Task.FromResult(CreateSnapshot() with { IsTruncated = true }),
        };
        var model = CreateModel(service);
        model.SelectedWindow = new TargetWindow(10, 123, "Target");
        await model.InspectCommand.ExecuteAsync(null);
        Assert.IsFalse(model.SaveCommand.CanExecute(null));
        Assert.AreEqual("Truncated", model.Status);
    }

    [TestMethod]
    public async Task CancelledOpenPreservesExistingSnapshot()
    {
        var model = CreateModel(new FakeInspectionService());
        model.SelectedWindow = new TargetWindow(10, 123, "Target");
        await model.InspectCommand.ExecuteAsync(null);
        var root = model.Roots.Single();
        await model.OpenCommand.ExecuteAsync(null);
        Assert.AreSame(root, model.Roots.Single());
        Assert.IsTrue(model.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    public void RefreshPreservesSelectedWindowAndClearsMissingSelection()
    {
        var service = new FakeInspectionService { Windows = [new TargetWindow(10, 123, "Target")] };
        var model = CreateModel(service);
        model.SelectedWindow = new TargetWindow(10, 123, "Old title");
        model.RefreshWindowsCommand.Execute(null);
        Assert.AreEqual("Target", model.SelectedWindow!.Title);
        service.Windows = [];
        model.RefreshWindowsCommand.Execute(null);
        Assert.IsNull(model.SelectedWindow);
        Assert.IsFalse(model.TestCommand.CanExecute(null));
    }

    private static WorkbenchViewModel CreateModel(IInspectionService service) =>
        new(service, new CancelledFilePicker(), key => key == "CaptureComplete" ? "{0} failed checks" : key);

    private static InspectionSnapshot CreateSnapshot() => new(Guid.NewGuid(), new ElementSnapshot
    {
        Id = 1,
        Name = "Root",
        ControlType = "Window",
        Properties = [new("Name", "Root")],
        Children = [],
        Failures = [new(1, "Root", "Failure", "Details", "https://example.com")],
    }, false);

    private sealed class CancelledFilePicker : ISnapshotFilePicker
    {
        public Task<string?> OpenAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync() => Task.FromResult<string?>(null);
    }

    private sealed class FakeInspectionService : IInspectionService
    {
        public Func<Task<InspectionSnapshot>> Capture { get; set; } = () => Task.FromResult(CreateSnapshot());
        public IReadOnlyList<TargetWindow> Windows { get; set; } = [];
        public bool RunTests { get; private set; }
        public IReadOnlyList<TargetWindow> GetWindows() => Windows;
        public Task<InspectionSnapshot> CaptureAsync(nint window, bool runTests)
        {
            RunTests = runTests;
            return Capture();
        }

        public Task<InspectionSnapshot> OpenAsync(string path) => Capture();
        public Task SaveAsync(string path) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
