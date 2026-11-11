// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace AccessibilityInsights.Core.Updates;

public interface IStartupUpdateInstaller
{
    Task InstallAsync(UpdateManifest manifest, CancellationToken cancellationToken);
}
