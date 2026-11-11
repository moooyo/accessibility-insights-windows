// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AccessibilityInsights.Core.Settings;

public sealed class SettingsPaths
{
    public const string ConfigurationFileName = "Configuration.json";
    public const string LayoutFileName = "Layout.Json";
    public const string EventConfigurationFileName = "EventConfig.Json";

    public SettingsPaths(LaunchOptions options)
        : this(options?.ConfigFolder, options?.UserDataFolder)
    {
    }

    public SettingsPaths(string? configurationFolderOverride = null, string? userDataFolderOverride = null)
    {
        ConfigurationFolder = Path.GetFullPath(configurationFolderOverride ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AccessibilityInsights", "V1", "Configurations"));
        UserDataFolder = Path.GetFullPath(userDataFolderOverride ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AccessibilityInsights"));
    }

    public string ConfigurationFolder { get; }
    public string UserDataFolder { get; }
    public string ConfigurationFile => Path.Combine(ConfigurationFolder, ConfigurationFileName);
    public string LayoutFile => Path.Combine(ConfigurationFolder, LayoutFileName);
    public string EventConfigurationFile => Path.Combine(ConfigurationFolder, EventConfigurationFileName);
    public string WebView2DataFolder => Path.Combine(ConfigurationFolder, "WebView2");
}
