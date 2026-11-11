// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights_WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using WinUIEx;

namespace AccessibilityInsights_WinUI;

public sealed partial class MainWindow : WindowEx
{
    public MainPage Shell => (MainPage)RootFrame.Content;

    public MainWindow()
    {
        InitializeComponent();
        Title = App.Services.GetRequiredService<AppStrings>().Get("ApplicationTitle");
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "BrandIconDesktop.ico"));
        RootFrame.Navigate(typeof(MainPage));
    }
}
