// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AccessibilityInsights.Core.IssueReporting;

public partial class IssueReportingViewModel : ObservableObject
{
    private readonly IIssueReportingService issueReporting;
    private readonly Func<string, string> getString;
    private bool applyingSelection;

    public IssueReportingViewModel(IIssueReportingService issueReporting, Func<string, string> getString)
    {
        this.issueReporting = issueReporting ?? throw new ArgumentNullException(nameof(issueReporting));
        this.getString = getString ?? throw new ArgumentNullException(nameof(getString));
    }

    public ObservableCollection<AzureDevOpsProject> Projects { get; } = [];
    public ObservableCollection<AzureDevOpsTeam> Teams { get; } = [];
    public IReadOnlyList<string> ReporterNames => [.. issueReporting.Reporters.Select(reporter => reporter.ServiceName)];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectAzureBoardsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadTeamsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(FileIssueCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial int SelectedReporterIndex { get; set; } = -1;

    [ObservableProperty]
    public partial bool IsGitHubSelected { get; set; }

    [ObservableProperty]
    public partial bool IsAzureBoardsSelected { get; set; }

    [ObservableProperty]
    public partial string GitHubRepositoryLink { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AzureBoardsServerLink { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadTeamsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigurationCommand))]
    public partial AzureDevOpsProject? SelectedProject { get; set; }

    [ObservableProperty]
    public partial AzureDevOpsTeam? SelectedTeam { get; set; }

    [ObservableProperty]
    public partial bool IsAzureBoardsConnected { get; set; }

    [ObservableProperty]
    public partial string AccountDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountEmail { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FileIssueCommand))]
    public partial IssueInformation? PendingIssue { get; set; }

    [ObservableProperty]
    public partial IssueReportResult? LastResult { get; set; }

    public bool HasPendingIssue => PendingIssue is not null;

    partial void OnPendingIssueChanged(IssueInformation? value) => OnPropertyChanged(nameof(HasPendingIssue));

    partial void OnSelectedReporterIndexChanged(int value)
    {
        if (applyingSelection || value < 0 || value >= issueReporting.Reporters.Count)
        {
            return;
        }

        IIssueReporter reporter = issueReporting.Reporters[value];
        issueReporting.Select(reporter.StableIdentifier);
        ApplyReporter(reporter);
        FileIssueCommand.NotifyCanExecuteChanged();
        SaveConfigurationCommand.NotifyCanExecuteChanged();
        ConnectAzureBoardsCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            await issueReporting.InitializeAsync().ConfigureAwait(true);
            IIssueReporter? selected = issueReporting.SelectedReporter;
            if (selected is null && issueReporting.Reporters.Count > 0)
            {
                selected = issueReporting.Reporters[0];
                issueReporting.Select(selected.StableIdentifier);
            }

            applyingSelection = true;
            try
            {
                SelectedReporterIndex = selected is null ? -1 : issueReporting.Reporters.IndexOf(selected);
            }
            finally
            {
                applyingSelection = false;
            }

            if (selected is not null)
            {
                ApplyReporter(selected);
            }
            ConnectAzureBoardsCommand.NotifyCanExecuteChanged();
            LoadTeamsCommand.NotifyCanExecuteChanged();
            SaveConfigurationCommand.NotifyCanExecuteChanged();
            FileIssueCommand.NotifyCanExecuteChanged();
            Status = getString("IssueReportingLoaded");
        }).ConfigureAwait(true);
    }

    public void SetPendingIssue(IssueInformation? issue)
    {
        PendingIssue = issue;
        LastResult = null;
    }

    private bool CanConnectAzureBoards() => !IsBusy && IsAzureBoardsSelected;
    private bool CanLoadTeams() => !IsBusy && IsAzureBoardsConnected && SelectedProject is not null;
    private bool CanSaveConfiguration() => !IsBusy && SelectedReporterIndex >= 0;
    private bool CanFileIssue() => !IsBusy && PendingIssue is not null && issueReporting.SelectedReporter?.IsConfigured == true;

    [RelayCommand(CanExecute = nameof(CanConnectAzureBoards))]
    private Task ConnectAzureBoardsAsync() => RunAsync(async () =>
    {
        AzureDevOpsIssueReporter reporter = GetAzureBoardsReporter();
        if (!AzureDevOpsServerUrl.TryNormalize(AzureBoardsServerLink, out Uri? serverUri))
        {
            throw new InvalidDataException(getString("AzureBoardsInvalidUrl"));
        }

        await reporter.ConnectAsync(serverUri!, CancellationToken.None).ConfigureAwait(true);
        IsAzureBoardsConnected = reporter.IsConnected;
        AccountDisplayName = reporter.Identity?.DisplayName ?? string.Empty;
        AccountEmail = reporter.Identity?.Email ?? string.Empty;
        Projects.Clear();
        Teams.Clear();
        foreach (AzureDevOpsProject project in
            await reporter.GetProjectsAsync(CancellationToken.None).ConfigureAwait(true))
        {
            Projects.Add(project);
        }
        Status = getString("AzureBoardsConnected");
        LoadTeamsCommand.NotifyCanExecuteChanged();
    });

    [RelayCommand(CanExecute = nameof(CanLoadTeams))]
    private Task LoadTeamsAsync() => RunAsync(async () =>
    {
        AzureDevOpsIssueReporter reporter = GetAzureBoardsReporter();
        AzureDevOpsProject project = SelectedProject ??
            throw new InvalidOperationException(getString("AzureBoardsSelectProject"));
        Teams.Clear();
        foreach (AzureDevOpsTeam team in
            await reporter.GetTeamsAsync(project, CancellationToken.None).ConfigureAwait(true))
        {
            Teams.Add(team);
        }
        Status = getString("AzureBoardsTeamsLoaded");
    });

    [RelayCommand]
    private void DisconnectAzureBoards()
    {
        AzureDevOpsIssueReporter reporter = GetAzureBoardsReporter();
        reporter.Disconnect();
        IsAzureBoardsConnected = false;
        AccountDisplayName = string.Empty;
        AccountEmail = string.Empty;
        Projects.Clear();
        Teams.Clear();
        SelectedProject = null;
        SelectedTeam = null;
        Status = getString("AzureBoardsDisconnected");
        LoadTeamsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveConfiguration))]
    private Task SaveConfigurationAsync() => RunAsync(async () =>
    {
        IIssueReporter reporter = issueReporting.SelectedReporter ??
            throw new InvalidOperationException(getString("IssueReporterRequired"));
        switch (reporter)
        {
            case GitHubIssueReporter gitHub:
                if (!GitHubRepositoryUrl.TryNormalize(GitHubRepositoryLink, out string normalized))
                {
                    throw new InvalidDataException(getString("GitHubInvalidUrl"));
                }
                gitHub.Configuration.RepoLink = normalized;
                GitHubRepositoryLink = normalized;
                break;
            case AzureDevOpsIssueReporter azure:
                if (!AzureDevOpsServerUrl.TryNormalize(AzureBoardsServerLink, out Uri? serverUri))
                {
                    throw new InvalidDataException(getString("AzureBoardsInvalidUrl"));
                }
                AzureDevOpsProject? project = SelectedProject ?? azure.Configuration.SavedConnection?.Project;
                AzureDevOpsTeam? team = SelectedProject is null
                    ? azure.Configuration.SavedConnection?.Team
                    : SelectedTeam;
                if (project is null)
                {
                    throw new InvalidDataException(getString("AzureBoardsSelectProject"));
                }
                azure.SetConnection(serverUri!, project, team);
                break;
        }

        await issueReporting.SaveSelectedConfigurationAsync().ConfigureAwait(true);
        Status = getString("IssueConfigurationSaved");
        FileIssueCommand.NotifyCanExecuteChanged();
    });

    [RelayCommand(CanExecute = nameof(CanFileIssue))]
    private Task FileIssueAsync() => RunAsync(async () =>
    {
        IssueInformation issue = PendingIssue ??
            throw new InvalidOperationException(getString("IssueContextRequired"));
        LastResult = await issueReporting.FileIssueAsync(issue).ConfigureAwait(true);
        Status = LastResult?.DisplayText is string identifier
            ? string.Format(CultureInfo.CurrentCulture, getString("IssueFiledWithIdentifier"), identifier)
            : getString("IssueReporterOpened");
    });

    private void ApplyReporter(IIssueReporter reporter)
    {
        IsGitHubSelected = reporter is GitHubIssueReporter;
        IsAzureBoardsSelected = reporter is AzureDevOpsIssueReporter;
        if (reporter is GitHubIssueReporter gitHub)
        {
            GitHubRepositoryLink = gitHub.Configuration.RepoLink;
        }
        else if (reporter is AzureDevOpsIssueReporter azure)
        {
            AzureDevOpsConnection? connection = azure.Configuration.SavedConnection;
            AzureBoardsServerLink = connection?.ServerUri?.AbsoluteUri ?? string.Empty;
            SelectedProject = connection?.Project;
            SelectedTeam = connection?.Team;
            IsAzureBoardsConnected = azure.IsConnected;
            AccountDisplayName = azure.Identity?.DisplayName ?? string.Empty;
            AccountEmail = azure.Identity?.Email ?? string.Empty;
        }
    }

    private AzureDevOpsIssueReporter GetAzureBoardsReporter() =>
        issueReporting.SelectedReporter as AzureDevOpsIssueReporter ??
        throw new InvalidOperationException(getString("AzureBoardsReporterRequired"));

    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            Error = string.Format(CultureInfo.CurrentCulture, getString("IssueReportingOperationFailed"), exception.Message);
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
            {
                return index;
            }
        }
        return -1;
    }
}
