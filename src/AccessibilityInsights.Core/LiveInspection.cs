// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public enum LiveSelectionScope
{
    Element,
    Application,
}

[Flags]
public enum LiveSelectionMethods
{
    None = 0,
    Mouse = 1,
    KeyboardFocus = 2,
}

public enum TreeNavigationDirection
{
    Parent,
    FirstChild,
    LastChild,
    NextSibling,
    PreviousSibling,
}

public sealed record LiveInspectionOptions(
    LiveSelectionScope Scope,
    LiveSelectionMethods Methods,
    int MouseSelectionDelayMilliseconds)
{
    public static LiveInspectionOptions Default { get; } =
        new(LiveSelectionScope.Element, LiveSelectionMethods.Mouse | LiveSelectionMethods.KeyboardFocus, 100);

    public void Validate()
    {
        if (Methods == LiveSelectionMethods.None ||
            (Methods & ~(LiveSelectionMethods.Mouse | LiveSelectionMethods.KeyboardFocus)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Methods), "Select mouse, keyboard focus, or both.");
        }

        if (MouseSelectionDelayMilliseconds < 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MouseSelectionDelayMilliseconds),
                "The mouse selection delay must be at least 50 milliseconds.");
        }
    }
}

public sealed record ElementPattern(int Id, string Name, IReadOnlyList<ElementProperty> Properties)
{
    public override string ToString() => Name;
}

public sealed record ElementDetails(
    Guid ContextId,
    int ElementId,
    IReadOnlyList<ElementProperty> Properties,
    IReadOnlyList<ElementPattern> Patterns,
    ScreenRectangle Bounds,
    int ProcessId,
    bool IsKeyboardFocusable,
    bool IsLive);

public sealed class LiveSelectionChangedEventArgs : EventArgs
{
    public LiveSelectionChangedEventArgs(InspectionSnapshot snapshot) => Snapshot = snapshot;

    public InspectionSnapshot Snapshot { get; }
}

public sealed class LiveInspectionErrorEventArgs : EventArgs
{
    public LiveInspectionErrorEventArgs(Exception exception) => Exception = exception;

    public Exception Exception { get; }
}

public interface ILiveInspectionService
{
    event EventHandler<LiveSelectionChangedEventArgs>? LiveSelectionChanged;
    event EventHandler<LiveInspectionErrorEventArgs>? LiveInspectionError;

    Task StartLiveInspectionAsync(LiveInspectionOptions options);
    Task StopLiveInspectionAsync();
    Task SetLiveInspectionPausedAsync(bool paused);
    Task ConfigureLiveInspectionAsync(LiveInspectionOptions options);
    Task<InspectionSnapshot?> NavigateAsync(Guid contextId, TreeNavigationDirection direction);
    Task<ElementDetails?> GetElementDetailsAsync(Guid contextId, int elementId);
    Task<bool> HighlightAsync(Guid contextId, int elementId);
    Task ClearHighlightAsync();
    Task<InspectionSnapshot> TestCurrentAsync(Guid contextId);
}
