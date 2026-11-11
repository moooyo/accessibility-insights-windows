// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class EventsPage : Page
{
    public RecordingViewModel ViewModel { get; } = App.Services.GetRequiredService<RecordingViewModel>();

    public EventsPage()
    {
        InitializeComponent();
        ViewModel.Events.CollectionChanged += OnEventsChanged;
        Loaded += OnLoaded;
    }

    public static Visibility InvertBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        ScopePicker.SelectedIndex = (int)ViewModel.SelectedScope;
        if (ViewModel.RefreshConfigurationCommand.CanExecute(null))
        {
            ViewModel.RefreshConfigurationCommand.Execute(null);
        }
    }

    private void OnScopeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ScopePicker.SelectedItem is ComboBoxItem item &&
            item.Tag is string value &&
            Enum.TryParse<RecordingScope>(value, out var scope))
        {
            ViewModel.SelectedScope = scope;
        }
    }

    private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is { Count: > 0 })
        {
            EventList.ScrollIntoView(args.NewItems[args.NewItems.Count - 1]);
        }
    }
}
