// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.IssueReporting;
using AccessibilityInsights_WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AccessibilityInsights_WinUI;

public sealed partial class MainPage : Page
{
    public WorkbenchViewModel ViewModel { get; } = App.Services.GetRequiredService<WorkbenchViewModel>();

    public MainPage()
    {
        InitializeComponent();
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItemContainer?.Tag as string) switch
        {
            "Test" => typeof(TestPage),
            "Contrast" => typeof(ContrastPage),
            "Events" => typeof(EventsPage),
            "TabStops" => typeof(TabStopsPage),
            "Issues" => typeof(IssueReportingPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(InspectPage),
        };
        ContentFrame.Navigate(page);
    }

    public void NavigateSection(string section)
    {
        Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>()
            .Single(item => string.Equals(item.Tag as string, section, StringComparison.Ordinal));
    }

    public void NavigateIssue(IssueInformation issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>()
            .Single(item => string.Equals(item.Tag as string, "Issues", StringComparison.Ordinal));
        ContentFrame.Navigate(typeof(IssueReportingPage), issue);
    }
}
