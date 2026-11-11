// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using System.IO;
using System.Text.Json;

namespace AccessibilityInsights.Services;

public sealed class SettingsStore(string path)
{
    public UserSettings Load()
    {
        if (!File.Exists(path)) { return new(); }
        var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ??
            throw new InvalidDataException("The settings file must contain a settings object.");
        Validate(settings);
        return settings;
    }

    public void Save(UserSettings settings)
    {
        Validate(settings);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    public static UserSettings ReadLegacy(string path, UserSettings current)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out _))
        {
            throw new InvalidDataException("Choose an Accessibility Insights WPF configuration file.");
        }

        bool Boolean(string name, bool fallback)
        {
            if (!root.TryGetProperty(name, out var value)) { return fallback; }
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException($"The legacy setting {name} must be a boolean.");
            }
            return value.GetBoolean();
        }
        string Text(string name, string fallback)
        {
            if (!root.TryGetProperty(name, out var value)) { return fallback; }
            return value.ValueKind == JsonValueKind.String ? value.GetString()! :
                throw new InvalidDataException($"The legacy setting {name} must be text.");
        }

        var imported = current with
        {
            SelectByFocus = Boolean("SelectionByFocus", current.SelectByFocus),
            ApplicationScope = !Boolean("IsUnderElementScope", !current.ApplicationScope),
            AlwaysOnTop = Boolean("AlwaysOnTop", current.AlwaysOnTop),
            PauseHotkey = Text("HotKeyForPause", current.PauseHotkey),
            RecordHotkey = Text("HotKeyForRecord", current.RecordHotkey),
            ScanHotkey = Text("HotKeyForSnap", current.ScanHotkey),
        };
        Validate(imported);
        return imported;
    }

    public static void Validate(UserSettings settings)
    {
        if (settings.Theme is < 0 or > 2) { throw new InvalidDataException("Unknown theme setting."); }
        var hotkeys = new[] { Hotkey.Parse(settings.PauseHotkey), Hotkey.Parse(settings.RecordHotkey),
            Hotkey.Parse(settings.ScanHotkey) };
        if (hotkeys.Distinct().Count() != hotkeys.Length)
        {
            throw new InvalidDataException("Selection, recording, and scanning must use distinct hotkeys.");
        }
        if (!string.IsNullOrWhiteSpace(settings.GitHubRepository))
        {
            GitHubIssueService.ValidateRepository(settings.GitHubRepository);
        }
    }
}
