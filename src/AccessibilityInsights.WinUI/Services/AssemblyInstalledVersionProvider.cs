// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.Updates;
using System.Reflection;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class AssemblyInstalledVersionProvider : IInstalledVersionProvider
{
    public Version GetInstalledVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ??
            throw new InvalidOperationException("The application entry assembly is unavailable.");
        return assembly.GetName().Version ??
            throw new InvalidOperationException("The application version metadata is unavailable.");
    }
}
