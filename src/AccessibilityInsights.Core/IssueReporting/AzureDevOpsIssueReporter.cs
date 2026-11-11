// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AccessibilityInsights.Core.IssueReporting;

public sealed record AzureDevOpsIdentity(string? DisplayName, string? Email, byte[]? Avatar);

public sealed record AzureDevOpsIssueBrowserResult(int IssueId, int ZoomLevel);

public interface IAzureDevOpsClient
{
    bool IsConnected { get; }
    Uri? ConnectedUri { get; }
    AzureDevOpsIdentity? Identity { get; }

    Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken);
    void Disconnect();
    Task<IReadOnlyList<AzureDevOpsProject>> GetProjectsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AzureDevOpsTeam>> GetTeamsAsync(
        AzureDevOpsProject project,
        CancellationToken cancellationToken);
    Task<string?> GetAreaPathAsync(AzureDevOpsConnection connection, CancellationToken cancellationToken);
    Task<string?> GetIterationPathAsync(AzureDevOpsConnection connection, CancellationToken cancellationToken);
    Task<string?> GetWorkItemDescriptionAsync(int issueId, CancellationToken cancellationToken);
    Task<int?> ReplaceWorkItemDescriptionAsync(
        int issueId,
        string description,
        CancellationToken cancellationToken);
    Task<int?> AttachTestResultAsync(string path, int issueId, CancellationToken cancellationToken);
    Task<string?> AttachScreenshotAsync(
        ReadOnlyMemory<byte> screenshotPng,
        int issueId,
        CancellationToken cancellationToken);
    Task<Uri?> GetWorkItemUriAsync(int issueId, CancellationToken cancellationToken);
}

public interface IAzureDevOpsIssueBrowser
{
    Task<AzureDevOpsIssueBrowserResult?> OpenAsync(
        Uri issuePreviewUri,
        int zoomLevel,
        CancellationToken cancellationToken);
}

public sealed class AzureDevOpsIssueReporter : IIssueReporter
{
    private readonly IAzureDevOpsClient client;
    private readonly IAzureDevOpsIssueBrowser browser;
    private readonly string serviceName;

    public AzureDevOpsIssueReporter(
        IAzureDevOpsClient client,
        IAzureDevOpsIssueBrowser browser,
        string serviceName)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
        this.serviceName = !string.IsNullOrWhiteSpace(serviceName)
            ? serviceName
            : throw new ArgumentException("A service name is required.", nameof(serviceName));
    }

    public Guid StableIdentifier => IssueReporterIds.AzureBoards;
    public string ServiceName => serviceName;
    public bool CanAttachFiles => true;
    public bool IsConfigured => Configuration.SavedConnection?.IsPopulated == true;
    public AzureDevOpsConfiguration Configuration { get; private set; } = new();
    public bool IsConnected => client.IsConnected;
    public AzureDevOpsIdentity? Identity => client.Identity;

    public string SerializeConfiguration() => AzureDevOpsConfigurationSerializer.Serialize(Configuration);

    public void RestoreConfiguration(string? serializedConfiguration) =>
        Configuration = AzureDevOpsConfigurationSerializer.Deserialize(serializedConfiguration);

    public async Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken)
    {
        if (!AzureDevOpsServerUrl.TryNormalize(serverUri.AbsoluteUri, out Uri? normalized))
        {
            throw new ArgumentException("The Azure DevOps server URL is invalid.", nameof(serverUri));
        }

        await client.ConnectAsync(normalized!, cancellationToken).ConfigureAwait(false);
    }

    public void Disconnect() => client.Disconnect();

    public Task<IReadOnlyList<AzureDevOpsProject>> GetProjectsAsync(CancellationToken cancellationToken) =>
        client.GetProjectsAsync(cancellationToken);

    public Task<IReadOnlyList<AzureDevOpsTeam>> GetTeamsAsync(
        AzureDevOpsProject project,
        CancellationToken cancellationToken) =>
        client.GetTeamsAsync(project, cancellationToken);

    public void SetConnection(Uri serverUri, AzureDevOpsProject project, AzureDevOpsTeam? team)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        ArgumentNullException.ThrowIfNull(project);
        var connection = new AzureDevOpsConnection
        {
            ServerUri = serverUri,
            Project = new AzureDevOpsProject { Name = project.Name, Id = project.Id },
            Team = team is null
                ? null
                : new AzureDevOpsTeam
                {
                    Name = team.Name,
                    Id = team.Id,
                    ParentProject = new AzureDevOpsProject { Name = project.Name, Id = project.Id },
                },
        };
        connection.SetLastUsage(DateTime.Now);
        Configuration.SavedConnection = connection;
        Configuration.CachedConnections.Add(connection);
    }

    public async Task<IssueReportResult?> FileIssueAsync(
        IssueInformation issue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issue);
        AzureDevOpsConnection connection = Configuration.SavedConnection ??
            throw new InvalidOperationException("Azure Boards issue reporting is not configured.");
        if (!client.IsConnected || client.ConnectedUri != connection.ServerUri)
        {
            throw new IssueReporterAuthenticationRequiredException(
                "Connect to the configured Azure Boards account before filing an issue.");
        }

        string? areaPath = await client.GetAreaPathAsync(connection, cancellationToken).ConfigureAwait(false);
        string? iterationPath = await client.GetIterationPathAsync(connection, cancellationToken).ConfigureAwait(false);
        AzureDevOpsIssuePayload payload = AzureDevOpsIssuePayloadBuilder.Build(issue, areaPath, iterationPath);
        Uri preview = AzureDevOpsIssuePayloadBuilder.CreateIssuePreviewUri(
            connection.ServerUri!,
            connection.Project!.Name,
            connection.Team?.Name,
            payload.Fields);

        AzureDevOpsIssueBrowserResult? created =
            await browser.OpenAsync(preview, Configuration.ZoomLevel, cancellationToken).ConfigureAwait(false);
        if (created is null)
        {
            return null;
        }

        Configuration.ZoomLevel = created.ZoomLevel;
        if (!string.IsNullOrEmpty(issue.TestFileName) || issue.ScreenshotPng is { Length: > 0 })
        {
            await AttachIssueDataAsync(issue, payload.InternalIssueId, created.IssueId, cancellationToken)
                .ConfigureAwait(false);
        }

        Uri? issueUri = await client.GetWorkItemUriAsync(created.IssueId, cancellationToken).ConfigureAwait(false);
        return new IssueReportResult(created.IssueId.ToString(System.Globalization.CultureInfo.InvariantCulture), issueUri);
    }

    private async Task AttachIssueDataAsync(
        IssueInformation issue,
        string internalIssueId,
        int issueId,
        CancellationToken cancellationToken)
    {
        string? description =
            await client.GetWorkItemDescriptionAsync(issueId, cancellationToken).ConfigureAwait(false);
        if (description is null ||
            string.IsNullOrEmpty(internalIssueId) ||
            !description.Contains(internalIssueId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The created work item no longer contains its Accessibility Insights verification identifier.");
        }

        if (!string.IsNullOrEmpty(issue.TestFileName))
        {
            int? attached = await AttachTestResultWithRetryAsync(
                issue.TestFileName,
                issueId,
                cancellationToken).ConfigureAwait(false);
            if (!attached.HasValue)
            {
                throw new IOException("The Accessibility Insights test file could not be attached.");
            }
        }

        string imageHtml = string.Empty;
        if (issue.ScreenshotPng is { Length: > 0 } screenshot)
        {
            string? imageUri =
                await client.AttachScreenshotAsync(screenshot, issueId, cancellationToken).ConfigureAwait(false);
            if (imageUri is null)
            {
                throw new IOException("The screenshot could not be attached.");
            }
            imageHtml = $"<img src=\"{imageUri}\" alt=\"screenshot\"></img>";
        }

        string scrubbed = AzureDevOpsIssuePayloadBuilder.RemoveInternalMetadata(description, internalIssueId);
        string updated = AzureDevOpsIssuePayloadBuilder.WrapInHtmlBody(scrubbed) + imageHtml;
        int? replaced =
            await client.ReplaceWorkItemDescriptionAsync(issueId, updated, cancellationToken).ConfigureAwait(false);
        if (!replaced.HasValue)
        {
            throw new IOException("The Azure Boards work item description could not be updated.");
        }

        if (!string.IsNullOrEmpty(issue.TestFileName) && File.Exists(issue.TestFileName))
        {
            File.Delete(issue.TestFileName);
        }
    }

    private async Task<int?> AttachTestResultWithRetryAsync(
        string path,
        int issueId,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 2;
        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                return await client.AttachTestResultAsync(path, issueId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                IsTransient(exception))
            {
            }
        }

        return null;
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        AggregateException aggregate => aggregate.InnerExceptions.All(IsTransient),
        HttpRequestException => true,
        TimeoutException => true,
        TaskCanceledException => true,
        IOException => true,
        _ when exception.InnerException is not null => IsTransient(exception.InnerException),
        _ => false,
    };
}
