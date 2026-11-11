// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.Settings;
using System.Diagnostics;

namespace AccessibilityInsights.Core.Updates;

public enum AutoUpdateOption
{
    Unknown,
    NewerThanCurrent,
    Current,
    OptionalUpgrade,
    RequiredUpgrade,
}

public static class StartupUpdateCheckDefaults
{
    public static TimeSpan DecisionTimeout { get; } = TimeSpan.FromSeconds(2);
}

public sealed record UpdateManifestMetadata(
    Uri RequestUri,
    Uri ResponseUri,
    int DataByteCount,
    bool IsSignatureVerified);

public sealed record UpdateManifest(
    Version CurrentVersion,
    Version MinimumVersion,
    Uri InstallerUri,
    Uri ReleaseNotesUri,
    Version ProductionMinimumVersion,
    int MsiSizeInBytes,
    string MsiSha512,
    UpdateManifestMetadata Metadata)
{
    public void Validate()
    {
        if (CurrentVersion < MinimumVersion)
        {
            throw new InvalidDataException("The update manifest current version is below its minimum version.");
        }
        if (!InstallerUri.IsAbsoluteUri || !ReleaseNotesUri.IsAbsoluteUri)
        {
            throw new InvalidDataException("The update manifest must contain absolute installer and release-notes addresses.");
        }
        if (!Metadata.IsSignatureVerified)
        {
            throw new InvalidDataException("The update manifest signature was not verified.");
        }
        if (!Metadata.RequestUri.IsAbsoluteUri || !Metadata.ResponseUri.IsAbsoluteUri ||
            Metadata.DataByteCount <= 0)
        {
            throw new InvalidDataException("The update manifest download metadata is invalid.");
        }
    }
}

public sealed record UpdateCheckPolicyDecision(bool IsAllowed, string? Reason)
{
    public static UpdateCheckPolicyDecision Allowed { get; } = new(true, null);
}

public sealed record UpdateCheckResult(
    AutoUpdateOption Option,
    ReleaseChannel ReleaseChannel,
    Version InstalledVersion,
    UpdateManifest? Manifest,
    TimeSpan Elapsed,
    UpdateCheckPolicyDecision Policy)
{
    public bool IsUpgradeAvailable =>
        Option is AutoUpdateOption.OptionalUpgrade or AutoUpdateOption.RequiredUpgrade;
}

public interface IUpdateManifestProvider
{
    Task<UpdateManifest> GetManifestAsync(
        ReleaseChannel releaseChannel,
        CancellationToken cancellationToken);
}

public interface IUpdateCheckPolicy
{
    UpdateCheckPolicyDecision GetDecision();
}

public interface IStartupUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(
        ReleaseChannel releaseChannel,
        Version installedVersion,
        CancellationToken cancellationToken = default);
}

public interface IInstalledVersionProvider
{
    Version GetInstalledVersion();
}

public interface IStartupUpdateDecisionService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class AllowAllUpdateCheckPolicy : IUpdateCheckPolicy
{
    public UpdateCheckPolicyDecision GetDecision() => UpdateCheckPolicyDecision.Allowed;
}

public sealed class StartupUpdateChecker : IStartupUpdateChecker
{
    private readonly IUpdateManifestProvider manifestProvider;
    private readonly IUpdateCheckPolicy policy;

    public StartupUpdateChecker(
        IUpdateManifestProvider manifestProvider,
        IUpdateCheckPolicy policy)
    {
        this.manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public async Task<UpdateCheckResult> CheckAsync(
        ReleaseChannel releaseChannel,
        Version installedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        cancellationToken.ThrowIfCancellationRequested();
        UpdateCheckPolicyDecision policyDecision = policy.GetDecision();
        if (!policyDecision.IsAllowed)
        {
            return new UpdateCheckResult(
                AutoUpdateOption.Unknown,
                releaseChannel,
                installedVersion,
                null,
                TimeSpan.Zero,
                policyDecision);
        }

        var stopwatch = Stopwatch.StartNew();
        UpdateManifest manifest = await manifestProvider
            .GetManifestAsync(releaseChannel, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();
        manifest.Validate();

        AutoUpdateOption option = installedVersion < manifest.MinimumVersion
            ? AutoUpdateOption.RequiredUpgrade
            : installedVersion < manifest.CurrentVersion
                ? AutoUpdateOption.OptionalUpgrade
                : installedVersion > manifest.CurrentVersion
                    ? AutoUpdateOption.NewerThanCurrent
                    : AutoUpdateOption.Current;

        return new UpdateCheckResult(
            option,
            releaseChannel,
            installedVersion,
            manifest,
            stopwatch.Elapsed,
            policyDecision);
    }
}

public sealed class StartupUpdateDecisionService : IStartupUpdateDecisionService
{
    private readonly IStartupUpdateChecker checker;
    private readonly IAppSettingsService settings;
    private readonly IInstalledVersionProvider installedVersion;

    public StartupUpdateDecisionService(
        IStartupUpdateChecker checker,
        IAppSettingsService settings,
        IInstalledVersionProvider installedVersion)
    {
        this.checker = checker ?? throw new ArgumentNullException(nameof(checker));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.installedVersion = installedVersion ?? throw new ArgumentNullException(nameof(installedVersion));
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        AppSettings appSettings = settings.IsInitialized
            ? settings.GetCurrent()
            : await settings.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await checker.CheckAsync(
            appSettings.ReleaseChannel,
            installedVersion.GetInstalledVersion(),
            cancellationToken).ConfigureAwait(false);
    }
}
