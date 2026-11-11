// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.IssueReporting;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.Core.WebApi.Types;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.Profile;
using Microsoft.VisualStudio.Services.Profile.Client;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using System.Globalization;
using Windows.Security.Authentication.Web.Core;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class AzureDevOpsClient : IAzureDevOpsClient, IDisposable
{
    private const int PageSize = 1000;
    private const string VisualStudioClientId = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";
    private const string AzureDevOpsScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private VssConnection? connection;
    private Profile? profile;
    private bool disposed;

    public bool IsConnected => connection?.HasAuthenticated == true;
    public Uri? ConnectedUri => connection?.Uri;
    public AzureDevOpsIdentity? Identity => profile is null
        ? null
        : new AzureDevOpsIdentity(profile.DisplayName, profile.EmailAddress, profile.Avatar?.Value);

    public async Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        ObjectDisposedException.ThrowIf(disposed, this);
        await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisconnectCore();
            VssConnection candidate;
            if (IsAzureDevOpsService(serverUri))
            {
                string accessToken = await AcquireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                candidate = new VssConnection(
                    serverUri,
                    new VssOAuthAccessTokenCredential(accessToken));
            }
            else
            {
                candidate = new VssConnection(
                    serverUri,
                    new VssCredentials(
                        new WindowsCredential(useDefaultCredentials: true),
                        CredentialPromptType.DoNotPrompt));
            }
            try
            {
                await candidate.ConnectAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                connection = candidate;
                if (!await CanGetProjectsAsync().ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The Azure Boards project list is unavailable.");
                }

                var profileClient = connection.GetClient<ProfileHttpClient>();
                var context = new ProfileQueryContext(AttributesScope.Core, CoreProfileAttributes.All, null);
                profile = await profileClient.GetProfileAsync(context).ConfigureAwait(false);
            }
            catch
            {
                candidate.Disconnect();
                connection = null;
                profile = null;
                throw;
            }
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public void Disconnect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        DisconnectCore();
    }

    public async Task<IReadOnlyList<AzureDevOpsProject>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        ProjectHttpClient client = GetConnection().GetClient<ProjectHttpClient>();
        var projects = new List<TeamProjectReference>();
        int returned;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await client.GetProjects(null, PageSize, projects.Count).ConfigureAwait(false);
            returned = page.Count;
            projects.AddRange(page);
        }
        while (returned >= PageSize);

        return [.. projects
            .OrderBy(project => project.Name, StringComparer.CurrentCulture)
            .Select(project => new AzureDevOpsProject { Name = project.Name, Id = project.Id })];
    }

    public async Task<IReadOnlyList<AzureDevOpsTeam>> GetTeamsAsync(
        AzureDevOpsProject project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        TeamHttpClient client = GetConnection().GetClient<TeamHttpClient>();
        var teams = new List<WebApiTeamRef>();
        int returned;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await client.GetTeamsAsync(
                project.Id.ToString("D", CultureInfo.InvariantCulture),
                null,
                PageSize,
                teams.Count).ConfigureAwait(false);
            returned = page.Count;
            teams.AddRange(page);
        }
        while (returned >= PageSize);

        return [.. teams
            .OrderBy(team => team.Name, StringComparer.CurrentCulture)
            .Select(team => new AzureDevOpsTeam
            {
                Name = team.Name,
                Id = team.Id,
                ParentProject = new AzureDevOpsProject { Name = project.Name, Id = project.Id },
            })];
    }

    public async Task<string?> GetAreaPathAsync(
        AzureDevOpsConnection selectedConnection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedConnection);
        cancellationToken.ThrowIfCancellationRequested();
        WorkHttpClient client = GetConnection().GetClient<WorkHttpClient>();
        try
        {
            var values = await client.GetTeamFieldValuesAsync(
                new TeamContext(selectedConnection.Project!.Id, selectedConnection.Team?.Id)).ConfigureAwait(false);
            return values?.DefaultValue;
        }
        catch (VssException)
        {
            return null;
        }
    }

    public async Task<string?> GetIterationPathAsync(
        AzureDevOpsConnection selectedConnection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedConnection);
        cancellationToken.ThrowIfCancellationRequested();
        WorkHttpClient client = GetConnection().GetClient<WorkHttpClient>();
        try
        {
            var iterations = await client.GetTeamIterationsAsync(
                new TeamContext(selectedConnection.Project!.Id, selectedConnection.Team?.Id),
                "current").ConfigureAwait(false);
            return iterations.FirstOrDefault()?.Path;
        }
        catch (VssException)
        {
            return null;
        }
    }

    public async Task<string?> GetWorkItemDescriptionAsync(
        int issueId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkItemTrackingHttpClient client = GetConnection().GetClient<WorkItemTrackingHttpClient>();
        try
        {
            WorkItem item = await client.GetWorkItemAsync(
                issueId,
                [AzureDevOpsField.ReproSteps.ToApiString()]).ConfigureAwait(false);
            return item.Fields.TryGetValue(AzureDevOpsField.ReproSteps.ToApiString(), out object? value)
                ? value?.ToString()
                : null;
        }
        catch (VssException)
        {
            return null;
        }
    }

    public async Task<int?> ReplaceWorkItemDescriptionAsync(
        int issueId,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkItemTrackingHttpClient client = GetConnection().GetClient<WorkItemTrackingHttpClient>();
        var patch = new JsonPatchDocument
        {
            new JsonPatchOperation
            {
                Operation = Operation.Replace,
                Path = $"/fields/{AzureDevOpsField.ReproSteps.ToApiString()}",
                Value = description,
            },
        };
        WorkItem item = await client.UpdateWorkItemAsync(patch, issueId).ConfigureAwait(false);
        return item.Id;
    }

    public async Task<int?> AttachTestResultAsync(
        string path,
        int issueId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkItemTrackingHttpClient client = GetConnection().GetClient<WorkItemTrackingHttpClient>();
        await using FileStream stream = File.OpenRead(path);
        AttachmentReference attachment = await client.CreateAttachmentAsync(
            stream,
            null,
            $"{issueId.ToString(CultureInfo.InvariantCulture)}.a11ytest",
            "Simple",
            null).ConfigureAwait(false);
        var patch = new JsonPatchDocument
        {
            new JsonPatchOperation
            {
                Operation = Operation.Test,
                Path = "/rev",
                Value = "1",
            },
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/System.History",
                Value = "Attached an Accessibility Insights for Windows test file and screenshot.",
            },
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = "AttachedFile",
                    url = attachment.Url,
                    attributes = new { comment = "Accessibility Insights for Windows test file" },
                },
            },
        };
        WorkItem item = await client.UpdateWorkItemAsync(patch, issueId).ConfigureAwait(false);
        return item.Id;
    }

    public async Task<string?> AttachScreenshotAsync(
        ReadOnlyMemory<byte> screenshotPng,
        int issueId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkItemTrackingHttpClient client = GetConnection().GetClient<WorkItemTrackingHttpClient>();
        await using var stream = new MemoryStream(screenshotPng.ToArray(), writable: false);
        AttachmentReference attachment = await client.CreateAttachmentAsync(
            stream,
            null,
            $"{issueId.ToString(CultureInfo.InvariantCulture)}-pic.png",
            "Simple",
            null).ConfigureAwait(false);
        var patch = new JsonPatchDocument
        {
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = "AttachedFile",
                    url = attachment.Url,
                    attributes = new { comment = "Screenshot of element" },
                },
            },
        };
        await client.UpdateWorkItemAsync(patch, issueId).ConfigureAwait(false);
        return attachment.Url;
    }

    public async Task<Uri?> GetWorkItemUriAsync(int issueId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkItemTrackingHttpClient client = GetConnection().GetClient<WorkItemTrackingHttpClient>();
        WorkItem item = await client.GetWorkItemAsync(issueId, expand: WorkItemExpand.Links).ConfigureAwait(false);
        return item.Links?.Links is not null &&
            item.Links.Links.TryGetValue("html", out object? link) &&
            link is ReferenceLink reference
            ? new Uri(reference.Href)
            : null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisconnectCore();
        connectionGate.Dispose();
    }

    private async Task<bool> CanGetProjectsAsync()
    {
        try
        {
            ProjectHttpClient client = GetConnection().GetClient<ProjectHttpClient>();
            var projects = await client.GetProjects(null, 1).ConfigureAwait(false);
            return projects.Count >= 0;
        }
        catch (VssException)
        {
            return false;
        }
    }

    private VssConnection GetConnection() =>
        connection is { HasAuthenticated: true }
            ? connection
            : throw new IssueReporterAuthenticationRequiredException(
                "Connect to Azure Boards before using its project or work item APIs.");

    private void DisconnectCore()
    {
        try
        {
            connection?.Disconnect();
        }
        catch (ArgumentNullException)
        {
        }
        finally
        {
            connection = null;
            profile = null;
        }
    }

    private static async Task<string> AcquireAccessTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = await WebAuthenticationCoreManager.FindSystemAccountProviderAsync(
            "https://login.microsoft.com",
            "organizations");
        var request = new WebTokenRequest(
            provider,
            AzureDevOpsScope,
            VisualStudioClientId,
            WebTokenRequestPromptType.Default);
        WebTokenRequestResult result =
            await WebAuthenticationCoreManager.GetTokenSilentlyAsync(request);
        if (result.ResponseStatus != WebTokenRequestStatus.Success)
        {
            result = await WebAuthenticationCoreManager.RequestTokenAsync(request);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (result.ResponseStatus != WebTokenRequestStatus.Success ||
            result.ResponseData.Count == 0 ||
            string.IsNullOrWhiteSpace(result.ResponseData[0].Token))
        {
            string error = result.ResponseError is null
                ? result.ResponseStatus.ToString()
                : $"{result.ResponseStatus}: {result.ResponseError.ErrorMessage}";
            throw new IssueReporterAuthenticationRequiredException(error);
        }

        return result.ResponseData[0].Token;
    }

    private static bool IsAzureDevOpsService(Uri serverUri) =>
        string.Equals(serverUri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
        serverUri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase);
}
