// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AccessibilityInsights.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly Func<string, string> resource;

    public MainPageViewModel(Func<string, string> resource)
    {
        this.resource = resource;
        SelectedElementName = resource("NoElementSelected");
        Status = resource("InitialStatus");
        SmallTextResult = LargeTextResult = NonTextResult = resource("PassResult");
    }

    public ObservableCollection<PropertyRow> Properties { get; } = [];
    public ObservableCollection<ElementRow> Elements { get; } = [];
    public ObservableCollection<RuleRow> Results { get; } = [];
    public ObservableCollection<EventRow> Events { get; } = [];
    public ObservableCollection<TabStopRow> TabStops { get; } = [];
    public ElementBounds SelectedBounds { get; private set; }
    public string SelectedProcessName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedElementName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsSelecting { get; set; }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial string Foreground { get; set; } = "#000000";

    [ObservableProperty]
    public partial string Background { get; set; } = "#FFFFFF";

    [ObservableProperty]
    public partial string ContrastRatio { get; set; } = "21:1";

    [ObservableProperty]
    public partial string SmallTextResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LargeTextResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NonTextResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ContrastError { get; set; } = string.Empty;

    partial void OnForegroundChanged(string value) => UpdateContrast();
    partial void OnBackgroundChanged(string value) => UpdateContrast();

    public void ClearSelection()
    {
        HasSelection = false;
        SelectedElementName = resource("NoElementSelected");
        SelectedProcessName = string.Empty;
        SelectedBounds = default;
        Properties.Clear();
        Elements.Clear();
        Results.Clear();
        TabStops.Clear();
        Status = resource("InitialStatus");
    }

    public void UpdateSnapshot(InspectionSnapshot snapshot)
    {
        SelectedElementName = snapshot.Name;
        SelectedBounds = snapshot.Bounds;
        SelectedProcessName = snapshot.ProcessName;
        HasSelection = true;
        Replace(Properties, snapshot.Properties);
        Replace(Elements, snapshot.Elements);
        Replace(Results, snapshot.Results.OrderBy(row => row.Status == "Fail" ? 0 : 1));
        Status = snapshot.IsTruncated
            ? resource("IncompleteResults")
            : string.Format(CultureInfo.CurrentCulture, resource("ScanSummary"), snapshot.Elements.Count,
                snapshot.Results.Count, snapshot.Results.Count(row => row.Status == "Fail"));
    }

    public static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private void UpdateContrast()
    {
        if (!RgbColor.TryParse(Foreground, out var foreground) ||
            !RgbColor.TryParse(Background, out var background))
        {
            ContrastError = resource("InvalidColors");
            ContrastRatio = SmallTextResult = LargeTextResult = NonTextResult = string.Empty;
            return;
        }

        ContrastError = string.Empty;
        var ratio = ContrastCalculator.CalculateRatio(foreground, background);
        ContrastRatio = $"{(Math.Truncate(ratio * 1000) / 1000).ToString("0.###", CultureInfo.InvariantCulture)}:1";
        SmallTextResult = resource(ratio >= 4.5 ? "PassResult" : "FailResult");
        LargeTextResult = NonTextResult = resource(ratio >= 3 ? "PassResult" : "FailResult");
    }
}
