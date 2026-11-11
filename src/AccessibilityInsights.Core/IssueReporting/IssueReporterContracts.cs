// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AccessibilityInsights.Core.IssueReporting;

public interface IExternalUriLauncher
{
    Task LaunchAsync(Uri uri, CancellationToken cancellationToken);
}

public interface IIssueReporter
{
    Guid StableIdentifier { get; }
    string ServiceName { get; }
    bool IsConfigured { get; }
    bool CanAttachFiles { get; }
    string SerializeConfiguration();
    void RestoreConfiguration(string? serializedConfiguration);
    Task<IssueReportResult?> FileIssueAsync(IssueInformation issue, CancellationToken cancellationToken);
}

public interface IIssueReportingTelemetry
{
    void ReportAttempt(Guid reporterId, IssueInformation issue);
    void ReportCompleted(Guid reporterId, IssueInformation issue, IssueReportResult? result);
    void ReportFailure(Guid reporterId, IssueInformation issue, Exception exception);
}

public interface IIssueReportingService
{
    IReadOnlyList<IIssueReporter> Reporters { get; }
    IIssueReporter? SelectedReporter { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    IIssueReporter? Select(Guid reporterId);
    Task SaveSelectedConfigurationAsync(CancellationToken cancellationToken = default);
    Task<IssueReportResult?> FileIssueAsync(IssueInformation issue, CancellationToken cancellationToken = default);
}

public sealed class IssueReporterAuthenticationRequiredException : InvalidOperationException
{
    public IssueReporterAuthenticationRequiredException(string message)
        : base(message)
    {
    }
}
