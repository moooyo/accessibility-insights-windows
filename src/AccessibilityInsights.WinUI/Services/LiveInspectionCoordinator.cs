// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.UI.Dispatching;
using System.Diagnostics;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class LiveInspectionCoordinator : IDisposable
{
    private readonly ILiveInspectionService inspection;
    private readonly WorkbenchViewModel viewModel;
    private DispatcherQueue? dispatcher;
    private bool subscribed;

    public LiveInspectionCoordinator(ILiveInspectionService inspection, WorkbenchViewModel viewModel)
    {
        this.inspection = inspection;
        this.viewModel = viewModel;
    }

    public void Attach(DispatcherQueue owningDispatcher)
    {
        ArgumentNullException.ThrowIfNull(owningDispatcher);
        dispatcher = owningDispatcher;
        if (!subscribed)
        {
            inspection.LiveSelectionChanged += OnLiveSelectionChanged;
            inspection.LiveInspectionError += OnLiveInspectionError;
            subscribed = true;
        }
    }

    private void OnLiveSelectionChanged(object? sender, LiveSelectionChangedEventArgs args) =>
        Dispatch(() => viewModel.ApplyLiveSnapshot(args.Snapshot));

    private void OnLiveInspectionError(object? sender, LiveInspectionErrorEventArgs args) =>
        Dispatch(() => viewModel.ReportError(args.Exception));

    private void Dispatch(Action action)
    {
        var target = dispatcher;
        if (target is null)
        {
            Trace.TraceWarning("A live inspection update arrived before a DispatcherQueue was attached.");
            return;
        }

        if (target.HasThreadAccess)
        {
            action();
        }
        else if (!target.TryEnqueue(() => action()))
        {
            Trace.TraceWarning("A live inspection update could not be queued because the UI dispatcher is shutting down.");
        }
    }

    public void Dispose()
    {
        if (subscribed)
        {
            inspection.LiveSelectionChanged -= OnLiveSelectionChanged;
            inspection.LiveInspectionError -= OnLiveInspectionError;
            subscribed = false;
        }

        dispatcher = null;
    }
}
