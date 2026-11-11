// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.Settings;
using AccessibilityInsights_WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace AccessibilityInsights_WinUI.Views;

public sealed partial class InspectPage : Page
{
    private readonly LiveInspectionCoordinator coordinator =
        App.Services.GetRequiredService<LiveInspectionCoordinator>();
    private readonly ILiveHotkeyService hotkeys =
        App.Services.GetRequiredService<ILiveHotkeyService>();
    private readonly IScreenshotFileSaver screenshotSaver =
        App.Services.GetRequiredService<IScreenshotFileSaver>();
    private readonly IAppSettingsService settings =
        App.Services.GetRequiredService<IAppSettingsService>();
    private readonly RecordingViewModel recording =
        App.Services.GetRequiredService<RecordingViewModel>();
    private readonly AppStrings strings = App.Services.GetRequiredService<AppStrings>();
    private bool initializing = true;
    private bool hotkeysConfigured;

    public WorkbenchViewModel ViewModel { get; } = App.Services.GetRequiredService<WorkbenchViewModel>();

    public InspectPage()
    {
        InitializeComponent();
        coordinator.Attach(DispatcherQueue);
        SelectionScope.SelectedIndex = ViewModel.SelectionScope == LiveSelectionScope.Element ? 0 : 1;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        settings.SettingsChanged += OnSettingsChanged;
        Loaded += OnLoaded;
        initializing = false;
        ViewModel.RefreshWindowsCommand.Execute(null);
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (hotkeysConfigured)
        {
            return;
        }

        hotkeysConfigured = true;
        try
        {
            ConfigureHotkeys(await settings.InitializeAsync());
        }
        catch (Exception exception)
        {
            ViewModel.ReportError(exception);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangeSet args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ConfigureHotkeys(args.Settings);
            }
            catch (Exception exception)
            {
                ViewModel.ReportError(exception);
            }
        });

    private void ConfigureHotkeys(AppSettings appSettings)
    {
        var failures = hotkeys.Configure(
            App.WindowHandle,
            new LiveHotkeyConfiguration(
                appSettings.HotKeyForPause!,
                appSettings.HotKeyForRecord!,
                appSettings.HotKeyForSnap!,
                appSettings.HotKeyForActivatingMainWindow!,
                appSettings.HotKeyForMoveToParent!,
                appSettings.HotKeyForMoveToFirstChild!,
                appSettings.HotKeyForMoveToLastChild!,
                appSettings.HotKeyForMoveToNextSibling!,
                appSettings.HotKeyForMoveToPreviousSibling!),
            new LiveHotkeyActions(
                () => ExecuteCommand(ViewModel.TogglePauseCommand),
                ToggleEventRecording,
                RunTestsFromHotkey,
                ToggleWindowActivation,
                () => ExecuteCommand(ViewModel.NavigateCommand, TreeNavigationDirection.Parent),
                () => ExecuteCommand(ViewModel.NavigateCommand, TreeNavigationDirection.FirstChild),
                () => ExecuteCommand(ViewModel.NavigateCommand, TreeNavigationDirection.LastChild),
                () => ExecuteCommand(ViewModel.NavigateCommand, TreeNavigationDirection.NextSibling),
                () => ExecuteCommand(ViewModel.NavigateCommand, TreeNavigationDirection.PreviousSibling)));
        if (failures.Count > 0)
        {
            var details = string.Join(
                Environment.NewLine,
                failures.Select(failure => $"{failure.Gesture}: {failure.Error}"));
            throw new InvalidOperationException(string.Format(
                CultureInfo.CurrentCulture,
                strings.Get("HotkeyRegistrationFailed"),
                details));
        }
    }

    private void ToggleEventRecording()
    {
        if (recording.StopEventsCommand.CanExecute(null))
        {
            recording.StopEventsCommand.Execute(null);
        }
        else
        {
            recording.StartEventsCommand.Execute(null);
        }
    }

    private async void OnElementSelected(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        try
        {
            await ViewModel.SelectElementAsync(sender.SelectedItem as ElementSnapshot);
        }
        catch (Exception exception)
        {
            ViewModel.ReportError(exception);
        }
    }

    private async void OnSelectionScopeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (initializing)
        {
            return;
        }

        var scope = SelectionScope.SelectedIndex == 1
            ? LiveSelectionScope.Application
            : LiveSelectionScope.Element;
        await ViewModel.ApplyLiveInspectionOptionsAsync(new LiveInspectionOptions(
            scope,
            GetSelectionMethods(),
            ViewModel.MouseSelectionDelayMilliseconds));
    }

    private async void OnSelectionMethodChanged(object sender, RoutedEventArgs args)
    {
        if (initializing)
        {
            return;
        }

        var methods = GetSelectionMethods();
        if (methods == LiveSelectionMethods.None)
        {
            ((CheckBox)sender).IsChecked = true;
            ViewModel.ReportError(new InvalidOperationException(strings.Get("InvalidSelectionMethods")));
            return;
        }

        await ViewModel.ApplyLiveInspectionOptionsAsync(new LiveInspectionOptions(
            ViewModel.SelectionScope,
            methods,
            ViewModel.MouseSelectionDelayMilliseconds));
    }

    private LiveSelectionMethods GetSelectionMethods() =>
        (MouseSelection.IsChecked == true ? LiveSelectionMethods.Mouse : LiveSelectionMethods.None) |
        (FocusSelection.IsChecked == true ? LiveSelectionMethods.KeyboardFocus : LiveSelectionMethods.None);

    private void OnNavigateClicked(object sender, RoutedEventArgs args)
    {
        if (((AppBarButton)sender).Tag is string value &&
            Enum.TryParse<TreeNavigationDirection>(value, out var direction))
        {
            ExecuteCommand(ViewModel.NavigateCommand, direction);
        }
    }

    private async void OnSaveScreenshotClicked(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Screenshot is not InspectionScreenshot screenshot)
        {
            return;
        }

        try
        {
            if (await screenshotSaver.SaveAsync(screenshot))
            {
                ViewModel.Status = strings.Get("ScreenshotSaved");
            }
        }
        catch (Exception exception)
        {
            ViewModel.ReportError(exception);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        var propertyName = args.PropertyName;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyViewModelChange(propertyName);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => ApplyViewModelChange(propertyName));
        }
    }

    private void ApplyViewModelChange(string? propertyName)
    {
        if (propertyName == nameof(WorkbenchViewModel.SelectionScope))
        {
            initializing = true;
            SelectionScope.SelectedIndex = ViewModel.SelectionScope == LiveSelectionScope.Element ? 0 : 1;
            initializing = false;
        }
        else if (propertyName == nameof(WorkbenchViewModel.Screenshot))
        {
            _ = UpdateScreenshotAsync();
        }
    }

    private async Task UpdateScreenshotAsync()
    {
        try
        {
            await ScreenshotPreview.SetScreenshotAsync(ViewModel.Screenshot);
        }
        catch (Exception exception)
        {
            ViewModel.ReportError(exception);
        }
    }

    private static void ExecuteCommand(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private void RunTestsFromHotkey()
    {
        ExecuteCommand(ViewModel.TestCommand);
        ActivateWindow();
        Hierarchy.Focus(FocusState.Programmatic);
    }

    private static void ActivateWindow()
    {
        if (App.Window.AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        App.Window.Activate();
        SetForegroundWindow(App.WindowHandle);
    }

    private static void ToggleWindowActivation()
    {
        var handle = App.WindowHandle;
        if (App.Window.AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            ActivateWindow();
            return;
        }

        if (GetForegroundWindow() == handle)
        {
            presenter.Minimize();
        }
        else
        {
            ActivateWindow();
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
