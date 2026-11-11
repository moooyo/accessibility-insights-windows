// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace AccessibilityInsights.WinUITests;

[TestClass]
[DoNotParallelize]
public sealed class LiveInspectionIntegrationTests
{
    [TestMethod]
    public async Task RepeatedLiveDetailsPreservePropertiesAndSnapshotRoundTrip()
    {
        using var target = Process.Start(GetTargetPath())
            ?? throw new InvalidOperationException("The WinUITestTarget process could not be started.");
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"live-clickable-{Guid.NewGuid():N}.a11ytest");
        try
        {
            var window = await WaitForMainWindowAsync(target, TimeSpan.FromSeconds(10));
            await using var service = new InspectionService();
            var snapshot = await service.CaptureAsync(window, false);
            var button = snapshot.Root.Children.Single(element => element.Name == "Save");
            var first = await service.GetElementDetailsAsync(snapshot.ContextId, button.Id);
            Assert.IsNotNull(first);
            Assert.IsTrue(first.Properties.Any(property =>
                property.Name == "Name" && property.Value == "Save"));

            var refreshed = await service.GetElementDetailsAsync(snapshot.ContextId, button.Id);
            Assert.IsNotNull(refreshed);
            CollectionAssert.AreEquivalent(first.Properties.ToArray(), refreshed.Properties.ToArray());

            await service.SaveAsync(snapshotPath);
            var loaded = await service.OpenAsync(snapshotPath);
            var loadedButton = loaded.Root.Children.Single(element => element.Name == "Save");
            CollectionAssert.AreEquivalent(refreshed.Properties.ToArray(), loadedButton.Properties.ToArray());
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }

            CloseTarget(target);
        }
    }

    [TestMethod]
    public async Task CaptureThenRunTestsFindsRealUnnamedButtonFailure()
    {
        var targetPath = GetTargetPath();
        Assert.IsTrue(File.Exists(targetPath), $"Build the WinUITestTarget first: {targetPath}");
        using var target = Process.Start(new ProcessStartInfo
        {
            FileName = targetPath,
            WorkingDirectory = Path.GetDirectoryName(targetPath),
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("The WinUITestTarget process could not be started.");

        try
        {
            var window = await WaitForMainWindowAsync(target, TimeSpan.FromSeconds(10));
            await using var service = new InspectionService();
            var workbench = new WorkbenchViewModel(
                service,
                new UnusedFilePicker(),
                key => key == "CaptureComplete" ? "{0}" : key)
            {
                IsHighlightEnabled = false,
                SelectedWindow = new TargetWindow(target.Id, window, "WinUI migration test target"),
            };
            await workbench.InspectCommand.ExecuteAsync(null);
            Assert.IsFalse(workbench.HasError, workbench.Error);
            await workbench.TestCommand.ExecuteAsync(null);
            Assert.IsFalse(workbench.HasError, workbench.Error);

            var root = workbench.Roots.Single();
            var unnamedFailure = workbench.Failures.FirstOrDefault(failure =>
                string.IsNullOrEmpty(failure.ElementName) &&
                FindElement(root, failure.ElementId)?.ControlType.Equals(
                    "button", StringComparison.OrdinalIgnoreCase) == true);
            Assert.IsNotNull(unnamedFailure, string.Join(
                Environment.NewLine,
                workbench.Failures.Select(failure =>
                    $"{failure.ElementId}: {failure.ElementName} - {failure.Description}")));
        }
        finally
        {
            CloseTarget(target);
        }
    }

    private static void CloseTarget(Process target)
    {
        if (!target.HasExited)
        {
            target.CloseMainWindow();
            if (!target.WaitForExit(3000))
            {
                target.Kill(entireProcessTree: true);
                target.WaitForExit();
            }
        }
    }

    private static async Task<nint> WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The WinUITestTarget exited with code {process.ExitCode} before creating its window.");
            }

            if (process.MainWindowHandle != 0)
            {
                return process.MainWindowHandle;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The WinUITestTarget did not create its main window.");
    }

    private static ElementSnapshot? FindElement(ElementSnapshot element, int id)
    {
        if (element.Id == id)
        {
            return element;
        }

        foreach (var child in element.Children)
        {
            var match = FindElement(child, id);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string GetTargetPath()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            ".."));
        return Path.Combine(
            repositoryRoot,
            "src",
            "AccessibilityInsights.WinUITestTarget",
            "bin",
            "x64",
            "Debug",
            "net10.0-windows",
            "AccessibilityInsights.WinUITestTarget.exe");
    }

    private sealed class UnusedFilePicker : ISnapshotFilePicker
    {
        public Task<string?> OpenAsync() => throw new InvalidOperationException();
        public Task<string?> SaveAsync() => throw new InvalidOperationException();
    }
}
