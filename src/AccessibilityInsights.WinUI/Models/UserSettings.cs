// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
namespace AccessibilityInsights.Models;

public sealed record UserSettings
{
    public int Theme { get; init; }
    public bool SelectByFocus { get; init; } = true;
    public bool ApplicationScope { get; init; }
    public bool AlwaysOnTop { get; init; }
    public string PauseHotkey { get; init; } = "Shift + F5";
    public string RecordHotkey { get; init; } = "Shift + F7";
    public string ScanHotkey { get; init; } = "Shift + F8";
    public string GitHubRepository { get; init; } = string.Empty;
}

public readonly record struct Hotkey(uint Modifiers, uint Key)
{
    public static Hotkey Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var tokens = text.Split(['+', ','], StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) { throw new FormatException("A hotkey must specify a key."); }
        uint modifiers = 0;
        foreach (var token in tokens[..^1])
        {
            modifiers |= token.ToUpperInvariant() switch
            {
                "ALT" => 1u,
                "CONTROL" or "CTRL" => 2u,
                "SHIFT" => 4u,
                "WINDOWS" or "WIN" => 8u,
                _ => throw new FormatException($"Unknown hotkey modifier: {token}"),
            };
        }

        var key = tokens[^1].ToUpperInvariant();
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            if (modifiers == 0) { throw new FormatException("Letter and digit hotkeys require a modifier."); }
            return new(modifiers, key[0]);
        }

        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24)
        {
            return new(modifiers, (uint)(111 + number));
        }

        throw new FormatException("Hotkeys must use a letter, digit, or F1 through F24.");
    }
}
