// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AccessibilityInsights.Core.IssueReporting;

public sealed class GitHubIssueConfiguration
{
    public string RepoLink { get; set; } = string.Empty;
}

public static partial class GitHubRepositoryUrl
{
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex(@"^https:/*(?:www\.)?github\.com/+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubPrefixPattern();

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim().Replace('\\', '/').TrimEnd('/');
        if (GitHubPrefixPattern().IsMatch(candidate))
        {
            candidate = GitHubPrefixPattern().Replace(candidate, "https://github.com/", 1);
        }
        else if (candidate.StartsWith("www.github.com/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        string owner = Uri.UnescapeDataString(segments[0]);
        string repository = Uri.UnescapeDataString(segments[1]);
        if (!OwnerPattern().IsMatch(owner) ||
            !owner.Any(char.IsLetter) ||
            owner.Contains("--", StringComparison.Ordinal) ||
            !RepositoryPattern().IsMatch(repository) ||
            repository is "." or "..")
        {
            return false;
        }

        normalized = $"https://github.com/{owner}/{repository}";
        return true;
    }
}

public static class GitHubIssueFormatter
{
    public static Uri CreateNewIssueUri(string repositoryLink, IssueInformation issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (!GitHubRepositoryUrl.TryNormalize(repositoryLink, out string normalizedRepository))
        {
            throw new ArgumentException("The GitHub repository URL is invalid.", nameof(repositoryLink));
        }

        (string title, string body) = issue.IssueType switch
        {
            IssueType.SingleFailure => FormatSingleFailure(issue),
            IssueType.NoFailure => FormatNoFailure(issue),
            _ => throw new ArgumentException("The issue type is not supported by GitHub issue reporting.", nameof(issue)),
        };

        string uri = $"{normalizedRepository}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        return new Uri(uri, UriKind.Absolute);
    }

    private static (string Title, string Body) FormatSingleFailure(IssueInformation issue)
    {
        string title = $"{Value(issue.RuleSource)}: ({Value(issue.ProcessName)}/{Value(issue.Glimpse)}) {Value(issue.RuleDescription)}";
        var body = new StringBuilder()
            .AppendLine("The following accessibility issue needs investigation.")
            .AppendLine()
            .Append("**App:** ").AppendLine(Value(issue.ProcessName))
            .AppendLine()
            .Append("**Element path:** ").AppendLine(Value(issue.Glimpse))
            .AppendLine()
            .Append("**Issue Details:** ").Append(Value(issue.RuleDescription)).Append(" [")
            .Append(Value(issue.RuleSource)).Append("](").Append(issue.HelpUri?.ToString() ?? string.Empty).AppendLine(")")
            .AppendLine()
            .Append("**How To Fix:** ").AppendLine(Value(issue.TestMessages))
            .AppendLine()
            .Append("This accessibility issue was found using Accessibility Insights for Windows, a tool that helps debug and find accessibility issues earlier. Get more information and download this tool at https://aka.ms/AccessibilityInsights.")
            .ToString();
        return (title, body);
    }

    private static (string Title, string Body) FormatNoFailure(IssueInformation issue)
    {
        string title = $"({Value(issue.ProcessName)}/{Value(issue.Glimpse)}) has an accessibility issue that needs investigation";
        var body = new StringBuilder()
            .AppendLine("The following accessibility issue needs investigation.")
            .AppendLine()
            .Append("**App:** ").AppendLine(Value(issue.ProcessName))
            .AppendLine()
            .Append("**Element path:** ").AppendLine(Value(issue.Glimpse))
            .AppendLine()
            .AppendLine("**Issue Details:** []")
            .AppendLine()
            .Append("**How To Fix:** []")
            .ToString();
        return (title, body);
    }

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value;
}

public sealed class GitHubIssueReporter : IIssueReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly IExternalUriLauncher launcher;
    private readonly string serviceName;

    public GitHubIssueReporter(IExternalUriLauncher launcher, string serviceName)
    {
        this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        this.serviceName = !string.IsNullOrWhiteSpace(serviceName)
            ? serviceName
            : throw new ArgumentException("A service name is required.", nameof(serviceName));
    }

    public Guid StableIdentifier => IssueReporterIds.GitHub;
    public string ServiceName => serviceName;
    public bool CanAttachFiles => false;
    public GitHubIssueConfiguration Configuration { get; private set; } = new();
    public bool IsConfigured => GitHubRepositoryUrl.TryNormalize(Configuration.RepoLink, out _);

    public string SerializeConfiguration()
    {
        if (!GitHubRepositoryUrl.TryNormalize(Configuration.RepoLink, out string normalized))
        {
            throw new InvalidDataException("The GitHub repository URL is invalid.");
        }

        Configuration.RepoLink = normalized;
        return JsonSerializer.Serialize(Configuration, SerializerOptions);
    }

    public void RestoreConfiguration(string? serializedConfiguration)
    {
        if (string.IsNullOrWhiteSpace(serializedConfiguration))
        {
            Configuration = new GitHubIssueConfiguration();
            return;
        }

        try
        {
            GitHubIssueConfiguration? configuration =
                JsonSerializer.Deserialize<GitHubIssueConfiguration>(serializedConfiguration, SerializerOptions);
            Configuration = configuration is not null &&
                GitHubRepositoryUrl.TryNormalize(configuration.RepoLink, out string normalized)
                ? new GitHubIssueConfiguration { RepoLink = normalized }
                : new GitHubIssueConfiguration();
        }
        catch (JsonException)
        {
            Configuration = new GitHubIssueConfiguration();
        }
    }

    public async Task<IssueReportResult?> FileIssueAsync(
        IssueInformation issue,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("GitHub issue reporting is not configured.");
        }

        Uri uri = GitHubIssueFormatter.CreateNewIssueUri(Configuration.RepoLink, issue);
        await launcher.LaunchAsync(uri, cancellationToken).ConfigureAwait(false);
        return new IssueReportResult(null, uri);
    }
}
