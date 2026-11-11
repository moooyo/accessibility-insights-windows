// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.Settings;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccessibilityInsights.Core.IssueReporting;

public sealed class IssueReportingService : IIssueReportingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IAppSettingsService settings;
    private readonly IReadOnlyList<IIssueReportingTelemetry> telemetry;
    private readonly Dictionary<Guid, IIssueReporter> reporters;
    private bool initialized;

    public IssueReportingService(
        IAppSettingsService settings,
        IEnumerable<IIssueReporter> reporters,
        IEnumerable<IIssueReportingTelemetry>? telemetry = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(reporters);
        var orderedReporters = reporters
            .Where(reporter => reporter is not null && reporter.StableIdentifier != Guid.Empty)
            .ToArray();
        this.reporters = [];
        foreach (IIssueReporter reporter in orderedReporters)
        {
            if (!this.reporters.TryAdd(reporter.StableIdentifier, reporter))
            {
                throw new ArgumentException(
                    $"Duplicate issue reporter identifier: {reporter.StableIdentifier:D}",
                    nameof(reporters));
            }
        }
        this.telemetry = telemetry?.ToArray() ?? [];
        Reporters = orderedReporters;
    }

    public IReadOnlyList<IIssueReporter> Reporters { get; }
    public IIssueReporter? SelectedReporter { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        AppSettings appSettings = settings.IsInitialized
            ? settings.GetCurrent()
            : await settings.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, string> configurations = ParseConfigurations(appSettings.IssueReporterSerializedConfigs);
        foreach (IIssueReporter reporter in Reporters)
        {
            configurations.TryGetValue(reporter.StableIdentifier, out string? serialized);
            if (reporter.StableIdentifier == IssueReporterIds.AzureBoards &&
                string.IsNullOrWhiteSpace(serialized))
            {
                serialized = GetLegacyAzureBoardsConfiguration(appSettings);
            }
            reporter.RestoreConfiguration(serialized);
        }

        Select(appSettings.SelectedIssueReporter);
        initialized = true;
    }

    public IIssueReporter? Select(Guid reporterId)
    {
        reporters.TryGetValue(reporterId, out IIssueReporter? reporter);
        SelectedReporter = reporter;
        return reporter;
    }

    public async Task SaveSelectedConfigurationAsync(CancellationToken cancellationToken = default)
    {
        IIssueReporter reporter = SelectedReporter ??
            throw new InvalidOperationException("Select an issue reporter before saving its configuration.");
        string serialized = reporter.SerializeConfiguration();
        await settings.UpdateAsync(appSettings =>
        {
            Dictionary<Guid, string> configurations =
                ParseConfigurations(appSettings.IssueReporterSerializedConfigs);
            configurations[reporter.StableIdentifier] = serialized;
            appSettings.IssueReporterSerializedConfigs =
                JsonSerializer.Serialize(configurations, SerializerOptions);
            appSettings.SelectedIssueReporter = reporter.StableIdentifier;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IssueReportResult?> FileIssueAsync(
        IssueInformation issue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);
        IIssueReporter reporter = SelectedReporter ??
            throw new InvalidOperationException("Select an issue reporter before filing an issue.");

        foreach (IIssueReportingTelemetry sink in telemetry)
        {
            sink.ReportAttempt(reporter.StableIdentifier, issue);
        }

        try
        {
            IssueReportResult? result =
                await reporter.FileIssueAsync(issue, cancellationToken).ConfigureAwait(false);
            await SaveSelectedConfigurationAsync(cancellationToken).ConfigureAwait(false);
            foreach (IIssueReportingTelemetry sink in telemetry)
            {
                sink.ReportCompleted(reporter.StableIdentifier, issue, result);
            }
            return result;
        }
        catch (Exception exception)
        {
            foreach (IIssueReportingTelemetry sink in telemetry)
            {
                sink.ReportFailure(reporter.StableIdentifier, issue, exception);
            }
            throw;
        }
    }

    internal static Dictionary<Guid, string> ParseConfigurations(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, string>>(serialized, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? GetLegacyAzureBoardsConfiguration(AppSettings settings)
    {
        JsonObject source = settings.AdditionalSettings;
        if (!source.ContainsKey("ZoomLevel") &&
            !source.ContainsKey("SerializedSavedConnection") &&
            !source.ContainsKey("SerializedCachedConnections"))
        {
            return null;
        }

        var legacy = new JsonObject();
        foreach (string key in new[]
        {
            "ZoomLevel",
            "SerializedSavedConnection",
            "SerializedCachedConnections",
        })
        {
            if (source.TryGetPropertyValue(key, out JsonNode? value))
            {
                legacy[key] = value?.DeepClone();
            }
        }

        return legacy.ToJsonString();
    }
}
