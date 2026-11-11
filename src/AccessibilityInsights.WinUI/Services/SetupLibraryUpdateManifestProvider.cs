// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.Settings;
using AccessibilityInsights.Core.Updates;
using AccessibilityInsights.SetupLibrary;
using System.Diagnostics;
using CoreReleaseChannel = AccessibilityInsights.Core.Settings.ReleaseChannel;
using SetupReleaseChannel = AccessibilityInsights.SetupLibrary.ReleaseChannel;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class SetupLibraryUpdateManifestProvider(
    IApplicationTelemetry telemetry) : IUpdateManifestProvider
{
    public Task<UpdateManifest> GetManifestAsync(
        CoreReleaseChannel releaseChannel,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reporter = new UpdateExceptionReporter(telemetry);
            if (!ChannelInfoUtilities.TryGetChannelInfo(
                    MapChannel(releaseChannel),
                    out EnrichedChannelInfo channelInfo,
                    gitHubWrapper: null,
                    exceptionReporter: reporter))
            {
                throw new InvalidOperationException(
                    "The signed update manifest could not be loaded or verified.",
                    reporter.LastException);
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (channelInfo is null || !channelInfo.IsValid || channelInfo.Metadata is null)
            {
                throw new InvalidDataException("The signed update manifest is incomplete.");
            }
            if (!Uri.TryCreate(channelInfo.InstallAsset, UriKind.Absolute, out Uri? installerUri) ||
                !Uri.TryCreate(channelInfo.ReleaseNotesAsset, UriKind.Absolute, out Uri? releaseNotesUri))
            {
                throw new InvalidDataException("The signed update manifest contains invalid addresses.");
            }

            return new UpdateManifest(
                channelInfo.CurrentVersion,
                channelInfo.MinimumVersion,
                installerUri,
                releaseNotesUri,
                channelInfo.ProductionMinimumVersion,
                channelInfo.MsiSizeInBytes,
                channelInfo.MsiSha512,
                new UpdateManifestMetadata(
                    channelInfo.Metadata.RequestUri,
                    channelInfo.Metadata.ResponseUri,
                    channelInfo.Metadata.DataByteCount,
                    IsSignatureVerified: true));
        }, cancellationToken);

    private static SetupReleaseChannel MapChannel(CoreReleaseChannel releaseChannel) =>
        releaseChannel switch
        {
            CoreReleaseChannel.Insider => SetupReleaseChannel.Insider,
            CoreReleaseChannel.Canary => SetupReleaseChannel.Canary,
            _ => SetupReleaseChannel.Production,
        };

    private sealed class UpdateExceptionReporter(IApplicationTelemetry telemetry) : IExceptionReporter
    {
        public Exception? LastException { get; private set; }

        public void ReportException(Exception exception)
        {
            LastException = exception;
            Trace.TraceError("Update manifest retrieval failed: {0}", exception);
            telemetry.ReportException(exception, new Dictionary<string, string>
            {
                ["Operation"] = "StartupUpdateCheck",
            });
        }
    }
}
