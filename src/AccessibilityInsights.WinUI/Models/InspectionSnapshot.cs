// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
namespace AccessibilityInsights.Models;

public sealed record PropertyRow(string Name, string Value);

public readonly record struct ElementBounds(int X, int Y, int Width, int Height);

public sealed record ElementRow(int Id, string Name, string ControlType, int Depth, ElementBounds Bounds = default)
{
    public string Label => $"{new string(' ', Depth * 2)}{ControlType}: {Name}";
}

public sealed record RuleRow(string Status, string Element, string Description, string Details,
    string Source = "", string HelpUrl = "", string Glimpse = "");

public sealed record EventRow(string Time, string Event, string Element, string Details);

public sealed record TabStopRow(int Number, string Name, ElementBounds Bounds)
{
    public string Label => $"{Number}. {Name}";
}

public sealed record DetectedColors(RgbColor Foreground, RgbColor Background, string Confidence);

public sealed record InspectionSnapshot(
    string Name,
    IReadOnlyList<PropertyRow> Properties,
    IReadOnlyList<ElementRow> Elements,
    IReadOnlyList<RuleRow> Results,
    bool IsTruncated,
    ElementBounds Bounds = default,
    string ProcessName = "");
