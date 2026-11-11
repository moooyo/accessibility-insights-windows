// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using AccessibilityInsights.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class SettingsTests
{
    [DataTestMethod]
    [DataRow("Shift + F8", 4u, 119u)]
    [DataRow("Control,Shift + F5", 6u, 116u)]
    [DataRow("Ctrl + Alt + K", 3u, 75u)]
    public void ExistingWpfHotkeyNotationMapsToNativeRegistration(string text, uint modifiers, uint key)
    {
        Assert.AreEqual(new Hotkey(modifiers, key), Hotkey.Parse(text));
    }

    [DataTestMethod]
    [DataRow("+")]
    [DataRow("Control + F25")]
    [DataRow("Unknown + F5")]
    [DataRow("A")]
    [DataRow("Shift + + F5")]
    [DataRow("Shift +")]
    public void InvalidOrTypingHijackingHotkeysAreRejected(string text)
    {
        Assert.ThrowsException<FormatException>(() => Hotkey.Parse(text));
    }

    [TestMethod]
    public void SettingsPersistAndInvalidChangesDoNotOverwriteTheLastValidConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            var settings = new UserSettings { Theme = 2, SelectByFocus = true, AlwaysOnTop = true };
            store.Save(settings);
            Assert.AreEqual(settings, store.Load());
            Assert.ThrowsException<InvalidDataException>(() =>
                store.Save(settings with { PauseHotkey = settings.ScanHotkey }));
            Assert.AreEqual(settings, store.Load());
        }
        finally { File.Delete(path); }
    }

    [DataTestMethod]
    [DataRow("ConfigSettings.json")]
    [DataRow("LegacyConfigSettings.json")]
    public void OriginalWpfConfigurationsImportSupportedSettingsWithoutChangingTheSource(string filename)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestFiles", filename);
        var before = File.ReadAllText(source);
        var current = new UserSettings { Theme = 2, GitHubRepository = "https://github.com/owner/repo" };
        var imported = SettingsStore.ReadLegacy(source, current);
        Assert.AreEqual("Alt + F2", imported.PauseHotkey);
        Assert.AreEqual("Alt + F1", imported.RecordHotkey);
        Assert.AreEqual("Alt + F3", imported.ScanHotkey);
        Assert.IsTrue(imported.SelectByFocus);
        Assert.IsFalse(imported.ApplicationScope);
        Assert.AreEqual(current.Theme, imported.Theme);
        Assert.AreEqual(current.GitHubRepository, imported.GitHubRepository);
        Assert.AreEqual(before, File.ReadAllText(source));
    }
}
