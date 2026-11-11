// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights_VersionSwitcher_WinUI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class UpdateBootstrapTests
{
    [TestMethod]
    public void UntrustedUpdateLocationCannotCopyPayloadOrLaunchElevatedCode()
    {
        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            UpdateBootstrap.RelaunchFromProtectedStaging(
                ["https://example.test/installer.msi", "32", new string('A', 128)]));
        StringAssert.Contains(exception.Message, "trusted installed application");
    }
}
