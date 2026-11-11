// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.IssueReporting;
using AccessibilityInsights.Core.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class IssueReportingTelemetry :
    IApplicationTelemetry,
    IIssueReportingTelemetry,
    IDisposable,
    IAsyncDisposable
{
    private const string ConnectionString = "InstrumentationKey=0ad67074-7af0-494c-adee-be70a786448a;IngestionEndpoint=https://southcentralus-0.in.applicationinsights.azure.com/;LiveEndpoint=https://southcentralus.livediagnostics.monitor.azure.com/";
    private const string InstallationInfoFileName = "InstallationInfo.json";
    private readonly IAppSettingsService settings;
    private readonly TelemetryConfiguration configuration;
    private readonly TelemetryClient client;
    private readonly Dictionary<string, string> context = new(StringComparer.Ordinal);
    private readonly object lifecycleGate = new();
    private readonly bool groupPolicyAllowsTelemetry;
    private int disposeState;

    public IssueReportingTelemetry(IAppSettingsService settings)
    {
        this.settings = settings;
        groupPolicyAllowsTelemetry = DoesGroupPolicyAllowTelemetry();
        configuration = new TelemetryConfiguration { ConnectionString = ConnectionString };
        client = new TelemetryClient(configuration);
        client.Context.Device.OperatingSystem = GetWindowsVersion();
        client.Context.Cloud.RoleInstance = "undefined";
        context["InstallationID"] = GetMonthlyInstallationId(settings.Paths.ConfigurationFolder);
        context["Version"] = settings.CreateDefault().AppVersion;
        context["AppSessionID"] = Guid.NewGuid().ToString("D");
        context["SessionType"] = "Desktop";
    }

    public bool IsEnabled =>
        Volatile.Read(ref disposeState) == 0 &&
        groupPolicyAllowsTelemetry &&
        settings.IsInitialized &&
        settings.GetCurrent().EnableTelemetry;

    public void AddOrUpdateContextProperty(string propertyName, string propertyValue)
    {
        if (!string.IsNullOrWhiteSpace(propertyName) && !string.IsNullOrWhiteSpace(propertyValue))
        {
            lock (context)
            {
                context[propertyName] = propertyValue;
            }
        }
    }

    public void PublishEvent(
        string eventName,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        try
        {
            lock (lifecycleGate)
            {
                if (Volatile.Read(ref disposeState) != 0)
                {
                    return;
                }
                client.TrackEvent(eventName, MergeProperties(properties));
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Telemetry event failed: {0}", exception);
        }
    }

    public void ReportException(
        Exception exception,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (!IsEnabled || exception is null)
        {
            return;
        }

        try
        {
            lock (lifecycleGate)
            {
                if (Volatile.Read(ref disposeState) != 0)
                {
                    return;
                }
                client.TrackException(exception, MergeProperties(properties));
            }
        }
        catch (Exception telemetryException)
        {
            Trace.TraceError("Telemetry exception reporting failed: {0}", telemetryException);
        }
    }

    public void ReportAttempt(Guid reporterId, IssueInformation issue)
    {
        PublishEvent("Issue_File_Attempt", CreateIssueProperties(reporterId, issue));
    }

    public void ReportCompleted(Guid reporterId, IssueInformation issue, IssueReportResult? result)
    {
        string eventName = result?.IssueLink is null ? "Issue_File_Attempt" : "Issue_Save";
        var properties = CreateIssueProperties(reporterId, issue);
        if (result?.IssueLink is not null && !string.IsNullOrWhiteSpace(issue.RuleForTelemetry))
        {
            properties["RuleId"] = issue.RuleForTelemetry;
            properties["UIFramework"] = issue.UIFramework ?? string.Empty;
        }
        PublishEvent(eventName, properties);
    }

    public void ReportFailure(Guid reporterId, IssueInformation issue, Exception exception)
    {
        ReportException(exception, CreateIssueProperties(reporterId, issue));
    }

    public void FlushAndShutDown()
    {
        lock (lifecycleGate)
        {
            if (Volatile.Read(ref disposeState) != 0)
            {
                return;
            }
            client.Flush();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        lock (lifecycleGate)
        {
            try
            {
                client.Flush();
            }
            finally
            {
                configuration.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private Dictionary<string, string> MergeProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        Dictionary<string, string> merged;
        lock (context)
        {
            merged = new Dictionary<string, string>(context, StringComparer.Ordinal);
        }
        merged["ReleaseChannel"] = settings.GetCurrent().ReleaseChannel.ToString();
        if (properties is not null)
        {
            foreach ((string key, string value) in properties)
            {
                merged[key] = value;
            }
        }
        return merged;
    }

    private static Dictionary<string, string> CreateIssueProperties(
        Guid reporterId,
        IssueInformation issue) => new(StringComparer.Ordinal)
        {
            ["IssueReporter"] = reporterId == IssueReporterIds.GitHub
            ? "GitHub"
            : reporterId == IssueReporterIds.AzureBoards
                ? "Azure Boards"
                : reporterId.ToString("D"),
            ["RuleId"] = issue.RuleForTelemetry ?? string.Empty,
            ["UIFramework"] = issue.UIFramework ?? string.Empty,
        };

    private static bool DoesGroupPolicyAllowTelemetry()
    {
        try
        {
            object? policyValue = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\Software\Policies\Accessibility Insights for Windows",
                "DisableTelemetry",
                0);
            return policyValue is not int value || value != 1;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Telemetry group policy check failed: {0}", exception);
            return false;
        }
    }

    private static string GetWindowsVersion()
    {
        try
        {
            const string key = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion";
            string version = Registry.GetValue(key, "CurrentVersion", string.Empty) as string ?? string.Empty;
            string build = Registry.GetValue(key, "CurrentBuild", string.Empty) as string ?? string.Empty;
            return string.IsNullOrEmpty(version)
                ? "unknown"
                : string.IsNullOrEmpty(build) ? version : $"{version}.{build}";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string GetMonthlyInstallationId(string configurationFolder)
    {
        Directory.CreateDirectory(configurationFolder);
        string path = Path.Combine(configurationFolder, InstallationInfoFileName);
        InstallationInfo info;
        try
        {
            info = JsonSerializer.Deserialize<InstallationInfo>(
                File.ReadAllText(path, Encoding.UTF8)) ?? new InstallationInfo();
        }
        catch
        {
            info = new InstallationInfo();
        }

        DateTime now = DateTime.UtcNow;
        if (info.LastReset.Month != now.Month || info.LastReset.Year != now.Year)
        {
            info = new InstallationInfo { InstallationGuid = Guid.NewGuid(), LastReset = now };
        }

        try
        {
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Telemetry installation identifier could not be saved: {0}", exception);
        }
        return info.InstallationGuid.ToString("D");
    }

    private sealed class InstallationInfo
    {
        public Guid InstallationGuid { get; set; } = Guid.NewGuid();
        public DateTime LastReset { get; set; } = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
