// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AccessibilityInsights.Services;

public sealed record IssueDraft(string Title, string Body);

public static partial class GitHubIssueService
{
    public static Uri ValidateRepository(string repository)
    {
        var match = RepositoryPattern().Match(repository.Trim());
        if (!match.Success || match.Groups[1].Length > 39 ||
            !match.Groups[1].Value.Any(char.IsAsciiLetter) || match.Groups[2].Length > 100 ||
            match.Groups[2].Value is "." or "..")
        {
            throw new ArgumentException("Enter a GitHub repository URL in https://github.com/owner/repo format.");
        }

        return new($"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}");
    }

    public static IssueDraft CreateDraft(string process, string element, RuleRow? rule, Func<string, string> resource)
    {
        if (rule is null)
        {
            return new(string.Format(CultureInfo.CurrentCulture, resource("NoFailureIssueTitle"), process, element),
                string.Format(CultureInfo.CurrentCulture, resource("NoFailureIssueBody"), process, element));
        }

        return new(string.Format(CultureInfo.CurrentCulture, resource("SingleFailureIssueTitle"),
                rule.Source, process, rule.Glimpse, rule.Description),
            string.Format(CultureInfo.CurrentCulture, resource("SingleFailureIssueBody"),
                process, rule.Glimpse, rule.Description, rule.Source, rule.HelpUrl, rule.Details));
    }

    public static Uri GetNewIssueUri(string repository, IssueDraft draft)
    {
        var root = ValidateRepository(repository);
        return new($"{root.AbsoluteUri}/issues/new?title={Uri.EscapeDataString(draft.Title)}&body={Uri.EscapeDataString(draft.Body)}");
    }

    [GeneratedRegex(@"^https://(?:www\.)?github\.com/([a-z0-9]+(?:-[a-z0-9]+)*)/([a-z0-9_.-]+)/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex RepositoryPattern();
}
