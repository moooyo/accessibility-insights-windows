// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Axe.Windows.Actions;
using Axe.Windows.Actions.Enums;
using Axe.Windows.Core.Bases;
using Axe.Windows.Core.Enums;
using Axe.Windows.Core.Results;
using Axe.Windows.Desktop.Settings;
using Axe.Windows.Desktop.UIAutomation;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using UIAutomationClient;

namespace AccessibilityInsights.Core;

public sealed partial class InspectionService : IInspectionService
{
    private readonly BlockingCollection<Action> work = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread worker;
    private Guid? currentContext;
    private int disposeRequested;
    private bool disposed => Volatile.Read(ref disposeRequested) != 0;

    public InspectionService()
    {
        worker = new Thread(Run) { IsBackground = true, Name = "Accessibility Insights UIA" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }

    public IReadOnlyList<TargetWindow> GetWindows()
    {
        var windows = new List<TargetWindow>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && process.MainWindowHandle != 0 &&
                        !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        windows.Add(new TargetWindow(process.Id, process.MainWindowHandle, process.MainWindowTitle));
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    Trace.TraceWarning("Could not enumerate a process window: {0}", exception.Message);
                }
            }
        }

        return windows.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task<InspectionSnapshot> CaptureAsync(nint window, bool runTests) => Enqueue(() =>
    {
        if (window == 0)
        {
            throw new ArgumentException("Select a target window.", nameof(window));
        }

        StopTracking();
        StopRecording();
        ReleaseContext();
        var automation = new CUIAutomation8();
        var nativeElement = automation.ElementFromHandle(window);
        var element = new DesktopElement(nativeElement);
        AxePropertyCompatibility.PrepareForCleanup(element);
        var selection = SelectAction.GetDefaultInstance();
        selection.Scope = SelectionScope.Element;
        selection.SetCandidateElement(element);
        if (!selection.Select() || selection.SelectedElementContextId is not Guid contextId)
        {
            throw new InvalidOperationException("The selected window is no longer available.");
        }

        currentContext = contextId;
        if (runTests)
        {
            CaptureAction.SetTestModeDataContext(contextId, DataContextMode.Test, TreeViewMode.Control);
            ScreenShotAction.CaptureScreenShot(contextId);
        }
        else
        {
            CaptureAction.SetLiveModeDataContext(contextId, TreeViewMode.Control);
        }

        return CreateSnapshot(contextId, runTests ? InspectionKind.Test : InspectionKind.Live);
    });

    public Task<InspectionSnapshot> OpenAsync(string path) => Enqueue(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        StopTracking();
        ReleaseContext();
        var loaded = SelectAction.GetDefaultInstance().SelectLoadedData(path, null);
        currentContext = loaded.Item1;
        return CreateSnapshot(loaded.Item1, InspectionKind.Loaded);
    });

    public Task SaveAsync(string path) => Enqueue(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var context = currentContext ?? throw new InvalidOperationException("Capture or open a snapshot first.");
        var mode = GetDataAction.GetElementDataContext(context).Mode == DataContextMode.Test
            ? A11yFileMode.Test : A11yFileMode.Inspect;
        SaveAction.SaveSnapshotZip(path, context, null, mode);
        return true;
    });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeRequested, 1) == 0)
        {
            try
            {
                work.CompleteAdding();
            }
            catch (ObjectDisposedException) when (stopped.Task.IsCompleted)
            {
            }
        }

        await stopped.Task.ConfigureAwait(false);
    }

    private Task<T> Enqueue<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private void Run()
    {
        try
        {
            foreach (var action in work.GetConsumingEnumerable())
            {
                action();
            }

            StopTracking();
            StopRecording();
            ReleaseContext();
            SelectAction.ClearDefaultInstance();
            DataManager.ClearDefaultInstance();
            stopped.SetResult();
        }
        catch (Exception exception)
        {
            stopped.SetException(exception);
        }
        finally
        {
            work.Dispose();
        }
    }

    private void ReleaseContext()
    {
        StopRecording();
        if (currentContext is Guid context)
        {
            SelectAction.GetDefaultInstance().ClearSelectedContext();
            SetDataAction.ReleaseElementContext(context);
            currentContext = null;
        }
    }

    private static InspectionSnapshot CreateSnapshot(Guid context, InspectionKind kind = InspectionKind.Live)
    {
        var data = GetDataAction.GetElementDataContext(context);
        return new InspectionSnapshot(context, CopyElement(data.Element), data.ElementCounter.UpperBoundExceeded)
        {
            Kind = kind,
        };
    }

    private static ElementSnapshot CopyElement(A11yElement element) => new()
    {
        Id = element.UniqueId,
        Name = element.Name ?? string.Empty,
        ControlType = element.LocalizedControlType ?? string.Empty,
        Bounds = new ScreenRectangle(
            element.BoundingRectangle.X,
            element.BoundingRectangle.Y,
            element.BoundingRectangle.Width,
            element.BoundingRectangle.Height),
        ProcessId = element.ProcessId,
        IsKeyboardFocusable = element.IsKeyboardFocusable,
        Properties = CopyProperties(element),
        Children = element.Children?.Select(CopyElement).ToArray() ?? [],
        Failures = element.ScanResults?.Items.SelectMany(result => result.Items)
            .Where(rule => rule.Status == ScanStatus.Fail)
            .Select(rule => new RuleFailure(element.UniqueId, element.Name ?? string.Empty,
                rule.Description, string.Join(Environment.NewLine, rule.Messages), rule.HelpUrl?.Url ?? string.Empty))
            .ToArray() ?? [],
    };

    private static ElementProperty[] CopyProperties(A11yElement element)
    {
        AxePropertyCompatibility.PrepareForCleanup(element);
        return element.Properties?.Values
            .OrderBy(property => property.Name, StringComparer.CurrentCulture)
            .Select(property => new ElementProperty(property.Name, property.TextValue)).ToArray() ?? [];
    }

    partial void StopTracking();
    partial void StopRecording();
}
