// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class ContrastViewModelTests
{
    [TestMethod]
    public async Task ScreenSelectionUpdatesOnlyTheRequestedColorAndContrast()
    {
        var picker = new FakePicker();
        var model = CreateModel(picker);
        var pick = model.PickBackgroundCommand.ExecuteAsync(null);
        Assert.IsTrue(model.IsPicking);
        Assert.IsFalse(model.PickForegroundCommand.CanExecute(null));
        picker.Completion.SetResult(new RgbColor(0, 0, 0));
        await pick;
        Assert.AreEqual(new RgbColor(0, 0, 0), model.Background);
        Assert.AreEqual(new RgbColor(0, 0, 0), model.Foreground);
        StringAssert.StartsWith(model.Result, "1:1");
        Assert.IsFalse(model.IsPicking);
        Assert.IsTrue(model.PickForegroundCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task CancelledScreenSelectionPreservesColorsAndResults()
    {
        var model = CreateModel(new FakePicker());
        var result = model.Result;
        var pick = model.PickBackgroundCommand.ExecuteAsync(null);
        model.PickBackgroundCommand.Cancel();
        await pick;
        Assert.AreEqual(new RgbColor(255, 255, 255), model.Background);
        Assert.AreEqual(result, model.Result);
        Assert.IsFalse(model.HasError);
        Assert.AreEqual("ScreenPickCancelled", model.Status);
    }

    [TestMethod]
    public async Task FailedScreenSelectionReportsTheErrorAndRestoresCommands()
    {
        var picker = new FakePicker();
        var model = CreateModel(picker);
        var pick = model.PickForegroundCommand.ExecuteAsync(null);
        picker.Completion.SetException(new InvalidOperationException("Screen capture unavailable"));
        await pick;
        Assert.AreEqual("Screen capture unavailable", model.Error);
        Assert.IsTrue(model.HasError);
        Assert.IsTrue(model.PickForegroundCommand.CanExecute(null));
        StringAssert.StartsWith(model.Result, "21:1");
    }

    private static ContrastViewModel CreateModel(IScreenColorPicker picker) => new(picker,
        key => key == "ContrastResultDetailed" ? "{0}:1 {1}/{2}/{3}" : key);

    private sealed class FakePicker : IScreenColorPicker
    {
        public TaskCompletionSource<RgbColor?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RgbColor?> PickAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Completion.TrySetCanceled(cancellationToken));
            return await Completion.Task;
        }
    }
}
