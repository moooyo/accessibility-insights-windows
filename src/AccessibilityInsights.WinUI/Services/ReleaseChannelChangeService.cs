// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.Settings;
using AccessibilityInsights.SetupLibrary;
using AppReleaseChannel = AccessibilityInsights.Core.Settings.ReleaseChannel;
using InstallerReleaseChannel = AccessibilityInsights.SetupLibrary.ReleaseChannel;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class ReleaseChannelChangeService : IReleaseChannelChangeService
{
    public Task ChangeChannelAsync(
        AppReleaseChannel currentChannel,
        AppReleaseChannel requestedChannel,
        CancellationToken cancellationToken)
    {
        if (currentChannel == requestedChannel)
        {
            return Task.CompletedTask;
        }

        if (!Enum.IsDefined(requestedChannel) ||
            !Enum.TryParse<InstallerReleaseChannel>(requestedChannel.ToString(), out var target))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedChannel));
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            VersionSwitcherWrapper.ChangeChannel(target);
        }, cancellationToken);
    }
}
