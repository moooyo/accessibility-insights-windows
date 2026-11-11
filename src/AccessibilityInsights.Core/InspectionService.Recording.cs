// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Axe.Windows.Actions;
using Axe.Windows.Core.Bases;
using Axe.Windows.Core.Enums;
using Axe.Windows.Core.Misc;
using Axe.Windows.Core.Types;
using Axe.Windows.Desktop.Types;
using Axe.Windows.Desktop.UIAutomation;
using Axe.Windows.Desktop.UIAutomation.EventHandlers;
using Axe.Windows.Desktop.UIAutomation.TreeWalkers;
using Axe.Windows.Desktop.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AccessibilityInsights.Core;

public sealed partial class InspectionService : IRecordingService
{
    private const long MaximumEventFileBytes = 100L * 1024 * 1024;
    private readonly object recordingGate = new();
    private readonly Queue<RecordedEvent> recordedEvents = new();
    private readonly Queue<TabStopRecord> recordedTabStops = new();
    private readonly HashSet<string> recordedTabStopIdentities = new(StringComparer.Ordinal);
    private EventListenerFactory? eventListener;
    private EventListenerFactory? tabStopListener;
    private A11yElement? tabStopRoot;
    private bool isEventRecording;
    private bool isTabStopRecording;
    private int eventRecordingGeneration;
    private int tabStopRecordingGeneration;
    private int droppedEventCount;
    private int droppedTabStopCount;
    private int tabStopOrder;
    private long eventSequence;
    private long tabStopSequence;
    private string? lastTabStopIdentity;

    public event EventHandler<RecordedEventEventArgs>? EventRecorded;
    public event EventHandler<TabStopRecordedEventArgs>? TabStopRecorded;
    public event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;
    public event EventHandler<RecordingFaultedEventArgs>? RecordingFaulted;

    public RecordingState State
    {
        get
        {
            lock (recordingGate)
            {
                return new RecordingState(isEventRecording, isTabStopRecording);
            }
        }
    }

    public Task<EventRecordingCatalog> GetEventRecordingCatalogAsync(int elementId) => Enqueue(() =>
    {
        var element = GetRecordingElement(elementId, requireLive: false);
        var recommendedEventIds = SupportedEvents
            .GetEventsForControl(element.ControlTypeId, element.Patterns ?? [])
            .Append(EventType.UIA_AutomationFocusChangedEventId)
            .ToHashSet();
        var availablePropertyIds = element.Properties is null
            ? new HashSet<int>()
            : element.Properties.Keys.ToHashSet();

        var eventNames = EventType.GetInstance().GetKeyValuePairList()
            .ToDictionary(item => item.Key, item => item.Value);
        eventNames[EventType.UIA_AutomationFocusChangedEventId] =
            EventType.GetInstance().GetNameById(EventType.UIA_AutomationFocusChangedEventId);
        foreach (var id in recommendedEventIds)
        {
            eventNames.TryAdd(id, EventType.GetInstance().GetNameById(id));
        }

        var events = eventNames
            .Select(item => new RecordingOption(
                item.Key,
                item.Value,
                recommendedEventIds.Contains(item.Key),
                recommendedEventIds.Contains(item.Key)))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id)
            .ToArray();

        var properties = PropertyType.GetInstance().GetKeyValuePairList()
            .Where(item => !DesktopElement.IsExcludedProperty(item.Key, item.Value))
            .Select(item => new RecordingOption(
                item.Key,
                item.Value,
                IsRecommended: false,
                availablePropertyIds.Contains(item.Key)))
            .OrderByDescending(item => item.IsAvailableOnTarget)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id)
            .ToArray();

        return new EventRecordingCatalog(
            string.IsNullOrWhiteSpace(element.Glimpse) ? element.Name ?? string.Empty : element.Glimpse,
            events,
            properties);
    });

    public Task StartEventRecordingAsync(
        int elementId,
        RecordingScope scope,
        IReadOnlyCollection<int> eventIds,
        IReadOnlyCollection<int> propertyIds)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        ArgumentNullException.ThrowIfNull(propertyIds);
        var selectedEvents = eventIds
            .Where(id => id > 0 && id != EventType.UIA_AutomationPropertyChangedEventId)
            .Distinct()
            .ToArray();
        var selectedProperties = propertyIds.Where(id => id > 0).Distinct().ToArray();
        if (selectedEvents.Length == 0 && selectedProperties.Length == 0)
        {
            throw new ArgumentException("Select at least one event or property to record.");
        }

        return Enqueue(() =>
        {
            var element = GetRecordingElement(elementId, requireLive: true);
            StopEventRecordingCore();
            StopTabStopRecordingCore();
            ClearEventBuffer();

            var listener = new EventListenerFactory(element, ToListenScope(scope));
            int generation;
            lock (recordingGate)
            {
                eventListener = listener;
                isEventRecording = true;
                generation = ++eventRecordingGeneration;
            }

            try
            {
                foreach (var eventId in selectedEvents)
                {
                    listener.RegisterAutomationEventListener(
                        eventId, message => OnEventMessage(generation, message));
                }

                if (selectedProperties.Length > 0)
                {
                    listener.RegisterAutomationEventListener(
                        EventType.UIA_AutomationPropertyChangedEventId,
                        message => OnEventMessage(generation, message),
                        selectedProperties);
                }
            }
            catch
            {
                lock (recordingGate)
                {
                    isEventRecording = false;
                    eventListener = null;
                    eventRecordingGeneration++;
                }

                listener.Dispose();
                PublishRecordingStateChanged();
                throw;
            }

            PublishRecordingStateChanged();
            return true;
        });
    }

    public Task StopEventRecordingAsync() => Enqueue(() =>
    {
        StopEventRecordingCore();
        return true;
    });

    public Task ClearEventsAsync() => Enqueue(() =>
    {
        ClearEventBuffer();
        return true;
    });

    public Task<RecordingBufferSnapshot<RecordedEvent>> GetEventsAsync() => Enqueue(GetEventSnapshot);

    public Task<RecordingBufferSnapshot<RecordedEvent>> ImportEventsAsync(string path) => Enqueue(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var imported = ReadEventFile(path);
        StopEventRecordingCore();
        StopTabStopRecordingCore();

        lock (recordingGate)
        {
            recordedEvents.Clear();
            foreach (var item in imported.Items)
            {
                recordedEvents.Enqueue(item);
            }

            droppedEventCount = imported.DroppedCount;
            eventSequence = imported.Items.Count == 0 ? 0 : imported.Items[^1].Sequence;
        }

        return GetEventSnapshot();
    });

    public Task ExportEventsAsync(string path) => Enqueue(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RecordedEvent[] events;
        lock (recordingGate)
        {
            events = recordedEvents.ToArray();
        }

        if (events.Length == 0)
        {
            throw new InvalidOperationException("There are no recorded events to save.");
        }

        var output = new JArray(events.Select(item => JObject.Parse(item.LegacyJson)));
        File.WriteAllText(path, output.ToString(Formatting.Indented), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return true;
    });

    public Task StartTabStopRecordingAsync(int elementId) => Enqueue(() =>
    {
        var element = GetRecordingElement(elementId, requireLive: true);
        StopEventRecordingCore();
        StopTabStopRecordingCore();
        ClearTabStopBuffer();

        var listener = new EventListenerFactory(element, ListenScope.Subtree);
        int generation;
        lock (recordingGate)
        {
            tabStopRoot = element;
            tabStopListener = listener;
            isTabStopRecording = true;
            generation = ++tabStopRecordingGeneration;
        }

        try
        {
            listener.RegisterAutomationEventListener(
                EventType.UIA_AutomationFocusChangedEventId,
                message => OnTabStopMessage(generation, message));
        }
        catch
        {
            lock (recordingGate)
            {
                isTabStopRecording = false;
                tabStopRoot = null;
                tabStopListener = null;
                tabStopRecordingGeneration++;
            }

            listener.Dispose();
            PublishRecordingStateChanged();
            throw;
        }

        PublishRecordingStateChanged();
        return true;
    });

    public Task StopTabStopRecordingAsync() => Enqueue(() =>
    {
        StopTabStopRecordingCore();
        return true;
    });

    public Task ClearTabStopsAsync() => Enqueue(() =>
    {
        ClearTabStopBuffer();
        return true;
    });

    public Task<RecordingBufferSnapshot<TabStopRecord>> GetTabStopsAsync() => Enqueue(GetTabStopSnapshot);

    partial void StopRecording()
    {
        Exception? eventException = null;
        Exception? tabStopException = null;
        try
        {
            StopEventRecordingCore();
        }
        catch (Exception exception)
        {
            eventException = exception;
        }

        try
        {
            StopTabStopRecordingCore();
        }
        catch (Exception exception)
        {
            tabStopException = exception;
        }

        ClearTabStopBuffer();
        if (eventException is not null && tabStopException is not null)
        {
            throw new AggregateException(eventException, tabStopException);
        }

        if (eventException is not null)
        {
            throw eventException;
        }

        if (tabStopException is not null)
        {
            throw tabStopException;
        }
    }

    private A11yElement GetRecordingElement(int elementId, bool requireLive)
    {
        var context = currentContext ?? throw new InvalidOperationException("Capture a live target before recording.");
        var element = GetDataAction.GetA11yElementInDataContext(context, elementId)
            ?? throw new InvalidOperationException("The selected element is no longer available.");
        if (element.PlatformObject is not null)
        {
            element = GetDataAction.GetA11yElementWithLiveData(context, elementId) ?? element;
        }

        if (requireLive && element.PlatformObject is null)
        {
            throw new InvalidOperationException("Recording requires a live inspection target.");
        }

        AxePropertyCompatibility.PrepareForCleanup(element);
        return element;
    }

    private static ListenScope ToListenScope(RecordingScope scope) => scope switch
    {
        RecordingScope.Element => ListenScope.Element,
        RecordingScope.Descendants => ListenScope.Descendants,
        _ => ListenScope.Subtree,
    };

    private void OnEventMessage(int generation, EventMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            lock (recordingGate)
            {
                if (!isEventRecording || generation != eventRecordingGeneration)
                {
                    return;
                }
            }

            var token = JObject.FromObject(message, CreateLegacySerializer());
            var item = CreateRecordedEvent(token, sequence: 0);
            lock (recordingGate)
            {
                if (!isEventRecording || generation != eventRecordingGeneration)
                {
                    return;
                }

                item = item with { Sequence = ++eventSequence };
                if (recordedEvents.Count == RecordingBufferLimits.Events)
                {
                    recordedEvents.Dequeue();
                    droppedEventCount++;
                }

                recordedEvents.Enqueue(item);
            }

            EventRecorded?.Invoke(this, new RecordedEventEventArgs(item));
            ReportRegistrationFailureIfPresent(item.Properties, "Register event listener");
        }
        catch (Exception exception)
        {
            ReportRecordingFault("Record UI Automation event", exception);
        }
        finally
        {
            message.Dispose();
        }
    }

    private void OnTabStopMessage(int generation, EventMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        DesktopElementAncestry? ancestry = null;
        try
        {
            lock (recordingGate)
            {
                if (!isTabStopRecording || generation != tabStopRecordingGeneration)
                {
                    return;
                }
            }

            if (message.EventId == EventType.UIA_EventRecorderNotificationEventId)
            {
                var notification = JObject.FromObject(message, CreateLegacySerializer());
                if (ReportRegistrationFailureIfPresent(
                    ReadEventProperties(notification.GetValue("Properties", StringComparison.OrdinalIgnoreCase)),
                    "Register keyboard focus listener"))
                {
                    _ = StopTabStopRecordingAsync().ContinueWith(
                        task => Trace.TraceError("Could not stop failed tab stop recording: {0}", task.Exception),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }

                return;
            }

            if (message.EventId != EventType.UIA_AutomationFocusChangedEventId || message.Element is null)
            {
                return;
            }

            A11yElement root;
            lock (recordingGate)
            {
                if (!isTabStopRecording || generation != tabStopRecordingGeneration || tabStopRoot is null)
                {
                    return;
                }

                root = tabStopRoot;
            }

            if (message.Element.ControlTypeId == ControlType.UIA_ToolTipControlTypeId)
            {
                return;
            }

            ancestry = new DesktopElementAncestry(TreeViewMode.Control, message.Element, setMembers: true);
            if (!IsChildOf(root, message.Element))
            {
                return;
            }

            var elementToken = JObject.FromObject(message.Element, CreateLegacySerializer());
            var recordedElement = CreateRecordedElement(elementToken);
            TabStopRecord item;
            bool isNew;
            lock (recordingGate)
            {
                if (!isTabStopRecording || generation != tabStopRecordingGeneration)
                {
                    return;
                }

                if (string.Equals(lastTabStopIdentity, recordedElement.Identity, StringComparison.Ordinal))
                {
                    return;
                }

                lastTabStopIdentity = recordedElement.Identity;
                var existing = recordedTabStops.FirstOrDefault(
                    candidate => string.Equals(
                        candidate.Element.Identity, recordedElement.Identity, StringComparison.Ordinal));
                if (existing is not null)
                {
                    item = existing;
                    isNew = false;
                }
                else
                {
                    item = new TabStopRecord(
                        ++tabStopSequence,
                        ++tabStopOrder,
                        message.TimeStamp ?? string.Empty,
                        recordedElement);
                    if (recordedTabStops.Count == RecordingBufferLimits.TabStops)
                    {
                        var removed = recordedTabStops.Dequeue();
                        recordedTabStopIdentities.Remove(removed.Element.Identity);
                        droppedTabStopCount++;
                    }

                    recordedTabStops.Enqueue(item);
                    recordedTabStopIdentities.Add(recordedElement.Identity);
                    isNew = true;
                }
            }

            TabStopRecorded?.Invoke(this, new TabStopRecordedEventArgs(item, isNew));
        }
        catch (Exception exception)
        {
            ReportRecordingFault("Record keyboard focus", exception);
        }
        finally
        {
            DisposeAncestry(ancestry, message.Element);
            message.Dispose();
        }
    }

    private static bool IsChildOf(A11yElement root, A11yElement? element)
    {
        while (element is not null)
        {
            if (element.IsSameUIElement(root))
            {
                return true;
            }

            element = element.Parent;
        }

        return false;
    }

    private static void DisposeAncestry(DesktopElementAncestry? ancestry, A11yElement? eventElement)
    {
        if (ancestry is null)
        {
            return;
        }

        var disposed = new HashSet<A11yElement>(ReferenceEqualityComparer.Instance);
        foreach (var item in ancestry.Items)
        {
            if (!ReferenceEquals(item, eventElement) && disposed.Add(item))
            {
                item.Dispose();
            }
        }
    }

    private void StopEventRecordingCore()
    {
        EventListenerFactory? listener;
        bool changed;
        lock (recordingGate)
        {
            changed = isEventRecording || eventListener is not null;
            isEventRecording = false;
            eventRecordingGeneration++;
            listener = eventListener;
            eventListener = null;
        }

        try
        {
            listener?.Dispose();
        }
        finally
        {
            if (changed)
            {
                PublishRecordingStateChanged();
            }
        }
    }

    private void StopTabStopRecordingCore()
    {
        EventListenerFactory? listener;
        bool changed;
        lock (recordingGate)
        {
            changed = isTabStopRecording || tabStopListener is not null;
            isTabStopRecording = false;
            tabStopRecordingGeneration++;
            listener = tabStopListener;
            tabStopListener = null;
            tabStopRoot = null;
            lastTabStopIdentity = null;
        }

        try
        {
            listener?.Dispose();
        }
        finally
        {
            if (changed)
            {
                PublishRecordingStateChanged();
            }
        }
    }

    private void ClearEventBuffer()
    {
        lock (recordingGate)
        {
            recordedEvents.Clear();
            droppedEventCount = 0;
            eventSequence = 0;
        }
    }

    private void ClearTabStopBuffer()
    {
        lock (recordingGate)
        {
            recordedTabStops.Clear();
            recordedTabStopIdentities.Clear();
            droppedTabStopCount = 0;
            tabStopSequence = 0;
            tabStopOrder = 0;
            lastTabStopIdentity = null;
        }
    }

    private RecordingBufferSnapshot<RecordedEvent> GetEventSnapshot()
    {
        lock (recordingGate)
        {
            return new RecordingBufferSnapshot<RecordedEvent>(
                recordedEvents.ToArray(), droppedEventCount);
        }
    }

    private RecordingBufferSnapshot<TabStopRecord> GetTabStopSnapshot()
    {
        lock (recordingGate)
        {
            return new RecordingBufferSnapshot<TabStopRecord>(
                recordedTabStops.ToArray(), droppedTabStopCount);
        }
    }

    private void PublishRecordingStateChanged() =>
        RecordingStateChanged?.Invoke(this, new RecordingStateChangedEventArgs(State));

    private void ReportRecordingFault(string operation, Exception exception)
    {
        Trace.TraceError("{0}: {1}", operation, exception);
        try
        {
            RecordingFaulted?.Invoke(this, new RecordingFaultedEventArgs(operation, exception));
        }
        catch (Exception handlerException)
        {
            Trace.TraceError("Recording fault handler failed: {0}", handlerException);
        }
    }

    private bool ReportRegistrationFailureIfPresent(
        IReadOnlyList<RecordedProperty> properties,
        string operation)
    {
        var registrationMessage = properties.FirstOrDefault(
            property => string.Equals(property.Name, "Message", StringComparison.Ordinal));
        if (registrationMessage is not null &&
            (registrationMessage.Value.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) ||
             registrationMessage.Value.Contains("rejected", StringComparison.OrdinalIgnoreCase)))
        {
            ReportRecordingFault(operation, new InvalidOperationException(registrationMessage.Value));
            return true;
        }

        return false;
    }

    private static RecordingBufferSnapshot<RecordedEvent> ReadEventFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The event record file was not found.", path);
        }

        if (file.Length > MaximumEventFileBytes)
        {
            throw new InvalidDataException("The event record file is too large.");
        }

        using var stream = file.OpenRead();
        using var text = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var reader = new JsonTextReader(text)
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 128,
        };
        var token = JToken.Load(reader, new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        });
        if (token is not JArray array)
        {
            throw new JsonSerializationException("An event record file must contain a JSON array.");
        }

        var start = Math.Max(0, array.Count - RecordingBufferLimits.Events);
        var items = new List<RecordedEvent>(array.Count - start);
        long sequence = 0;
        for (var index = start; index < array.Count; index++)
        {
            if (array[index] is not JObject item)
            {
                throw new JsonSerializationException(
                    string.Format(CultureInfo.InvariantCulture, "Event record {0} is not a JSON object.", index));
            }

            items.Add(CreateRecordedEvent(item, ++sequence));
        }

        return new RecordingBufferSnapshot<RecordedEvent>(items, start);
    }

    private static JsonSerializer CreateLegacySerializer() => JsonSerializer.Create(new JsonSerializerSettings
    {
        DateParseHandling = DateParseHandling.None,
        NullValueHandling = NullValueHandling.Include,
        ReferenceLoopHandling = ReferenceLoopHandling.Error,
        TypeNameHandling = TypeNameHandling.None,
    });

    private static RecordedEvent CreateRecordedEvent(JObject item, long sequence)
    {
        var eventId = item.GetValue("EventId", StringComparison.OrdinalIgnoreCase)?.Value<int>()
            ?? throw new JsonSerializationException("An event record is missing EventId.");
        var timeStamp = item.GetValue("TimeStamp", StringComparison.OrdinalIgnoreCase)?.Value<string>()
            ?? string.Empty;
        var properties = ReadEventProperties(
            item.GetValue("Properties", StringComparison.OrdinalIgnoreCase));
        var elementToken = item.GetValue("Element", StringComparison.OrdinalIgnoreCase);
        var element = elementToken switch
        {
            null => null,
            { Type: JTokenType.Null } => null,
            JObject elementObject => CreateRecordedElement(elementObject),
            _ => throw new JsonSerializationException("An event record Element must be a JSON object or null."),
        };

        return new RecordedEvent(
            sequence,
            eventId,
            EventType.GetInstance().GetNameById(eventId),
            timeStamp,
            element,
            properties)
        {
            LegacyJson = item.ToString(Formatting.None),
        };
    }

    private static IReadOnlyList<RecordedProperty> ReadEventProperties(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return [];
        }

        if (token is not JArray properties)
        {
            throw new JsonSerializationException("Event Properties must be a JSON array or null.");
        }

        var result = new List<RecordedProperty>(properties.Count);
        foreach (var tokenProperty in properties)
        {
            if (tokenProperty is not JObject property)
            {
                throw new JsonSerializationException("Each event property must be a JSON object.");
            }

            result.Add(new RecordedProperty(
                property.GetValue("Key", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? string.Empty,
                FormatJsonValue(property.GetValue("Value", StringComparison.OrdinalIgnoreCase))));
        }

        return result;
    }

    private static RecordedElement CreateRecordedElement(JObject element)
    {
        var rawProperties = element.GetValue("Properties", StringComparison.OrdinalIgnoreCase);
        var propertiesToken = rawProperties switch
        {
            null => null,
            { Type: JTokenType.Null } => null,
            JObject propertyObject => propertyObject,
            _ => throw new JsonSerializationException("Element Properties must be a JSON object or null."),
        };
        var properties = new List<RecordedProperty>();
        if (propertiesToken is not null)
        {
            foreach (var property in propertiesToken.Properties())
            {
                if (property.Value is not JObject propertyValue)
                {
                    throw new JsonSerializationException("Each element property must be a JSON object.");
                }

                properties.Add(new RecordedProperty(
                    propertyValue.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? string.Empty,
                    FormatJsonValue(propertyValue.GetValue("Value", StringComparison.OrdinalIgnoreCase))));
            }

            properties.Sort((left, right) =>
                StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        }

        var uniqueId = element.GetValue("UniqueId", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
        var name = ReadElementPropertyString(propertiesToken, PropertyType.UIA_NamePropertyId);
        var localizedControlType = ReadElementPropertyString(
            propertiesToken, PropertyType.UIA_LocalizedControlTypePropertyId);
        var controlTypeId = ReadElementPropertyInt(propertiesToken, PropertyType.UIA_ControlTypePropertyId);
        var controlType = !string.IsNullOrWhiteSpace(localizedControlType)
            ? localizedControlType
            : controlTypeId == 0 ? string.Empty : ControlType.GetInstance().GetNameById(controlTypeId);
        var automationId = ReadElementPropertyString(propertiesToken, PropertyType.UIA_AutomationIdPropertyId);
        var processId = ReadElementPropertyInt(propertiesToken, PropertyType.UIA_ProcessIdPropertyId);
        var runtimeId = ReadElementPropertyToken(propertiesToken, PropertyType.UIA_RuntimeIdPropertyId)
            ?.ToString(Formatting.None) ?? string.Empty;
        var glimpse = element.GetValue("Glimpse", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        var sender = !string.IsNullOrWhiteSpace(glimpse)
            ? glimpse
            : string.Format(CultureInfo.CurrentCulture, "{0} '{1}'", controlType, name).Trim();
        var identity = CreateElementIdentity(
            propertiesToken, processId, runtimeId, automationId, controlTypeId, name, uniqueId);

        return new RecordedElement(
            uniqueId,
            name,
            controlType,
            automationId,
            processId,
            runtimeId,
            sender,
            properties.ToArray())
        {
            Identity = identity,
        };
    }

    private static string CreateElementIdentity(
        JObject? properties,
        int processId,
        string runtimeId,
        string automationId,
        int controlTypeId,
        string name,
        int uniqueId)
    {
        if (!string.IsNullOrWhiteSpace(runtimeId))
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1}", processId, runtimeId);
        }

        var nativeWindow = ReadElementPropertyToken(properties, PropertyType.UIA_NativeWindowHandlePropertyId);
        var bounds = ReadElementPropertyToken(properties, PropertyType.UIA_BoundingRectanglePropertyId);
        return string.Join("|",
            processId.ToString(CultureInfo.InvariantCulture),
            nativeWindow?.ToString(Formatting.None) ?? string.Empty,
            controlTypeId.ToString(CultureInfo.InvariantCulture),
            automationId,
            name,
            bounds?.ToString(Formatting.None) ?? string.Empty,
            uniqueId.ToString(CultureInfo.InvariantCulture));
    }

    private static JToken? ReadElementPropertyToken(JObject? properties, int propertyId) =>
        (properties?.GetValue(propertyId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            as JObject)?.GetValue("Value", StringComparison.OrdinalIgnoreCase);

    private static string ReadElementPropertyString(JObject? properties, int propertyId)
    {
        var value = ReadElementPropertyToken(properties, propertyId);
        return value?.Type == JTokenType.String
            ? value.Value<string>() ?? string.Empty
            : FormatJsonValue(value);
    }

    private static int ReadElementPropertyInt(JObject? properties, int propertyId)
    {
        var value = ReadElementPropertyToken(properties, propertyId);
        return value?.Type == JTokenType.Integer ? value.Value<int>() : 0;
    }

    private static string FormatJsonValue(JToken? value)
    {
        if (value is null || value.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return string.Empty;
        }

        if (value is JValue scalar)
        {
            return Convert.ToString(scalar.Value, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        return value.ToString(Formatting.None);
    }
}
