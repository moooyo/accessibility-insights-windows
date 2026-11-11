// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace AccessibilityInsights.Core;

public enum RecordingScope
{
    Element,
    Descendants,
    Subtree,
}

public sealed record RecordingOption(int Id, string Name, bool IsRecommended, bool IsAvailableOnTarget);

public sealed record EventRecordingCatalog(
    string TargetName,
    IReadOnlyList<RecordingOption> Events,
    IReadOnlyList<RecordingOption> Properties);

public sealed record RecordedProperty(string Name, string Value);

public sealed record RecordedElement(
    int UniqueId,
    string Name,
    string ControlType,
    string AutomationId,
    int ProcessId,
    string RuntimeId,
    string Sender,
    IReadOnlyList<RecordedProperty> Properties)
{
    internal string Identity { get; init; } = string.Empty;
}

public sealed record RecordedEvent(
    long Sequence,
    int EventId,
    string EventName,
    string TimeStamp,
    RecordedElement? Element,
    IReadOnlyList<RecordedProperty> Properties)
{
    public string Sender => Element?.Sender ?? string.Empty;
    public string DisplayName => string.Format(
        CultureInfo.CurrentCulture, "{0}, {1}, {2}", TimeStamp, EventName, Sender);
    internal string LegacyJson { get; init; } = "{}";
}

public sealed record TabStopRecord(
    long Sequence,
    int Order,
    string TimeStamp,
    RecordedElement Element)
{
    public string DisplayName => string.Format(
        CultureInfo.CurrentCulture, "{0}: {1}", Order, Element.Sender);
}

public sealed record RecordingBufferSnapshot<T>(IReadOnlyList<T> Items, int DroppedCount);

public sealed record RecordingState(bool IsEventRecording, bool IsTabStopRecording);

public static class RecordingBufferLimits
{
    public const int Events = 10_000;
    public const int TabStops = 1_000;
}

public sealed class RecordedEventEventArgs(RecordedEvent item) : EventArgs
{
    public RecordedEvent Item { get; } = item;
}

public sealed class TabStopRecordedEventArgs(TabStopRecord item, bool isNew) : EventArgs
{
    public TabStopRecord Item { get; } = item;
    public bool IsNew { get; } = isNew;
}

public sealed class RecordingStateChangedEventArgs(RecordingState state) : EventArgs
{
    public RecordingState State { get; } = state;
}

public sealed class RecordingFaultedEventArgs(string operation, Exception exception) : EventArgs
{
    public string Operation { get; } = operation;
    public Exception Exception { get; } = exception;
}

public partial class RecordingOptionViewModel : ObservableObject
{
    public RecordingOptionViewModel(
        RecordingOption option,
        bool isSelected,
        string automationPrefix = "RecordingOption")
    {
        Id = option.Id;
        Name = option.Name;
        IsRecommended = option.IsRecommended;
        IsAvailableOnTarget = option.IsAvailableOnTarget;
        AutomationId = string.Format(
            CultureInfo.InvariantCulture, "{0}_{1}", automationPrefix, option.Id);
        IsSelected = isSelected;
    }

    public int Id { get; }
    public string Name { get; }
    public string AutomationId { get; }
    public bool IsRecommended { get; }
    public bool IsAvailableOnTarget { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
