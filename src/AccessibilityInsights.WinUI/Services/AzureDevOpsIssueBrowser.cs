// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.IssueReporting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Globalization;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class AzureDevOpsIssueBrowser : IAzureDevOpsIssueBrowser
{
    private readonly AppStrings strings;

    public AzureDevOpsIssueBrowser(AppStrings strings)
    {
        this.strings = strings;
    }

    public async Task<AzureDevOpsIssueBrowserResult?> OpenAsync(
        Uri issuePreviewUri,
        int zoomLevel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuePreviewUri);
        cancellationToken.ThrowIfCancellationRequested();

        int currentZoom = Math.Clamp(zoomLevel, 25, 1000);
        int? issueId = null;
        var webView = new WebView2
        {
            MinWidth = 780,
            MinHeight = 560,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(webView, strings.Get("AzureBoardsIssueBrowserAutomationName"));

        var zoomLabel = new TextBlock
        {
            Text = string.Create(CultureInfo.CurrentCulture, $"{currentZoom}%"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var zoomOut = new Button { Content = strings.Get("AzureBoardsZoomOut") };
        var zoomIn = new Button { Content = strings.Get("AzureBoardsZoomIn") };
        AutomationProperties.SetName(zoomOut, strings.Get("AzureBoardsZoomOut"));
        AutomationProperties.SetName(zoomIn, strings.Get("AzureBoardsZoomIn"));

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { zoomOut, zoomLabel, zoomIn },
        };
        var content = new Grid { RowSpacing = 8 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(toolbar);
        Grid.SetRow(webView, 1);
        content.Children.Add(webView);

        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = strings.Get("AzureBoardsIssueBrowserTitle"),
            CloseButtonText = strings.Get("Cancel"),
            Content = content,
        };

        async Task ApplyZoomAsync()
        {
            zoomLabel.Text = string.Create(CultureInfo.CurrentCulture, $"{currentZoom}%");
            if (webView.CoreWebView2 is not null)
            {
                string script = string.Create(
                    CultureInfo.InvariantCulture,
                    $"document.documentElement.style.zoom='{currentZoom}%';");
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
        }

        zoomOut.Click += async (_, _) =>
        {
            currentZoom = Math.Max(25, currentZoom - 25);
            await ApplyZoomAsync();
        };
        zoomIn.Click += async (_, _) =>
        {
            currentZoom = Math.Min(1000, currentZoom + 25);
            await ApplyZoomAsync();
        };

        void CheckForCreatedIssue()
        {
            Uri? current = webView.Source;
            if (current is null ||
                !string.Equals(current.Host, issuePreviewUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (TryGetIssueId(current, out int parsed))
            {
                issueId = parsed;
                dialog.Hide();
            }
        }

        webView.NavigationCompleted += async (_, args) =>
        {
            if (args.IsSuccess)
            {
                await ApplyZoomAsync();
                CheckForCreatedIssue();
            }
        };

        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.HistoryChanged += (_, _) => CheckForCreatedIssue();
        webView.Source = issuePreviewUri;

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            App.Window.DispatcherQueue.TryEnqueue(dialog.Hide));
        await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return issueId.HasValue
            ? new AzureDevOpsIssueBrowserResult(issueId.Value, currentZoom)
            : null;
    }

    private static bool TryGetIssueId(Uri uri, out int issueId)
    {
        string value = uri.PathAndQuery;
        string[] markers = ["_queries/edit/", "_workitems/edit/", "_workitems?id="];
        foreach (string marker in markers)
        {
            int markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            int start = markerIndex + marker.Length;
            int length = 0;
            while (start + length < value.Length && char.IsDigit(value[start + length]))
            {
                length++;
            }

            if (length > 0 &&
                int.TryParse(value.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture, out issueId))
            {
                return true;
            }
        }

        issueId = default;
        return false;
    }
}
