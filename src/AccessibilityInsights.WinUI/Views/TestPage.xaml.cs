// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.IssueReporting;
using AccessibilityInsights_WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class TestPage : Page
{
    public WorkbenchViewModel ViewModel { get; } = App.Services.GetRequiredService<WorkbenchViewModel>();
    private readonly AppStrings strings = App.Services.GetRequiredService<AppStrings>();

    public TestPage() => InitializeComponent();

    private async void OnHelpClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            var address = (string)((HyperlinkButton)sender).Tag;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(strings.Get("InvalidHelpAddress"));
            }

            if (!await Windows.System.Launcher.LaunchUriAsync(uri))
            {
                throw new InvalidOperationException(strings.Get("HelpOpenFailed"));
            }
        }
        catch (Exception exception)
        {
            ViewModel.ReportError(exception);
        }
    }

    private void OnFileIssueClicked(object sender, RoutedEventArgs args)
    {
        if (((Button)sender).DataContext is not RuleFailure failure)
        {
            return;
        }

        Uri.TryCreate(failure.HelpUrl, UriKind.Absolute, out Uri? helpUri);
        App.Window.Shell.NavigateIssue(new IssueInformation
        {
            IssueType = IssueType.SingleFailure,
            InternalGuid = Guid.NewGuid(),
            ProcessName = ViewModel.SelectedWindow?.Title,
            Glimpse = failure.ElementName,
            RuleSource = "Accessibility",
            RuleDescription = failure.Description,
            TestMessages = failure.Details,
            HelpUri = helpUri,
        });
    }
}
