// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace AccessibilityInsights_VersionSwitcher_WinUI;

public sealed partial class MainPage : Page
{
    private bool started;
    public UpdateViewModel ViewModel { get; } = App.Services.GetRequiredService<UpdateViewModel>();

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnPropertyChanged;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UpdateViewModel.Status))
        {
            FrameworkElementAutomationPeer.FromElement(StatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (started)
        {
            return;
        }

        started = true;
        await ViewModel.RunCommand.ExecuteAsync(null);
        if (!string.IsNullOrEmpty(ViewModel.Error))
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = App.GetString("InstallError"),
                Content = ViewModel.Error,
                CloseButtonText = App.GetString("Close"),
            }.ShowAsync();
        }

        App.Window.Close();
    }
}
