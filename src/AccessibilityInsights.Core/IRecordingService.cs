// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core;

public interface IRecordingService
{
    event EventHandler<RecordedEventEventArgs>? EventRecorded;
    event EventHandler<TabStopRecordedEventArgs>? TabStopRecorded;
    event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;
    event EventHandler<RecordingFaultedEventArgs>? RecordingFaulted;

    RecordingState State { get; }

    Task<EventRecordingCatalog> GetEventRecordingCatalogAsync(int elementId);
    Task StartEventRecordingAsync(
        int elementId,
        RecordingScope scope,
        IReadOnlyCollection<int> eventIds,
        IReadOnlyCollection<int> propertyIds);
    Task StopEventRecordingAsync();
    Task ClearEventsAsync();
    Task<RecordingBufferSnapshot<RecordedEvent>> GetEventsAsync();
    Task<RecordingBufferSnapshot<RecordedEvent>> ImportEventsAsync(string path);
    Task ExportEventsAsync(string path);

    Task StartTabStopRecordingAsync(int elementId);
    Task StopTabStopRecordingAsync();
    Task ClearTabStopsAsync();
    Task<RecordingBufferSnapshot<TabStopRecord>> GetTabStopsAsync();
}
