// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using AccessibilityInsights.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class ContrastTests
{
    [DataTestMethod]
    [DataRow("#000000", "#FFFFFF", 21.0)]
    [DataRow("#FFFFFF", "#000000", 21.0)]
    [DataRow("#123456", "#123456", 1.0)]
    [DataRow("#FF0000", "#FFFFFF", 3.9984767707539985)]
    public void ContrastPreservesWpfLuminanceCalculation(string first, string second, double expected)
    {
        Assert.IsTrue(RgbColor.TryParse(first, out var foreground));
        Assert.IsTrue(RgbColor.TryParse(second, out var background));
        Assert.AreEqual(expected, ContrastCalculator.CalculateRatio(foreground, background), 0.0000001);
    }

    [DataTestMethod]
    [DataRow("#12345")]
    [DataRow("#1234567")]
    [DataRow("#GG0000")]
    [DataRow("#FF000000")]
    [DataRow("")]
    [DataRow(null)]
    public void InvalidOrTransparentColorsAreRejected(string? text)
    {
        Assert.IsFalse(RgbColor.TryParse(text, out _));
    }

    [TestMethod]
    public void ColorInputAcceptsTrimmedHexAndNormalizesItsCase()
    {
        Assert.IsTrue(RgbColor.TryParse(" abcdef ", out var color));
        Assert.AreEqual("#ABCDEF", color.ToString());
    }

    [TestMethod]
    public void InvalidInputClearsStalePassingResultsAndRecovers()
    {
        var viewModel = new MainPageViewModel(TestResources.GetString) { Foreground = "#bad" };
        Assert.AreNotEqual(string.Empty, viewModel.ContrastError);
        Assert.AreEqual(string.Empty, viewModel.ContrastRatio);
        Assert.AreEqual(string.Empty, viewModel.SmallTextResult);

        viewModel.Foreground = "#FFFFFF";
        Assert.AreEqual(string.Empty, viewModel.ContrastError);
        Assert.AreEqual("1:1", viewModel.ContrastRatio);
        Assert.AreEqual("Fail", viewModel.SmallTextResult);
        Assert.AreEqual("Fail", viewModel.LargeTextResult);
    }

    [TestMethod]
    public void NormalTextAndLargeTextUseDistinctUnroundedThresholds()
    {
        var viewModel = new MainPageViewModel(TestResources.GetString) { Foreground = "#FF0000" };
        Assert.AreEqual("Fail", viewModel.SmallTextResult);
        Assert.AreEqual("Pass", viewModel.LargeTextResult);
        Assert.AreEqual("Pass", viewModel.NonTextResult);
        Assert.AreEqual("3.998:1", viewModel.ContrastRatio);
    }

    [TestMethod]
    public void SnapshotReplacementDoesNotAccumulateStaleRowsAndWarnsOnTruncation()
    {
        var viewModel = new MainPageViewModel(TestResources.GetString);
        viewModel.UpdateSnapshot(new("Old", [new("Name", "Old")], [new(1, "Old", "Button", 0)],
            [new("Fail", "Old", "Missing name", "")], false));
        viewModel.UpdateSnapshot(new("New", [new("Name", "New")], [], [], true));

        Assert.AreEqual("New", viewModel.SelectedElementName);
        Assert.AreEqual(1, viewModel.Properties.Count);
        Assert.AreEqual("New", viewModel.Properties[0].Value);
        Assert.AreEqual(0, viewModel.Results.Count);
        Assert.AreEqual(0, viewModel.Elements.Count);
        Assert.IsTrue(viewModel.HasSelection);
        StringAssert.Contains(viewModel.Status, "incomplete");
    }

    [TestMethod]
    public void ScanFailuresArePresentedBeforePassingChecksWithoutDiscardingResults()
    {
        var viewModel = new MainPageViewModel(TestResources.GetString);
        var passing = new RuleRow("Pass", "Window", "Passing rule", "");
        var failing = new RuleRow("Fail", "Button", "Missing name", "");
        viewModel.UpdateSnapshot(new("Window", [], [], [passing, failing], false));
        CollectionAssert.AreEqual(new[] { failing, passing }, viewModel.Results.ToArray());
        StringAssert.Contains(viewModel.Status, "2 checks; 1 failed checks.");
    }

    [TestMethod]
    public void RestartingSelectionDoesNotExposeResultsFromTheReleasedContext()
    {
        var viewModel = new MainPageViewModel(TestResources.GetString);
        viewModel.UpdateSnapshot(new("Old", [new("Name", "Old")], [new(1, "Old", "Button", 0)],
            [new("Fail", "Old", "Missing name", "")], false));
        viewModel.TabStops.Add(new(1, "Old", default));
        viewModel.ClearSelection();
        Assert.IsFalse(viewModel.HasSelection);
        Assert.AreEqual("No element selected", viewModel.SelectedElementName);
        Assert.AreEqual(0, viewModel.Elements.Count);
        Assert.AreEqual(0, viewModel.Properties.Count);
        Assert.AreEqual(0, viewModel.Results.Count);
        Assert.AreEqual(0, viewModel.TabStops.Count);
    }
}
