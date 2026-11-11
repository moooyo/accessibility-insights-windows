// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Xml.Linq;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class InventoryGenerationTests
{
    [TestMethod]
    public async Task InventoryIncludesNestedResourcesAndStableComponentIdentities()
    {
        var root = Path.Combine(Path.GetTempPath(), $"winui-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            foreach (var layout in new[] { first, second })
            {
                Directory.CreateDirectory(Path.Combine(layout, "Assets", "en-US"));
                File.WriteAllText(Path.Combine(layout, "AccessibilityInsights.exe"), "Inventory fixture, not an executable");
                File.WriteAllText(Path.Combine(layout, "Assets", "en-US", "resources.pri"), "PRI fixture");
                File.WriteAllText(Path.Combine(layout, "AccessibilityInsights.runtimeconfig.json"), "{}");
            }

            var firstInventory = Path.Combine(root, "first.wxs");
            var secondInventory = Path.Combine(root, "second.wxs");
            Assert.AreEqual(0, await GenerateAsync(first, firstInventory));
            Assert.AreEqual(0, await GenerateAsync(second, secondInventory));
            XNamespace ns = "http://wixtoolset.org/schemas/v4/wxs";
            var document = XDocument.Load(firstInventory);
            Assert.AreEqual(3, document.Descendants(ns + "File").Count());
            Assert.IsTrue(document.Descendants(ns + "File").Any(file => (string?)file.Attribute("Id") == "FileExe"));
            Assert.IsTrue(document.Descendants(ns + "Directory").Any(directory => (string?)directory.Attribute("Name") == "en-US"));
            CollectionAssert.AreEqual(
                document.Descendants(ns + "Component").Select(component => (string)component.Attribute("Guid")!).ToArray(),
                XDocument.Load(secondInventory).Descendants(ns + "Component").Select(component => (string)component.Attribute("Guid")!).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task InventoryRejectsWpfDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"winui-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "AccessibilityInsights.exe"), "Inventory fixture");
            File.WriteAllText(Path.Combine(root, "PresentationFramework.dll"), "WPF fixture");
            Assert.AreNotEqual(0, await GenerateAsync(root, Path.Combine(root, "rejected.wxs")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task<int> GenerateAsync(string layout, string output)
    {
        var startInfo = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(AppContext.BaseDirectory, "Scripts", "New-WinUIInventory.ps1"),
            "-LayoutDirectory", layout, "-OutputFile", output })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw;
        }

        Trace.WriteLine(await standardOutput);
        Trace.WriteLine(await standardError);
        return process.ExitCode;
    }
}
