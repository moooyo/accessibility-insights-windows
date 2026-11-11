// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Axe.Windows.Actions;
using Axe.Windows.Actions.Enums;
using Axe.Windows.Core.Bases;
using Axe.Windows.Core.Enums;
using Axe.Windows.Desktop.UIAutomation;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UIAutomationClient;

namespace AccessibilityInsights.Core;

public sealed partial class InspectionService : ILiveInspectionService
{
    private const int SelectionPollIntervalMilliseconds = 100;
    private System.Threading.Timer? trackingTimer;
    private SelectAction? liveSelection;
    private NativeHighlightService? highlighter;
    private volatile bool trackingActive;
    private int trackingPollQueued;
    private long trackingGeneration;

    public event EventHandler<LiveSelectionChangedEventArgs>? LiveSelectionChanged;
    public event EventHandler<LiveInspectionErrorEventArgs>? LiveInspectionError;

    public Task StartLiveInspectionAsync(LiveInspectionOptions options) => Enqueue(() =>
    {
        options.Validate();
        StopTracking();
        ReleaseContext();

        liveSelection = SelectAction.GetDefaultInstance();
        ApplyOptions(liveSelection, options);
        Interlocked.Increment(ref trackingGeneration);
        trackingActive = true;
        liveSelection.Start();
        trackingTimer = new System.Threading.Timer(
            QueueTrackingPoll,
            null,
            SelectionPollIntervalMilliseconds,
            SelectionPollIntervalMilliseconds);
        return true;
    });

    public Task StopLiveInspectionAsync() => Enqueue(() =>
    {
        StopTracking();
        return true;
    });

    public Task SetLiveInspectionPausedAsync(bool paused) => Enqueue(() =>
    {
        if (!trackingActive || liveSelection is null)
        {
            throw new InvalidOperationException("Live inspection is not running.");
        }

        if (paused)
        {
            liveSelection.PauseUIATreeTracker();
        }
        else
        {
            liveSelection.ResumeUIATreeTracker();
        }

        return true;
    });

    public Task ConfigureLiveInspectionAsync(LiveInspectionOptions options) => Enqueue(() =>
    {
        options.Validate();
        if (!trackingActive || liveSelection is null)
        {
            throw new InvalidOperationException("Live inspection is not running.");
        }

        var scope = ToAxeScope(options.Scope);
        Interlocked.Increment(ref trackingGeneration);
        var scopeChanged = liveSelection.Scope != scope;
        var wasPaused = liveSelection.IsPaused;
        if (!wasPaused)
        {
            liveSelection.Stop();
        }

        ApplyOptions(liveSelection, options);
        if (scopeChanged)
        {
            if (wasPaused)
            {
                liveSelection.ResumeUIATreeTracker();
                liveSelection.Stop();
            }

            liveSelection.ClearSelectedContext();
            currentContext = null;
            highlighter?.Hide();

            if (wasPaused)
            {
                liveSelection.PauseUIATreeTracker();
            }
        }

        if (!wasPaused)
        {
            liveSelection.Start();
        }

        return true;
    });

    public Task<InspectionSnapshot?> NavigateAsync(Guid contextId, TreeNavigationDirection direction) => Enqueue(() =>
    {
        if (currentContext != contextId || !GetDataAction.ExistElementContext(contextId))
        {
            return null;
        }

        var data = GetDataAction.GetElementDataContext(contextId);
        if (data.Mode != DataContextMode.Live)
        {
            throw new InvalidOperationException("Tree navigation is only available for live inspection data.");
        }

        var selection = liveSelection ?? SelectAction.GetDefaultInstance();
        switch (direction)
        {
            case TreeNavigationDirection.Parent:
                selection.TreeTracker.MoveToParent();
                break;
            case TreeNavigationDirection.FirstChild:
                selection.TreeTracker.MoveToFirstChild();
                break;
            case TreeNavigationDirection.LastChild:
                selection.TreeTracker.MoveToLastChild();
                break;
            case TreeNavigationDirection.NextSibling:
                selection.TreeTracker.MoveToNextSibling();
                break;
            case TreeNavigationDirection.PreviousSibling:
                selection.TreeTracker.MoveToPreviousSibling();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (selection.SelectedElementContextId is not Guid selectedContext)
        {
            return null;
        }

        currentContext = selectedContext;
        try
        {
            AxePropertyCompatibility.PrepareForCleanup(GetDataAction.GetElementContext(selectedContext).Element);
            CaptureAction.SetLiveModeDataContext(selectedContext, TreeViewMode.Control);
            return CreateSnapshot(selectedContext, InspectionKind.Live);
        }
        catch
        {
            selection.ClearSelectedContext();
            currentContext = null;
            highlighter?.Hide();
            throw;
        }
    });

    public Task<ElementDetails?> GetElementDetailsAsync(Guid contextId, int elementId) => Enqueue(() =>
    {
        if (currentContext != contextId || !GetDataAction.ExistElementContext(contextId))
        {
            return null;
        }

        var data = GetDataAction.GetElementDataContext(contextId);
        var element = data.Mode == DataContextMode.Live
            ? GetDataAction.GetA11yElementWithLiveData(contextId, elementId)
            : GetDataAction.GetA11yElementInDataContext(contextId, elementId);
        return element is null ? null : CopyDetails(contextId, element, data.Mode == DataContextMode.Live);
    });

    public Task<bool> HighlightAsync(Guid contextId, int elementId) => Enqueue(() =>
    {
        if (currentContext != contextId || !GetDataAction.ExistElementContext(contextId))
        {
            return false;
        }

        var data = GetDataAction.GetElementDataContext(contextId);
        if (data.Mode == DataContextMode.Load)
        {
            highlighter?.Hide();
            return false;
        }

        var element = GetDataAction.GetA11yElementInDataContext(contextId, elementId);
        if (element is null || element.ProcessId == Environment.ProcessId || element.BoundingRectangle.IsEmpty)
        {
            highlighter?.Hide();
            return false;
        }

        highlighter ??= new NativeHighlightService();
        highlighter.Show(ToScreenRectangle(element));
        return true;
    });

    public Task ClearHighlightAsync() => Enqueue(() =>
    {
        highlighter?.Hide();
        return true;
    });

    public Task<InspectionSnapshot> TestCurrentAsync(Guid contextId) => Enqueue(() =>
    {
        if (currentContext != contextId || !GetDataAction.ExistElementContext(contextId))
        {
            throw new InvalidOperationException("The selected live element is no longer available.");
        }

        StopTracking();
        var source = GetDataAction.GetElementDataContext(contextId);
        if (source.Mode != DataContextMode.Live)
        {
            throw new InvalidOperationException("Automated tests require a live inspection context.");
        }

        var testElement = CloneForTest(source.Element);
        var selection = SelectAction.GetDefaultInstance();
        selection.ClearSelectedContext();
        currentContext = null;
        try
        {
            selection.SetCandidateElement(testElement);
            if (!selection.Select() || selection.SelectedElementContextId is not Guid testContext)
            {
                throw new InvalidOperationException("The selected live element is no longer available.");
            }

            currentContext = testContext;
            CaptureAction.SetTestModeDataContext(testContext, DataContextMode.Test, TreeViewMode.Control);
            ScreenShotAction.CaptureScreenShot(testContext);
            return CreateSnapshot(testContext, InspectionKind.Test);
        }
        catch
        {
            selection.ClearSelectedContext();
            currentContext = null;
            throw;
        }
    });

    private void QueueTrackingPoll(object? state)
    {
        if (!trackingActive || Interlocked.Exchange(ref trackingPollQueued, 1) != 0)
        {
            return;
        }

        Task<LivePollResult?> poll;
        try
        {
            poll = Enqueue(PollTracking);
        }
        catch (ObjectDisposedException)
        {
            Interlocked.Exchange(ref trackingPollQueued, 0);
            return;
        }

        _ = poll.ContinueWith(
            completed =>
            {
                Interlocked.Exchange(ref trackingPollQueued, 0);
                if (completed.IsFaulted)
                {
                    RaiseLiveInspectionError(completed.Exception!.GetBaseException());
                }
                else if (completed.Result is LivePollResult result &&
                    result.Generation == Volatile.Read(ref trackingGeneration) &&
                    trackingActive)
                {
                    RaiseLiveSelectionChanged(result.Snapshot);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private LivePollResult? PollTracking()
    {
        if (!trackingActive || liveSelection is null || liveSelection.IsPaused || !liveSelection.Select())
        {
            return null;
        }

        var generation = Volatile.Read(ref trackingGeneration);
        if (liveSelection.SelectedElementContextId is not Guid selectedContext)
        {
            return null;
        }

        var elementContext = GetDataAction.GetElementContext(selectedContext);
        if (elementContext?.Element is null || elementContext.Element.ProcessId == Environment.ProcessId)
        {
            liveSelection.ClearSelectedContext();
            currentContext = null;
            highlighter?.Hide();
            return null;
        }

        currentContext = selectedContext;
        try
        {
            AxePropertyCompatibility.PrepareForCleanup(elementContext.Element);
            CaptureAction.SetLiveModeDataContext(selectedContext, TreeViewMode.Control);
            return new LivePollResult(generation, CreateSnapshot(selectedContext, InspectionKind.Live));
        }
        catch
        {
            liveSelection.ClearSelectedContext();
            currentContext = null;
            highlighter?.Hide();
            throw;
        }
    }

    private static void ApplyOptions(SelectAction selection, LiveInspectionOptions options)
    {
        selection.Scope = ToAxeScope(options.Scope);
        selection.IsMouseSelectOn = (options.Methods & LiveSelectionMethods.Mouse) != 0;
        selection.IsFocusSelectOn = (options.Methods & LiveSelectionMethods.KeyboardFocus) != 0;
        selection.IntervalMouseSelector = options.MouseSelectionDelayMilliseconds;
        selection.TreeViewMode = TreeViewMode.Control;
    }

    private static SelectionScope ToAxeScope(LiveSelectionScope scope) => scope switch
    {
        LiveSelectionScope.Element => SelectionScope.Element,
        LiveSelectionScope.Application => SelectionScope.App,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    private static DesktopElement CloneForTest(A11yElement element)
    {
        if (element.PlatformObject is not IUIAutomationElement nativeElement)
        {
            throw new InvalidOperationException("The selected live element is no longer available.");
        }

        var automation = new CUIAutomation8();
        var request = automation.CreateCacheRequest();
        try
        {
            var clone = nativeElement.BuildUpdatedCache(request);
            var result = new DesktopElement(clone);
            AxePropertyCompatibility.PrepareForCleanup(result);
            return result;
        }
        finally
        {
            if (Marshal.IsComObject(request))
            {
                Marshal.ReleaseComObject(request);
            }
        }
    }

    private static ElementDetails CopyDetails(Guid contextId, A11yElement element, bool isLive) => new(
        contextId,
        element.UniqueId,
        CopyProperties(element),
        element.Patterns?
            .OrderBy(pattern => pattern.Name, StringComparer.CurrentCulture)
            .Select(pattern => new ElementPattern(
                pattern.Id,
                pattern.Name ?? string.Empty,
                pattern.Properties?
                    .OrderBy(property => property.Name, StringComparer.CurrentCulture)
                    .Select(property => new ElementProperty(property.Name, property.NodeValue ?? string.Empty))
                    .ToArray() ?? []))
            .ToArray() ?? [],
        ToScreenRectangle(element),
        element.ProcessId,
        element.IsKeyboardFocusable,
        isLive);

    private static ScreenRectangle ToScreenRectangle(A11yElement element) => new(
        element.BoundingRectangle.X,
        element.BoundingRectangle.Y,
        element.BoundingRectangle.Width,
        element.BoundingRectangle.Height);

    private void RaiseLiveSelectionChanged(InspectionSnapshot snapshot)
    {
        var handlers = LiveSelectionChanged;
        if (handlers is null)
        {
            return;
        }

        var args = new LiveSelectionChangedEventArgs(snapshot);
        foreach (EventHandler<LiveSelectionChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                Trace.TraceError("Live selection subscriber failed: {0}", exception);
            }
        }
    }

    private void RaiseLiveInspectionError(Exception exception)
    {
        var handlers = LiveInspectionError;
        if (handlers is null)
        {
            Trace.TraceError("Live inspection failed: {0}", exception);
            return;
        }

        var args = new LiveInspectionErrorEventArgs(exception);
        foreach (EventHandler<LiveInspectionErrorEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception subscriberException)
            {
                Trace.TraceError("Live inspection error subscriber failed: {0}", subscriberException);
            }
        }
    }

    partial void StopTracking()
    {
        Interlocked.Increment(ref trackingGeneration);
        trackingActive = false;
        trackingTimer?.Dispose();
        trackingTimer = null;

        if (liveSelection is not null)
        {
            if (liveSelection.IsPaused)
            {
                liveSelection.ResumeUIATreeTracker();
            }

            liveSelection.Stop();
        }

        highlighter?.Hide();
        if (disposed)
        {
            highlighter?.Dispose();
            highlighter = null;
        }
    }

    private sealed record LivePollResult(long Generation, InspectionSnapshot Snapshot);
}
