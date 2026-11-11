// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.SetupLibrary;
using AccessibilityInsights.VersionSwitcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Cryptography;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class UpdateEngineCompatibilityTests
{
    [TestMethod]
    public void MissingUpdateArgumentsPreserveFailureResultAndLocalizedMessage()
    {
        var engine = new InstallationEngine("Test", null, new ExecutionHistory(), () => ["ignored"]);
        var exception = Assert.ThrowsException<ResultBearingException>(() => engine.GetInstallationOptions());
        Assert.AreEqual(ExecutionResult.ErrorBadCommandLine, exception.Result);
        StringAssert.Contains(exception.Message, "Invalid command line");
    }

    [TestMethod]
    public void InstallerSizeAndSha512ValidationArePreserved()
    {
        var file = Path.Combine(Path.GetTempPath(), $"winui-update-{Guid.NewGuid():N}.msi");
        var bytes = "Migration compatibility fixture"u8.ToArray();
        File.WriteAllBytes(file, bytes);
        try
        {
            var history = new ExecutionHistory();
            var engine = new InstallationEngine("Test", null, history);
            var hash = Convert.ToHexString(SHA512.HashData(bytes));
            engine.ValidateFileProperties(file, bytes.Length, hash.ToLowerInvariant());
            Assert.AreEqual(bytes.Length, history.ActualMsiSizeInBytes);
            Assert.AreEqual(hash, history.ActualMsiSha512);
            var sizeException = Assert.ThrowsException<ResultBearingException>(() =>
                engine.ValidateFileProperties(file, bytes.Length + 1, hash));
            Assert.AreEqual(ExecutionResult.ErrorMsiSizeMismatch, sizeException.Result);
            var hashException = Assert.ThrowsException<ResultBearingException>(() =>
                engine.ValidateFileProperties(file, bytes.Length, new string('0', 128)));
            Assert.AreEqual(ExecutionResult.ErrorMsiSha512Mismatch, hashException.Result);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
