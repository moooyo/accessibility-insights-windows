// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AccessibilityInsights.Core.IssueReporting;

public enum IssueType
{
    SingleFailure,
    NoFailure,
    ColorContrast,
}

public sealed record IssueInformation
{
    public string? WindowTitle { get; init; }
    public string? Glimpse { get; init; }
    public Uri? HowToFixLink { get; init; }
    public Uri? HelpUri { get; init; }
    public string? ProcessName { get; init; }
    public string? RuleSource { get; init; }
    public string? RuleDescription { get; init; }
    public string? TestMessages { get; init; }
    public Guid? InternalGuid { get; init; }
    public string? ElementPath { get; init; }
    public string? RuleForTelemetry { get; init; }
    public string? UIFramework { get; init; }
    public IssueType? IssueType { get; init; }
    public string? TestFileName { get; init; }
    public byte[]? ScreenshotPng { get; init; }
    public string? ContrastRatio { get; init; }
    public string? FirstColorHex { get; init; }
    public string? SecondColorHex { get; init; }
    public string? ContrastFailureText { get; init; }

    public IssueInformation OverwritingUnion(IssueInformation replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return this with
        {
            WindowTitle = Replace(WindowTitle, replacement.WindowTitle),
            Glimpse = Replace(Glimpse, replacement.Glimpse),
            HowToFixLink = replacement.HowToFixLink ?? HowToFixLink,
            HelpUri = replacement.HelpUri ?? HelpUri,
            ProcessName = Replace(ProcessName, replacement.ProcessName),
            RuleSource = Replace(RuleSource, replacement.RuleSource),
            RuleDescription = Replace(RuleDescription, replacement.RuleDescription),
            TestMessages = Replace(TestMessages, replacement.TestMessages),
            InternalGuid = replacement.InternalGuid ?? InternalGuid,
            ElementPath = Replace(ElementPath, replacement.ElementPath),
            RuleForTelemetry = Replace(RuleForTelemetry, replacement.RuleForTelemetry),
            UIFramework = Replace(UIFramework, replacement.UIFramework),
            IssueType = replacement.IssueType ?? IssueType,
            TestFileName = replacement.TestFileName ?? TestFileName,
            ScreenshotPng = replacement.ScreenshotPng ?? ScreenshotPng,
            ContrastRatio = Replace(ContrastRatio, replacement.ContrastRatio),
            FirstColorHex = Replace(FirstColorHex, replacement.FirstColorHex),
            SecondColorHex = Replace(SecondColorHex, replacement.SecondColorHex),
            ContrastFailureText = Replace(ContrastFailureText, replacement.ContrastFailureText),
        };
    }

    private static string? Replace(string? original, string? replacement) =>
        replacement is null ? original : string.IsNullOrEmpty(replacement) ? null : replacement;
}

public sealed record IssueReportResult(string? DisplayText, Uri? IssueLink);

public static class IssueReporterIds
{
    public static readonly Guid GitHub = new("bbdf3582-d4a6-4b76-93ea-ef508d1fd4b8");
    public static readonly Guid AzureBoards = new("73D8F6EB-E98A-4285-9BA3-B532A7601CC4");
}
