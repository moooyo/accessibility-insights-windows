// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.IssueReporting;
using AccessibilityInsights.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Text.Json;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class IssueReportingMigrationTests
{
    [TestMethod]
    public void GitHubValidationNormalizesOriginalAcceptedFormsAndRejectsUnsafeLinks()
    {
        Assert.IsTrue(GitHubRepositoryUrl.TryNormalize("https://github.com/bla/bla-blas/", out string canonical));
        Assert.AreEqual("https://github.com/bla/bla-blas", canonical);
        Assert.IsTrue(GitHubRepositoryUrl.TryNormalize(@"https:\\github.com/\bla//\bla-blas\", out canonical));
        Assert.AreEqual("https://github.com/bla/bla-blas", canonical);
        Assert.IsTrue(GitHubRepositoryUrl.TryNormalize("https:/github.com/bla/bla-blas", out canonical));
        Assert.AreEqual("https://github.com/bla/bla-blas", canonical);
        Assert.IsTrue(GitHubRepositoryUrl.TryNormalize("www.github.com/bla/bla-blas", out _));
        Assert.IsFalse(GitHubRepositoryUrl.TryNormalize("http://github.com/bla/bla-blas", out _));
        Assert.IsFalse(GitHubRepositoryUrl.TryNormalize("https://github.com/bla/bla-blas/issues", out _));
        Assert.IsFalse(GitHubRepositoryUrl.TryNormalize("https://github.com/1-1/bla-blas", out _));
        Assert.IsFalse(GitHubRepositoryUrl.TryNormalize("https://github.com/bla/..", out _));
    }

    [TestMethod]
    public void GitHubPayloadPreservesOriginalTitleAndBodyMetadata()
    {
        var issue = new IssueInformation
        {
            IssueType = IssueType.SingleFailure,
            ProcessName = "processName",
            Glimpse = "glimpse",
            RuleSource = "ruleSource",
            RuleDescription = "ruleDescription",
            HelpUri = new Uri("https://www.helpUri.com"),
            TestMessages = "testMessages",
        };

        Uri uri = GitHubIssueFormatter.CreateNewIssueUri("https://github.com/bla/bla-blas", issue);
        Dictionary<string, string> query = ParseQuery(uri.Query);

        Assert.AreEqual("ruleSource: (processName/glimpse) ruleDescription", query["title"]);
        StringAssert.Contains(query["body"], "**Issue Details:** ruleDescription [ruleSource](https://www.helpuri.com/)");
        StringAssert.Contains(query["body"], "**How To Fix:** testMessages");
        StringAssert.Contains(query["body"], "https://aka.ms/AccessibilityInsights");
    }

    [TestMethod]
    public void AzureBoardsPayloadPreservesTemplatesTruncationAndMetadata()
    {
        Guid internalId = new("73e16008-4166-4305-b9ca-5621a38e04d0");
        var issue = new IssueInformation
        {
            IssueType = IssueType.SingleFailure,
            InternalGuid = internalId,
            ProcessName = new string('x', 60),
            Glimpse = new string('y', 60),
            TestMessages = new string('z', 200),
            RuleSource = "[Rule]",
            RuleDescription = "Description",
        };

        AzureDevOpsIssuePayload payload =
            AzureDevOpsIssuePayloadBuilder.Build(issue, "Some Area", "Some Iteration");

        Assert.AreEqual(5, payload.Fields.Count);
        Assert.AreEqual(internalId.ToString(), payload.InternalIssueId);
        StringAssert.StartsWith(payload.Fields[AzureDevOpsField.Title], "Rule: (" + new string('x', 50) + ".exe/");
        StringAssert.Contains(payload.Fields[AzureDevOpsField.ReproSteps], new string('z', 150) + "...open attached");
        StringAssert.Contains(payload.Fields[AzureDevOpsField.ReproSteps], internalId.ToString());
        Assert.AreEqual("Some Area", payload.Fields[AzureDevOpsField.AreaPath]);
        Assert.AreEqual("Some Iteration", payload.Fields[AzureDevOpsField.IterationPath]);
    }

    [TestMethod]
    public void AzureBoardsPreviewUriUsesOriginalFieldNamesAndCharacterSubstitutions()
    {
        var fields = new Dictionary<AzureDevOpsField, string>
        {
            [AzureDevOpsField.Title] = "This\u00a0has\u00b7characters",
            [AzureDevOpsField.Tags] = "A11y",
        };
        Uri uri = AzureDevOpsIssuePayloadBuilder.CreateIssuePreviewUri(
            new Uri("https://dev.azure.com/fabrikam"),
            "My Project",
            "My Team",
            fields);

        Assert.AreEqual("dev.azure.com", uri.Host);
        StringAssert.Contains(uri.AbsoluteUri, "fabrikam/My%20Project/My%20Team/_workItems/create/Bug?");
        StringAssert.Contains(uri.Query, "%5BSystem.Title%5D=This+has-characters");
    }

    [TestMethod]
    public void AzureBoardsConnectionAndCacheSerializationMatchOriginalFixtures()
    {
        const string knownConnection =
            """{"ServerUri":"https://www.bing.com","Project":{"Name":"My Project","Id":"71b41af3-4c55-44eb-a1d4-3b0973c07ea3"},"Team":{"ParentProject":{"Name":"My Project","Id":"71b41af3-4c55-44eb-a1d4-3b0973c07ea3"},"Name":"My Team","Id":"4a56a1a0-b99f-49ca-986d-9c6d7fa6137d"},"LastUsage":"2018-12-23T16:19:30Z"}""";
        AzureDevOpsConnection connection =
            JsonSerializer.Deserialize<AzureDevOpsConnection>(knownConnection)!;
        var cache = new AzureDevOpsConnectionCache();
        cache.Add(connection);

        AzureDevOpsConnection restored =
            AzureDevOpsConnectionCache.FromConfigString(cache.ToConfigString()).GetMostRecent()!;

        Assert.AreEqual(new Uri("https://www.bing.com"), restored.ServerUri);
        Assert.AreEqual("My Project", restored.Project!.Name);
        Assert.AreEqual("My Team", restored.Team!.Name);
        Assert.AreEqual(new DateTime(2018, 12, 23, 16, 19, 30, DateTimeKind.Utc), restored.LastUsage);
    }

    [TestMethod]
    public void AzureBoardsConfigurationLoadsLegacyAndObjectShapedCaches()
    {
        const string connection =
            """{"ServerUri":"https://dev.azure.com/fabrikam","Project":{"Name":"Project","Id":"71b41af3-4c55-44eb-a1d4-3b0973c07ea3"},"Team":null,"LastUsage":"2018-12-23T16:19:30Z"}""";
        string objectCacheConfiguration =
            "{\"ZoomLevel\":350,\"SavedConnection\":" + connection + ",\"CachedConnections\":{}}";

        AzureDevOpsConfiguration current =
            AzureDevOpsConfigurationSerializer.Deserialize(objectCacheConfiguration);
        Assert.AreEqual(350, current.ZoomLevel);
        Assert.AreEqual("Project", current.SavedConnection!.Project!.Name);

        string legacyConfiguration = JsonSerializer.Serialize(new
        {
            ZoomLevel = 275,
            SerializedSavedConnection = connection,
            SerializedCachedConnections = $"[{connection}]",
        });
        AzureDevOpsConfiguration legacy =
            AzureDevOpsConfigurationSerializer.Deserialize(legacyConfiguration);
        Assert.AreEqual(275, legacy.ZoomLevel);
        Assert.AreEqual("Project", legacy.SavedConnection!.Project!.Name);
        Assert.AreEqual(1, legacy.CachedConnections.Connections.Count);
    }

    [TestMethod]
    public async Task RestoringIssueConfigurationHasNoNetworkOrAuthenticationSideEffects()
    {
        var settings = new FakeSettingsService();
        var launcher = new FakeLauncher();
        var client = new FakeAzureDevOpsClient();
        var browser = new FakeIssueBrowser();
        var gitHub = new GitHubIssueReporter(launcher, "GitHub");
        var azure = new AzureDevOpsIssueReporter(client, browser, "Azure Boards");
        var configuration = new AzureDevOpsConfiguration
        {
            SavedConnection = new AzureDevOpsConnection
            {
                ServerUri = new Uri("https://dev.azure.com/fabrikam"),
                Project = new AzureDevOpsProject { Name = "Project", Id = Guid.NewGuid() },
            },
        };
        settings.Current.IssueReporterSerializedConfigs = JsonSerializer.Serialize(
            new Dictionary<Guid, string>
            {
                [IssueReporterIds.AzureBoards] = AzureDevOpsConfigurationSerializer.Serialize(configuration),
            });
        settings.Current.SelectedIssueReporter = IssueReporterIds.AzureBoards;
        var service = new IssueReportingService(settings, [gitHub, azure]);

        await service.InitializeAsync();

        Assert.AreSame(azure, service.SelectedReporter);
        Assert.IsTrue(azure.IsConfigured);
        Assert.AreEqual(0, client.CallCount);
        Assert.AreEqual(0, browser.CallCount);
        Assert.AreEqual(0, launcher.CallCount);
    }

    [TestMethod]
    public async Task LegacyTopLevelAzureBoardsConfigurationIsRestoredWithoutNetwork()
    {
        const string connection =
            """{"ServerUri":"https://dev.azure.com/fabrikam","Project":{"Name":"Project","Id":"71b41af3-4c55-44eb-a1d4-3b0973c07ea3"},"Team":null,"LastUsage":"2018-12-23T16:19:30Z"}""";
        string json = JsonSerializer.Serialize(new
        {
            HotKeyForRecord = "Alt + F1",
            HotKeyForPause = "Alt + F2",
            HotKeyForSnap = "Alt + F3",
            HotKeyForActivatingMainWindow = "Alt + F4",
            ZoomLevel = 350,
            SerializedSavedConnection = connection,
            SerializedCachedConnections = $"[{connection}]",
        });
        AppSettings appSettings = AppSettingsSerializer.Deserialize(
            json,
            new SettingsPaths("C:\\config", "C:\\user"),
            "2.0.0");
        var settings = new FakeSettingsService(appSettings);
        var client = new FakeAzureDevOpsClient();
        var azure = new AzureDevOpsIssueReporter(client, new FakeIssueBrowser(), "Azure Boards");
        var service = new IssueReportingService(settings, [azure]);

        await service.InitializeAsync();

        Assert.AreEqual(350, azure.Configuration.ZoomLevel);
        Assert.AreEqual("Project", azure.Configuration.SavedConnection!.Project!.Name);
        Assert.AreEqual(1, azure.Configuration.CachedConnections.Connections.Count);
        Assert.AreEqual(0, client.CallCount);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => WebUtility.UrlDecode(part[0]),
                part => WebUtility.UrlDecode(part.Length > 1 ? part[1] : string.Empty),
                StringComparer.Ordinal);
    }

    private sealed class FakeLauncher : IExternalUriLauncher
    {
        public int CallCount { get; private set; }
        public Task LaunchAsync(Uri uri, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIssueBrowser : IAzureDevOpsIssueBrowser
    {
        public int CallCount { get; private set; }
        public Task<AzureDevOpsIssueBrowserResult?> OpenAsync(
            Uri issuePreviewUri,
            int zoomLevel,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<AzureDevOpsIssueBrowserResult?>(null);
        }
    }

    private sealed class FakeAzureDevOpsClient : IAzureDevOpsClient
    {
        public int CallCount { get; private set; }
        public bool IsConnected => false;
        public Uri? ConnectedUri => null;
        public AzureDevOpsIdentity? Identity => null;
        public Task ConnectAsync(Uri serverUri, CancellationToken cancellationToken) => Called();
        public void Disconnect() => CallCount++;
        public Task<IReadOnlyList<AzureDevOpsProject>> GetProjectsAsync(CancellationToken cancellationToken) =>
            Called<IReadOnlyList<AzureDevOpsProject>>([]);
        public Task<IReadOnlyList<AzureDevOpsTeam>> GetTeamsAsync(
            AzureDevOpsProject project,
            CancellationToken cancellationToken) => Called<IReadOnlyList<AzureDevOpsTeam>>([]);
        public Task<string?> GetAreaPathAsync(
            AzureDevOpsConnection connection,
            CancellationToken cancellationToken) => Called<string?>(null);
        public Task<string?> GetIterationPathAsync(
            AzureDevOpsConnection connection,
            CancellationToken cancellationToken) => Called<string?>(null);
        public Task<string?> GetWorkItemDescriptionAsync(
            int issueId,
            CancellationToken cancellationToken) => Called<string?>(null);
        public Task<int?> ReplaceWorkItemDescriptionAsync(
            int issueId,
            string description,
            CancellationToken cancellationToken) => Called<int?>(null);
        public Task<int?> AttachTestResultAsync(
            string path,
            int issueId,
            CancellationToken cancellationToken) => Called<int?>(null);
        public Task<string?> AttachScreenshotAsync(
            ReadOnlyMemory<byte> screenshotPng,
            int issueId,
            CancellationToken cancellationToken) => Called<string?>(null);
        public Task<Uri?> GetWorkItemUriAsync(
            int issueId,
            CancellationToken cancellationToken) => Called<Uri?>(null);

        private Task Called()
        {
            CallCount++;
            return Task.CompletedTask;
        }

        private Task<T> Called<T>(T value)
        {
            CallCount++;
            return Task.FromResult(value);
        }
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        public FakeSettingsService(AppSettings? current = null)
        {
            Current = current ??
                AppSettings.CreateDefault(new SettingsPaths("C:\\config", "C:\\user"), "2.0.0");
        }

        public AppSettings Current { get; }

        public SettingsPaths Paths => new("C:\\config", "C:\\user");
        public bool IsInitialized => true;
        public event EventHandler<AppSettingsChangeSet>? SettingsChanged;
        public Task<AppSettings> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current.Clone());
        public AppSettings GetCurrent() => Current.Clone();
        public Task<AppSettingsChangeSet> SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettingsChangeSet(settings.Clone(), new HashSet<string>()));
        public Task<AppSettingsChangeSet> UpdateAsync(
            Action<AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            update(Current);
            var changeSet = new AppSettingsChangeSet(Current.Clone(), new HashSet<string>());
            SettingsChanged?.Invoke(this, changeSet);
            return Task.FromResult(changeSet);
        }
        public AppSettings CreateDefault() => Current.Clone();
    }
}
