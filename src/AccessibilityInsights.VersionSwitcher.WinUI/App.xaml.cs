// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AccessibilityInsights_VersionSwitcher_WinUI;

public partial class App : Application
{
    private static readonly Lazy<ResourceLoader> Strings = new(() => new ResourceLoader());
    public static MainWindow Window { get; private set; } = null!;
    public static ServiceProvider Services { get; private set; } = null!;
    public static string GetString(string key) => Strings.Value.GetString(key);

    public App()
    {
        InitializeComponent();
        Services = new ServiceCollection()
            .AddSingleton<IUpdateService, UpdateService>()
            .AddSingleton(provider => new UpdateViewModel(provider.GetRequiredService<IUpdateService>(), GetString))
            .BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Closed += (_, _) => Services.Dispose();
        Window.Activate();
    }
}
