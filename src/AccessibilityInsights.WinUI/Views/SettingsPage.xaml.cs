// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.Settings;
using AccessibilityInsights_WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using WinUIEx;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class SettingsPage : Page
{
    private bool initialized;
    private bool updatingDelay;

    public SettingsViewModel ViewModel { get; } = App.Services.GetRequiredService<SettingsViewModel>();

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await ViewModel.InitializeAsync();
        UpdateMouseDelay();
    }

    private void OnMouseDelayChanged(NumberBox<int> sender, NumberBoxValueChangedEventArgs<int> args)
    {
        if (!updatingDelay && args.NewValue.HasValue)
        {
            ViewModel.MouseSelectionDelayMilliSeconds = args.NewValue.Value;
        }
    }

    private void OnShortcutKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            e.Key is VirtualKey.Escape or VirtualKey.Tab or
            VirtualKey.Menu or VirtualKey.Control or VirtualKey.Shift or
            VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        var modifiers = new List<string>(4);
        AddModifier(modifiers, VirtualKey.Menu, "Alt");
        AddModifier(modifiers, VirtualKey.Control, "Control");
        AddModifier(modifiers, VirtualKey.Shift, "Shift");
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers.Add("Windows");
        }

        if (modifiers.Count == 0)
        {
            return;
        }

        string key = GetLegacyKeyName(e.Key);
        textBox.Text = $"{string.Join(",", modifiers)} + {key}";
        e.Handled = true;
    }

    private async void OnResetClicked(object sender, RoutedEventArgs e)
    {
        var strings = App.Services.GetRequiredService<AppStrings>();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = strings.Get("SettingsResetDialogTitle"),
            Content = strings.Get("SettingsResetDialogMessage"),
            PrimaryButtonText = strings.Get("SettingsResetDialogPrimary"),
            CloseButtonText = strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ResetCommand.Execute(null);
            UpdateMouseDelay();
        }
    }

    private void UpdateMouseDelay()
    {
        updatingDelay = true;
        try
        {
            MouseDelay.Value = ViewModel.MouseSelectionDelayMilliSeconds;
        }
        finally
        {
            updatingDelay = false;
        }
    }

    private static void AddModifier(ICollection<string> modifiers, VirtualKey key, string name)
    {
        if (IsDown(key))
        {
            modifiers.Add(name);
        }
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static string GetLegacyKeyName(VirtualKey key)
    {
        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            return ((int)key - (int)VirtualKey.Number0)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
        {
            return $"NumPad{(int)key - (int)VirtualKey.NumberPad0}";
        }

        return key switch
        {
            VirtualKey.CapitalLock => "CapsLock",
            VirtualKey.Snapshot => "PrintScreen",
            _ => key.ToString(),
        };
    }
}
