// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.Updates;
using AccessibilityInsights.SetupLibrary;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class StartupUpdateInstaller : IStartupUpdateInstaller
{
    public Task InstallAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.Metadata.IsSignatureVerified)
        {
            throw new InvalidDataException("The update manifest signature has not been verified.");
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            VersionSwitcherWrapper.InstallUpgrade(
                manifest.InstallerUri,
                manifest.MsiSizeInBytes,
                manifest.MsiSha512);
        }, cancellationToken);
    }
}
