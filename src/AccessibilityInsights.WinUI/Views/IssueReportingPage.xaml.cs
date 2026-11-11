// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.IssueReporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class IssueReportingPage : Page
{
    private bool initialized;

    public IssueReportingViewModel ViewModel { get; } =
        App.Services.GetRequiredService<IssueReportingViewModel>();

    public IssueReportingPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static bool Not(bool value) => !value;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IssueInformation issue)
        {
            ViewModel.SetPendingIssue(issue);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await ViewModel.InitializeAsync();
    }

    private void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedProject is not null &&
            ViewModel.LoadTeamsCommand.CanExecute(null))
        {
            ViewModel.LoadTeamsCommand.Execute(null);
        }
    }
}
