// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.Extensions.DependencyInjection;
using WinUIEx;

namespace AccessibilityInsights_VersionSwitcher_WinUI;

public sealed partial class MainWindow : WindowEx
{
    public MainWindow()
    {
        InitializeComponent();
        Title = App.GetString("UpdaterTitle");
        AppWindow.Closing += (_, args) =>
            args.Cancel = App.Services.GetRequiredService<UpdateViewModel>().IsBusy;
        RootFrame.Navigate(typeof(MainPage));
    }
}
