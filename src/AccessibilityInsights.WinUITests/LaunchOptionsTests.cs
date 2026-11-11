// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class LaunchOptionsTests
{
    [TestMethod]
    public void OriginalOptionNamesAndAliasesArePreserved()
    {
        var shortOptions = LaunchOptions.Parse(["-c", "config", "-u", "data", "-a"]);
        Assert.AreEqual("config", shortOptions.ConfigFolder);
        Assert.AreEqual("data", shortOptions.UserDataFolder);
        Assert.IsTrue(shortOptions.AttachToDebugger);
        var longOptions = LaunchOptions.Parse(["--ConfigFolder", "config", "--UserDataFolder", "data",
            "--AttachToDebugger", "--", "-snapshot.a11ytest"]);
        Assert.AreEqual("-snapshot.a11ytest", longOptions.FileToOpen);
        Assert.IsTrue(longOptions.AttachToDebugger);
    }

    [TestMethod]
    public void InvalidArgumentsAreNotSilentlyDiscarded()
    {
        Assert.ThrowsException<ArgumentException>(() => LaunchOptions.Parse(["--not-a-real-option"]));
        Assert.ThrowsException<FileNotFoundException>(() =>
            new LaunchOptions { FileToOpen = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.a11ytest") }.Validate());
    }

    [TestMethod]
    public void PackagedActivationPreservesWindowsQuotingAndTrailingBackslashes()
    {
        var options = LaunchOptions.ParseRawArguments(
            """-c "C:\Settings path\\" -- "C:\snapshot path\test.a11ytest" """);
        Assert.AreEqual(@"C:\Settings path\", options.ConfigFolder);
        Assert.AreEqual(@"C:\snapshot path\test.a11ytest", options.FileToOpen);
        Assert.ThrowsException<ArgumentException>(() => LaunchOptions.ParseRawArguments("-c config\0 --ignored"));
    }
}
