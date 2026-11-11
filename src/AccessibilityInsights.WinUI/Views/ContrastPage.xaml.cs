// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class ContrastPage : Page
{
    private bool updatingControls;
    public ContrastViewModel ViewModel { get; } = App.Services.GetRequiredService<ContrastViewModel>();

    public ContrastPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ViewModel.PropertyChanged += OnPropertyChanged;
            UpdateControls();
        };
        Unloaded += (_, _) =>
        {
            CancelPicking();
            ViewModel.PropertyChanged -= OnPropertyChanged;
        };
    }

    private void OnColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!updatingControls && ForegroundPicker is not null && BackgroundPicker is not null)
        {
            var foreground = ForegroundPicker.Color;
            var background = BackgroundPicker.Color;
            ViewModel.Foreground = new RgbColor(foreground.R, foreground.G, foreground.B);
            ViewModel.Background = new RgbColor(background.R, background.G, background.B);
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ContrastViewModel.Foreground) or nameof(ContrastViewModel.Background))
        {
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        updatingControls = true;
        try
        {
            var foreground = ViewModel.Foreground;
            var background = ViewModel.Background;
            ForegroundPicker.Color = Windows.UI.Color.FromArgb(255, foreground.Red, foreground.Green, foreground.Blue);
            BackgroundPicker.Color = Windows.UI.Color.FromArgb(255, background.Red, background.Green, background.Blue);
        }
        finally
        {
            updatingControls = false;
        }
    }

    private void CancelPicking()
    {
        ViewModel.PickForegroundCommand.Cancel();
        ViewModel.PickBackgroundCommand.Cancel();
    }
}
