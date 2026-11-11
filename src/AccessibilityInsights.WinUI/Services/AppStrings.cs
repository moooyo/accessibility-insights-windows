// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.Windows.ApplicationModel.Resources;

namespace AccessibilityInsights_WinUI.Services;

internal sealed class AppStrings
{
    private readonly Lazy<ResourceLoader> loader = new(() => new ResourceLoader());

    public string Get(string key)
    {
        // PRI represents x:Uid property keys as resource subpaths.
        var value = loader.Value.GetString(key.Replace('.', '/'));
        return !string.IsNullOrEmpty(value) ? value : throw new KeyNotFoundException($"Missing resource: {key}");
    }
}
