// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AccessibilityInsights.Core.IssueReporting;

public enum AzureDevOpsField
{
    Title,
    ReproSteps,
    Tags,
    AreaPath,
    IterationPath,
}

public static class AzureDevOpsFieldExtensions
{
    public static string ToApiString(this AzureDevOpsField value) => value switch
    {
        AzureDevOpsField.Title => "System.Title",
        AzureDevOpsField.ReproSteps => "Microsoft.VSTS.TCM.ReproSteps",
        AzureDevOpsField.Tags => "System.Tags",
        AzureDevOpsField.AreaPath => "System.AreaPath",
        AzureDevOpsField.IterationPath => "System.IterationPath",
        _ => string.Empty,
    };
}

public sealed record AzureDevOpsIssuePayload(
    IReadOnlyDictionary<AzureDevOpsField, string> Fields,
    string InternalIssueId);

public static class AzureDevOpsIssuePayloadBuilder
{
    private const string UnknownValue = "[unknown]";
    private const string ConcatenationMessage = "...open attached A11y test file for full details.";

    private const string SingleFailureHtml = """
        <b>Application: </b>@[ProcessName]@ has the following accessibility issue that needs investigation.<br/>
        <br/>
        <b>Issue Details</b><br/>
        @[TestMessages]@
        <br/>
        <br/>
        <b>Element path: </b>@[Glimpse]@<br/>
        <br/>
        <b>To Reproduce and How to Fix guidance: </b>Please open the attached Accessibility Insights for Windows Test File (*.a11ytest) to reproduce the error in Accessibility Insights for Windows.<br/>
        <br/>
        This accessibility issue was found using <b>Accessibility Insights for Windows</b>, a tool that helps debug and find accessibility issues earlier. Get more information & download this tool at <a href="https://aka.ms/AccessibilityInsights">https://aka.ms/AccessibilityInsights</a>.<br/>
        <br/>
        <div>
        <hr/>
        Do not modify - Accessibility Insights for Windows uses this section to:<br/>
        <ol>
        <li>Add a screenshot at the end of the Repro Steps.</li>
        <li>Attach a test file to this issue.</li>
        </ol>
        This section will be removed when this issue is saved: @[InternalGuid]@<br/>
        <hr/>
        </div>
        """;

    private const string NoFailureHtml = """
        <b>Application: </b>@[ProcessName]@ has the following accessibility issue that needs investigation.<br/>
        <br/>
        <b>Issue Details:</b><br/>
        <br/>
        <b>Element path: </b>@[Glimpse]@<br/>
        <br/>
        <b>To Reproduce and How to Fix guidance: </b>Please open the attached Accessibility Insights for Windows Test File (*.a11ytest) to reproduce the error in Accessibility Insights for Windows.<br/>
        <br/>
        This accessibility issue was found using <b>Accessibility Insights for Windows</b>, a tool that helps debug and find accessibility issues earlier. Get more information & download this tool at <a href="https://aka.ms/AccessibilityInsights">https://aka.ms/AccessibilityInsights</a>.<br/>
        <br/>
        <div>
        <hr/>
        Do not modify - Accessibility Insights for Windows uses this section to:<br/>
        <ol>
        <li>Add a screenshot at the end of the Repro Steps.</li>
        <li>Attach a test file to this issue.</li>
        </ol>
        This section will be removed when this issue is saved: @[InternalGuid]@<br/>
        <hr/>
        </div>
        """;

    private const string ColorContrastHtml = """
        <b>Application: </b>@[ProcessName]@ has the following accessibility issue that needs investigation.<br/>
        <br/>
        <b>Issue Details: </b>@[Glimpse]@ has color contrast ratio of <b>@[ContrastRatio]@:1</b><br/>
        Color 1: @[FirstColorHex]@<br/>
        Color 2: @[SecondColorHex]@<br/>
        Failure results:<br/>
        @[ContrastFailureText]@<br/>
        <br/>
        <b>To Reproduce and How to Fix guidance:</b> Please open the attached Accessibility Insights for Windows Test File (*.a11ytest) and use the color picker dropdown to find a color that meets the necessary color contrast ratio.
        <br/>
        <br/>
        This accessibility issue was found using <b>Accessibility Insights for Windows</b>, a tool that helps debug and find accessibility issues earlier. Get more information & download this tool at <a href="https://aka.ms/AccessibilityInsights">https://aka.ms/AccessibilityInsights</a>.<br/>
        <br/>
        <div>
        <hr/>
        Do not modify - Accessibility Insights for Windows uses this section to:<br/>
        <ol>
        <li>Add a screenshot at the end of the Repro Steps.</li>
        <li>Attach a test file to this issue.</li>
        </ol>
        This section will be removed when this issue is saved: @[InternalGuid]@<br/>
        <hr/>
        </div>
        """;

    public static AzureDevOpsIssuePayload Build(
        IssueInformation issue,
        string? areaPath = null,
        string? iterationPath = null)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var values = ToTemplateValues(issue);
        string template;
        string title;
        string tags;

        switch (issue.IssueType)
        {
            case IssueType.SingleFailure:
                template = SingleFailureHtml;
                title = "@[RuleSource]@: (@[ProcessName]@/@[Glimpse]@) @[RuleDescription]@";
                tags = "Accessibility Insights For Windows;A11y;Accessibility;@[RuleSource]@";
                break;
            case IssueType.ColorContrast:
                template = ColorContrastHtml;
                title = "@[RuleSource]@: (@[ProcessName]@/@[Glimpse]@) @[RuleDescription]@";
                tags = "Accessibility Insights For Windows;A11y;Accessibility;@[RuleSource]@";
                break;
            default:
                template = NoFailureHtml;
                title = "(@[ProcessName]@/@[Glimpse]@) has an accessibility issue that needs investigation";
                tags = "Accessibility Insights For Windows;A11y;Accessibility";
                break;
        }

        var fields = new Dictionary<AzureDevOpsField, string>
        {
            [AzureDevOpsField.Title] = Populate(title, values),
            [AzureDevOpsField.Tags] = Populate(tags, values),
            [AzureDevOpsField.ReproSteps] = Populate(RemoveTemplateIndentation(template), values),
        };
        if (areaPath is not null)
        {
            fields[AzureDevOpsField.AreaPath] = areaPath;
        }
        if (iterationPath is not null)
        {
            fields[AzureDevOpsField.IterationPath] = iterationPath;
        }

        return new AzureDevOpsIssuePayload(fields, values["InternalGuid"]);
    }

    public static Uri CreateIssuePreviewUri(
        Uri serverUri,
        string projectName,
        string? teamName,
        IReadOnlyDictionary<AzureDevOpsField, string> fields)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(fields);

        string baseUri = serverUri.AbsoluteUri.TrimEnd('/');
        string project = Uri.EscapeDataString(projectName);
        string team = string.IsNullOrWhiteSpace(teamName) ? string.Empty : "/" + Uri.EscapeDataString(teamName);
        string query = string.Join("&", fields.Select(pair =>
            $"{EscapeForUrl($"[{pair.Key.ToApiString()}]")}={EscapeForUrl(pair.Value)}")).Replace("%20", "+", StringComparison.Ordinal);
        return new Uri($"{baseUri}/{project}{team}/_workItems/create/Bug?{query}", UriKind.Absolute);
    }

    public static string EscapeForUrl(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(character switch
            {
                '\u00a0' => ' ',
                '\u00b7' => '-',
                <= '\u007f' => character,
                _ => '?',
            });
        }

        return Uri.EscapeDataString(builder.ToString());
    }

    public static string RemoveInternalMetadata(string inputHtml, string internalIssueId)
    {
        ArgumentNullException.ThrowIfNull(inputHtml);
        if (string.IsNullOrEmpty(internalIssueId))
        {
            return inputHtml;
        }

        string pattern = $@"<div\b[^>]*>.*?{Regex.Escape(internalIssueId)}.*?</div>";
        return Regex.Replace(
            inputHtml,
            pattern,
            "<div></div>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
    }

    public static string WrapInHtmlBody(string innerHtml) => $"<body>{innerHtml}</body>";

    private static Dictionary<string, string> ToTemplateValues(IssueInformation issue)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WindowTitle"] = Value(issue.WindowTitle),
            ["Glimpse"] = Truncate(issue.Glimpse, 50) ?? UnknownValue,
            ["HowToFixLink"] = Value(issue.HowToFixLink?.ToString()),
            ["HelpURL"] = Value(issue.HelpUri?.ToString()),
            ["RuleSource"] = RemoveSurroundingBrackets(issue.RuleSource) ?? UnknownValue,
            ["RuleDescription"] = Value(issue.RuleDescription),
            ["TestMessages"] = Truncate(issue.TestMessages, 150, ConcatenationMessage) ?? UnknownValue,
            ["ProcessName"] = Truncate(issue.ProcessName, 50, ".exe") ?? UnknownValue,
            ["ScreenshotLink"] = string.Empty,
            ["InternalGuid"] = issue.InternalGuid?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty,
            ["ElementPath"] = Value(issue.ElementPath),
            ["RuleForTelemetry"] = Value(issue.RuleForTelemetry),
            ["UIFramework"] = Value(issue.UIFramework),
            ["ContrastRatio"] = Value(issue.ContrastRatio),
            ["FirstColorHex"] = Value(issue.FirstColorHex),
            ["SecondColorHex"] = Value(issue.SecondColorHex),
            ["ContrastFailureText"] = Value(issue.ContrastFailureText),
        };
    }

    private static string Populate(string template, IReadOnlyDictionary<string, string> values)
    {
        foreach ((string name, string value) in values)
        {
            template = template.Replace($"@[{name}]@", value ?? UnknownValue, StringComparison.Ordinal);
        }
        return template;
    }

    private static string? RemoveSurroundingBrackets(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith('[') && value.EndsWith(']')
            ? value[1..^1]
            : value;
    }

    private static string? Truncate(string? value, int limit, string suffix = "...")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length > limit ? value[..limit] + suffix : value;
    }

    private static string Value(string? value) => value ?? string.Empty;

    private static string RemoveTemplateIndentation(string value)
    {
        string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => line.TrimStart()));
    }
}
