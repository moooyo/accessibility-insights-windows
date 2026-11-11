// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using Axe.Windows.Actions;
using Axe.Windows.Actions.Enums;
using Axe.Windows.Core.Bases;
using Axe.Windows.Core.Enums;
using Axe.Windows.Core.Misc;
using Axe.Windows.Desktop.ColorContrastAnalyzer;
using Axe.Windows.Desktop.Settings;
using Axe.Windows.Desktop.Types;
using Axe.Windows.Desktop.UIAutomation.EventHandlers;
using Axe.Windows.Desktop.UIAutomation.TreeWalkers;
using Axe.Windows.Desktop.Utility;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Imaging;
using System.IO;

namespace AccessibilityInsights.Services;

public sealed class InspectionService : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private SelectAction? selector;
    private Guid? contextId;
    private Guid? recorderId;
    private bool disposed;
    private bool loaded;
    private bool selectorStarted;
    private A11yFileMode fileMode = A11yFileMode.Inspect;
    private readonly object eventLock = new();
    private readonly List<EventMessage> recordedEvents = [];
    private readonly HashSet<string> focusElements = [];
    private bool recordTabStops;

    public event Action<EventRow>? EventReceived;
    public event Action<TabStopRow>? TabStopReceived;

    public async Task<byte[]?> GetScreenshotAsync(bool capture) => await RunAsync(() =>
    {
        var id = RequireContext();
        if (capture)
        {
            if (loaded)
            {
                throw new InvalidOperationException("A loaded snapshot cannot capture a live screenshot.");
            }

            ScreenShotAction.CaptureScreenShot(id);
        }

        var bitmap = GetDataAction.GetElementDataContext(id).Screenshot;
        if (bitmap is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    });

    public async Task<DetectedColors> DetectColorsAsync() => await RunAsync(() =>
    {
        if (loaded)
        {
            throw new InvalidOperationException("Select a live element before detecting its colors.");
        }

        var element = GetDataAction.GetElementContext(RequireContext()).Element;
        using var bitmap = element.CaptureBitmap();
        var result = new BitmapCollection(bitmap, new ColorContrastConfigBuilder().Build()).RunColorContrastCalculation();
        var pair = result.MostLikelyColorPair ??
            throw new InvalidOperationException("Unable to determine colors for the selected element.");
        var first = pair.DarkerColor.DrawingColor;
        var second = pair.LighterColor.DrawingColor;
        return new DetectedColors(new(first.R, first.G, first.B), new(second.R, second.G, second.B),
            result.Confidence.ToString());
    });

    public async Task SaveEventsAsync(string path) => await RunAsync(() =>
    {
        StopRecordingCore();
        lock (eventLock)
        {
            if (recordedEvents.Count == 0)
            {
                throw new InvalidOperationException("Record events before saving an event file.");
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(recordedEvents, Formatting.Indented));
        }
    });

    public async Task<IReadOnlyList<EventRow>> LoadEventsAsync(string path) => await RunAsync(() =>
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024)
        {
            throw new InvalidDataException("Event files must not exceed 50 MB.");
        }

        var events = JsonConvert.DeserializeObject<List<EventMessage>>(File.ReadAllText(path)) ??
            throw new InvalidDataException("The event file must contain an event array.");
        if (events.Count > 10000 || events.Any(message => message is null))
        {
            foreach (var message in events) { message?.Dispose(); }
            throw new InvalidDataException("Event files must contain at most 10,000 non-null events.");
        }

        StopRecordingCore();
        lock (eventLock)
        {
            ClearEvents();
            recordedEvents.AddRange(events);
            return events.Select(ToRow).ToArray();
        }
    });

    public async Task StartSelectionAsync(bool byFocus, bool applicationScope) =>
        await RunAsync(() =>
        {
            StopRecordingCore();
            StopSelector();
            selector ??= SelectAction.GetDefaultInstance();
            selector.ClearSelectedContext();
            contextId = null;
            loaded = false;
            fileMode = A11yFileMode.Inspect;
            StartSelector(byFocus, applicationScope);
        });

    [MemberNotNull(nameof(selector))]
    private void StartSelector(bool byFocus, bool applicationScope)
    {
        selector ??= SelectAction.GetDefaultInstance();
        selector.IsFocusSelectOn = byFocus;
        selector.IsMouseSelectOn = !byFocus;
        selector.IntervalMouseSelector = 300;
        selector.Scope = applicationScope ? SelectionScope.App : SelectionScope.Element;
        selector.TreeViewMode = TreeViewMode.Control;
        if (!selectorStarted)
        {
            selector.Start();
            selectorStarted = true;
        }
    }

    private void StopSelector()
    {
        selector?.Stop();
        selectorStarted = false;
    }

    public async Task<InspectionSnapshot?> PollAsync(bool byFocus, bool applicationScope)
    {
        await gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await Task.Run(() =>
            {
                StartSelector(byFocus, applicationScope);

                if (!selector.Select() || !selector.SelectedElementContextId.HasValue)
                {
                    return null;
                }

                var selected = selector.SelectedElementContextId.Value;
                var context = GetDataAction.GetElementContext(selected);
                if (context.Element.ProcessId == Environment.ProcessId || context.Element.ProcessId <= 0)
                {
                    return null;
                }

                contextId = selected;
                loaded = false;
                fileMode = A11yFileMode.Inspect;
                CaptureAction.SetLiveModeDataContext(selected, TreeViewMode.Control);
                return BuildSnapshot(selected);
            });
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task PauseAsync()
    {
        await RunAsync(StopSelector);
    }

    public async Task<InspectionSnapshot> ScanAsync()
    {
        return await RunAsync(() =>
        {
            var id = RequireContext();
            StopSelector();
            if (loaded)
            {
                throw new InvalidOperationException("Select a live element before running automated checks.");
            }

            SetDataAction.ReleaseDataContext(id);
            CaptureAction.SetTestModeDataContext(id, DataContextMode.Test, TreeViewMode.Control);
            fileMode = A11yFileMode.Test;
            return BuildSnapshot(id);
        });
    }

    public async Task<IReadOnlyList<PropertyRow>> GetPropertiesAsync(int elementId)
    {
        return await RunAsync(() =>
        {
            var element = GetDataAction.GetA11yElementInDataContext(RequireContext(), elementId);
            return GetProperties(element);
        });
    }

    public async Task SaveAsync(string path)
    {
        await RunAsync(() =>
        {
            var id = RequireContext();
            StopSelector();
            SaveAction.SaveSnapshotZip(path, id, null, fileMode);
        });
    }

    public async Task<InspectionSnapshot> LoadAsync(string path)
    {
        return await RunAsync(() =>
        {
            StopRecordingCore();
            selector ??= SelectAction.GetDefaultInstance();
            StopSelector();
            var result = selector.SelectLoadedData(path, null);
            contextId = result.Item1;
            loaded = true;
            fileMode = result.Item2.Mode;
            return BuildSnapshot(result.Item1);
        });
    }

    public async Task StartRecordingAsync(bool tabStops = false)
    {
        await RunAsync(() =>
        {
            var id = RequireContext();
            if (loaded)
            {
                throw new InvalidOperationException("Select a live element before recording events.");
            }

            StopSelector();
            StopRecordingCore();
            lock (eventLock)
            {
                ClearEvents();
                focusElements.Clear();
                recordTabStops = tabStops;
            }

            recorderId = ListenAction.CreateInstance(ListenScope.Subtree, id, OnEvent);
            ListenAction.GetInstance(recorderId.Value).Start(
                tabStops ? [20005] : [20000, 20001, 20002, 20003, 20004, 20005, 20006, 20009, 20015, 20016, 20024, 20035],
                tabStops ? [] : [30005, 30008, 30010, 30022, 30045, 30070]);
        });
    }

    public async Task StopRecordingAsync() => await RunAsync(StopRecordingCore);

    private void OnEvent(EventMessage message)
    {
        TabStopRow? tabStop = null;
        EventRow row;
        lock (eventLock)
        {
            row = ToRow(message);
            if (recordTabStops && message.EventId == 20005 && message.Element is { } element)
            {
                var root = GetDataAction.GetElementContext(RequireContext()).Element;
                var ancestry = new DesktopElementAncestry(TreeViewMode.Control, element, true);
                var withinScope = false;
                for (var parent = element; parent is not null; parent = parent.Parent)
                {
                    if (parent.IsSameUIElement(root)) { withinScope = true; break; }
                }

                if (withinScope && focusElements.Add(element.RuntimeId))
                {
                    var bounds = element.BoundingRectangle;
                    tabStop = new(focusElements.Count, element.Name,
                        new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                }
            }

            if (recordedEvents.Count == 10000)
            {
                recordedEvents[0].Dispose();
                recordedEvents.RemoveAt(0);
            }

            recordedEvents.Add(message);
        }

        EventReceived?.Invoke(row);
        if (tabStop is not null) { TabStopReceived?.Invoke(tabStop); }
    }

    private static EventRow ToRow(EventMessage message) => new(
            message.TimeStamp,
            message.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            message.Element?.Name ?? string.Empty,
            string.Join("; ", message.Properties?.Select(property => $"{property.Key}: {property.Value}") ?? []));

    private void ClearEvents()
    {
        foreach (var message in recordedEvents) { message.Dispose(); }
        recordedEvents.Clear();
    }

    private void StopRecordingCore()
    {
        if (recorderId is { } id)
        {
            ListenAction.GetInstance(id).Stop();
            ListenAction.ReleaseInstance(id);
            recorderId = null;
        }
    }

    private Guid RequireContext() => contextId ??
        throw new InvalidOperationException("Select an element in another application first.");

    private static InspectionSnapshot BuildSnapshot(Guid id)
    {
        var context = GetDataAction.GetElementContext(id);
        var data = GetDataAction.GetElementDataContext(id);
        var root = data.RootElment ?? context.Element;
        var elements = new List<ElementRow>();
        var results = new List<RuleRow>();
        var seen = new HashSet<int>();
        var stack = new Stack<(A11yElement Element, int Depth)>();
        stack.Push((root, 0));
        while (stack.TryPop(out var entry))
        {
            var element = entry.Element;
            if (!seen.Add(element.UniqueId))
            {
                continue;
            }

            var rectangle = element.BoundingRectangle;
            elements.Add(new(element.UniqueId, element.Name, element.LocalizedControlType, entry.Depth,
                new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)));
            if (element.ScanResults?.Items is { } scans)
            {
                foreach (var scan in scans)
                {
                    foreach (var rule in scan.Items)
                    {
                        results.Add(new(rule.Status.ToString(), element.Name, rule.Description,
                            string.Join(Environment.NewLine, rule.Messages ?? []),
                            rule.Source, rule.HelpUrl?.Url ?? string.Empty, element.Glimpse));
                    }
                }
            }

            if (element.Children is { } children)
            {
                for (var index = children.Count - 1; index >= 0; index--)
                {
                    stack.Push((children[index], entry.Depth + 1));
                }
            }
        }

        var selected = data.Element;
        if (selected?.Properties is null)
        {
            throw new InvalidOperationException("The selected element is no longer available. Select it again.");
        }

        var selectedBounds = selected.BoundingRectangle;
        return new(selected.Name, GetProperties(selected), elements, results,
            data.ElementCounter?.UpperBoundExceeded == true,
            new(selectedBounds.X, selectedBounds.Y, selectedBounds.Width, selectedBounds.Height),
            selected.GetProcessName());
    }

    private static IReadOnlyList<PropertyRow> GetProperties(A11yElement element) =>
        element.Properties.Values.OrderBy(property => property.Name, StringComparer.CurrentCulture)
            .Select(property => new PropertyRow(property.Name, property.TextValue)).ToArray();

    private async Task RunAsync(Action action) => await RunAsync(() =>
    {
        action();
        return true;
    });

    private async Task<T> RunAsync<T>(Func<T> action)
    {
        await gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await Task.Run(action);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }

            await Task.Run(() =>
            {
                StopRecordingCore();
                StopSelector();
                SelectAction.ClearDefaultInstance();
                SetDataAction.ReleaseAll();
                lock (eventLock) { ClearEvents(); }
            });
            disposed = true;
        }
        finally
        {
            gate.Release();
        }
    }
}
