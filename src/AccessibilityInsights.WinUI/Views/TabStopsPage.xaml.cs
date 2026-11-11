// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class TabStopsPage : Page
{
    public RecordingViewModel ViewModel { get; } = App.Services.GetRequiredService<RecordingViewModel>();

    public TabStopsPage()
    {
        InitializeComponent();
        ViewModel.TabStops.CollectionChanged += OnTabStopsChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public static Visibility InvertBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    private void OnTabStopsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is { Count: > 0 })
        {
            TabStopList.ScrollIntoView(args.NewItems[args.NewItems.Count - 1]);
        }
        else if (ViewModel.SelectedTabStop is not null)
        {
            TabStopList.ScrollIntoView(ViewModel.SelectedTabStop);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RecordingViewModel.SelectedTabStop) &&
            ViewModel.SelectedTabStop is not null)
        {
            TabStopList.ScrollIntoView(ViewModel.SelectedTabStop);
        }
    }
}
