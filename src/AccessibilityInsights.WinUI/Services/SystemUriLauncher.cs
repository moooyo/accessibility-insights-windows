// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.IssueReporting;
using Windows.System;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class SystemUriLauncher : IExternalUriLauncher
{
    public async Task LaunchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await Launcher.LaunchUriAsync(uri))
        {
            throw new InvalidOperationException("Windows could not open the requested address.");
        }
    }
}
