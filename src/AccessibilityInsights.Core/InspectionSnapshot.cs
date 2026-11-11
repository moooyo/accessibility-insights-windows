// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public sealed class ElementSnapshot
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string ControlType { get; init; }
    public required IReadOnlyList<ElementProperty> Properties { get; init; }
    public required IReadOnlyList<ElementSnapshot> Children { get; init; }
    public required IReadOnlyList<RuleFailure> Failures { get; init; }
    public ScreenRectangle Bounds { get; init; }
    public int ProcessId { get; init; }
    public bool IsKeyboardFocusable { get; init; }
    public string DisplayName => $"{ControlType}: {Name}";
    public override string ToString() => DisplayName;
}

public sealed record ElementProperty(string Name, string Value)
{
    public override string ToString() => $"{Name}: {Value}";
}

public sealed record RuleFailure(int ElementId, string ElementName, string Description, string Details, string HelpUrl)
{
    public override string ToString() => $"{Description} {ElementName}";
}

public enum InspectionKind
{
    Live,
    Test,
    Loaded,
}

public sealed record InspectionSnapshot(Guid ContextId, ElementSnapshot Root, bool IsTruncated)
{
    public InspectionKind Kind { get; init; } = InspectionKind.Live;
    public IEnumerable<RuleFailure> Failures => Enumerate(Root).SelectMany(element => element.Failures);

    private static IEnumerable<ElementSnapshot> Enumerate(ElementSnapshot element)
    {
        yield return element;
        foreach (var child in element.Children)
        {
            foreach (var descendant in Enumerate(child))
            {
                yield return descendant;
            }
        }
    }
}

public sealed record TargetWindow(int ProcessId, nint Handle, string Title)
{
    public string DisplayName => $"{Title} ({ProcessId})";
}
