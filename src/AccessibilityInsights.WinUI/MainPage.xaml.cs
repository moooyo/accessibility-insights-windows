// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using AccessibilityInsights.Models;
using AccessibilityInsights.Services;
using AccessibilityInsights.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AccessibilityInsights;

public sealed partial class MainPage : Page
{
    private readonly InspectionService inspection = new();
    private readonly DispatcherQueueTimer timer;
    private bool polling;
    private bool closing;
    private bool recordingTabStops;
    private readonly NativeHighlighter highlighter = new();
    private readonly SettingsStore settings = new(Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json"));
    private GlobalHotkeys? hotkeys;
    private readonly ResourceLoader resources = new();

    public MainPageViewModel ViewModel { get; }

    public MainPage()
    {
        ViewModel = new(resources.GetString);
        InitializeComponent();
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.Tick += Poll_Tick;
        inspection.EventReceived += OnEventReceived;
        inspection.TabStopReceived += OnTabStopReceived;
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.Error))
            {
                ErrorBar.IsOpen = ViewModel.Error.Length > 0;
            }
        };
        Navigation.SelectedItem = InspectNavigation;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= Page_Loaded;
        App.Window.AppWindow.Closing += Window_Closing;
        await ExecuteAsync(() =>
        {
            var stored = settings.Load();
            PopulateSettings(stored);
            ApplySettings(stored);
            return Task.CompletedTask;
        });
    }

    private void PopulateSettings(UserSettings stored)
    {
        ThemePicker.SelectedIndex = stored.Theme;
        FocusSelection.IsOn = stored.SelectByFocus;
        ApplicationScope.IsOn = stored.ApplicationScope;
        AlwaysOnTop.IsOn = stored.AlwaysOnTop;
        PauseHotkey.Text = stored.PauseHotkey;
        RecordHotkey.Text = stored.RecordHotkey;
        ScanHotkey.Text = stored.ScanHotkey;
        GitHubRepository.Text = stored.GitHubRepository;
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await PauseAsync();
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            PopulateSettings(SettingsStore.ReadLegacy(file.Path, settings.Load()));
            ViewModel.Status = resources.GetString("LegacySettingsImported");
        }
    });

    private void ApplySettings(UserSettings stored)
    {
        SettingsStore.Validate(stored);
        hotkeys ??= new GlobalHotkeys(App.WindowHandle);
        hotkeys.Configure([
            (Hotkey.Parse(stored.PauseHotkey), () => Selection_Click(this, new())),
            (Hotkey.Parse(stored.RecordHotkey), () =>
            {
                if (TestPanel.Visibility == Visibility.Visible && TestPanel.SelectedIndex == 1)
                {
                    TabStopRecord_Click(this, new());
                }
                else { Record_Click(this, new()); }
            }),
            (Hotkey.Parse(stored.ScanHotkey), () => RunChecks_Click(this, new())),
        ]);
        if (App.Window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = stored.AlwaysOnTop;
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(() =>
    {
        var stored = new UserSettings
        {
            Theme = ThemePicker.SelectedIndex,
            SelectByFocus = FocusSelection.IsOn,
            ApplicationScope = ApplicationScope.IsOn,
            AlwaysOnTop = AlwaysOnTop.IsOn,
            PauseHotkey = PauseHotkey.Text,
            RecordHotkey = RecordHotkey.Text,
            ScanHotkey = ScanHotkey.Text,
            GitHubRepository = GitHubRepository.Text,
        };
        ApplySettings(stored);
        settings.Save(stored);
        ViewModel.Status = resources.GetString("SettingsSaved");
        return Task.CompletedTask;
    });

    private async void Poll_Tick(DispatcherQueueTimer sender, object args)
    {
        if (polling || ViewModel.IsBusy || !ViewModel.IsSelecting || closing)
        {
            return;
        }

        polling = true;
        try
        {
            await ExecuteAsync(async () =>
            {
                var snapshot = await inspection.PollAsync(FocusSelection.IsOn, ApplicationScope.IsOn);
                if (snapshot is not null && !closing)
                {
                    ViewModel.UpdateSnapshot(snapshot);
                    if (HighlightToggle.IsChecked == true) { highlighter.Show(snapshot.Bounds); }
                }
            }, false);
        }
        finally
        {
            polling = false;
        }
    }

    private async void Selection_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (ViewModel.IsSelecting)
            {
                await PauseAsync();
            }
            else
            {
                await StopRecordingAsync();
                await inspection.StartSelectionAsync(FocusSelection.IsOn, ApplicationScope.IsOn);
                ViewModel.ClearSelection();
                ScreenshotImage.Source = null;
                highlighter.Hide();
                ViewModel.IsSelecting = true;
                SelectionButton.Label = resources.GetString("PauseSelection");
                timer.Start();
            }
        });
    }

    private async Task PauseAsync()
    {
        timer.Stop();
        ViewModel.IsSelecting = false;
        SelectionButton.Label = resources.GetString("StartSelection");
        await inspection.PauseAsync();
    }

    private async void RunChecks_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            await PauseAsync();
            await StopRecordingAsync();
            ViewModel.UpdateSnapshot(await inspection.ScanAsync());
            TestPanel.SelectedIndex = 0;
            Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>()
                .Single(item => item.Tag as string == "Test");
        });
    }

    private async void ElementList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsSelecting || ViewModel.IsBusy || ElementList.SelectedItem is not ElementRow element)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            MainPageViewModel.Replace(ViewModel.Properties, await inspection.GetPropertiesAsync(element.Id));
            if (HighlightToggle.IsChecked == true) { highlighter.Show(element.Bounds); }
        });
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            await PauseAsync();
            await StopRecordingAsync();
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".a11ytest");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                ViewModel.UpdateSnapshot(await inspection.LoadAsync(file.Path));
                await ShowScreenshotAsync(false);
            }
        });
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            await PauseAsync();
            var picker = new FileSavePicker { SuggestedFileName = "AccessibilityInsights" };
            picker.FileTypeChoices.Add("Accessibility Insights snapshot", [".a11ytest"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is not null)
            {
                await inspection.SaveAsync(file.Path);
                ViewModel.Status = FormatResource("SavedFile", file.Name);
            }
        });
    }

    private async void FileIssue_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await PauseAsync();
        await StopRecordingAsync();
        GitHubIssueService.ValidateRepository(GitHubRepository.Text);
        var rule = TestPanel.Visibility == Visibility.Visible && TestPanel.SelectedIndex == 0 ?
            CheckResults.SelectedItem as RuleRow : null;
        var draft = GitHubIssueService.CreateDraft(ViewModel.SelectedProcessName,
            ViewModel.SelectedElementName, rule, resources.GetString);
        var body = new TextBox
        {
            Text = draft.Body,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 380,
        };
        AutomationProperties.SetAutomationId(body, "IssueDraftBody");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = draft.Title,
            Content = body,
            PrimaryButtonText = resources.GetString("OpenGitHub"),
            CloseButtonText = resources.GetString("Cancel"),
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(
                GitHubIssueService.GetNewIssueUri(GitHubRepository.Text, draft)))
            {
                throw new InvalidOperationException("Windows could not open the GitHub issue draft in your browser.");
            }
        }
    });

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            await PauseAsync();
            if (ViewModel.IsRecording)
            {
                await StopRecordingAsync();
            }
            else
            {
                ViewModel.Events.Clear();
                recordingTabStops = false;
                await inspection.StartRecordingAsync();
                ViewModel.IsRecording = true;
                RecordButton.Content = resources.GetString("StopRecording");
            }
        });
    }

    private async Task StopRecordingAsync()
    {
        await inspection.StopRecordingAsync();
        ViewModel.IsRecording = false;
        recordingTabStops = false;
        RecordButton.Content = resources.GetString("StartRecording");
        TabStopRecordButton.Content = resources.GetString("StartTabRecording");
    }

    private async void TabStopRecord_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await PauseAsync();
        if (ViewModel.IsRecording) { await StopRecordingAsync(); return; }
        ViewModel.TabStops.Clear();
        recordingTabStops = true;
        await inspection.StartRecordingAsync(true);
        ViewModel.IsRecording = true;
        TabStopRecordButton.Content = resources.GetString("StopTabRecording");
    });

    private void OnTabStopReceived(TabStopRow row)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (!closing && ViewModel.IsRecording)
            {
                ViewModel.TabStops.Add(row);
                ViewModel.Status = FormatResource("TabStopCount", ViewModel.TabStops.Count);
            }
        }))
        {
            System.Diagnostics.Trace.TraceError("The UI dispatcher rejected a tab-stop update.");
        }
    }

    private async void TabStop_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await ExecuteAsync(() =>
        {
            if ((sender as ListView)?.SelectedItem is TabStopRow row) { highlighter.Show(row.Bounds); }
            return Task.CompletedTask;
        });

    private async void Highlight_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(() =>
    {
        if (HighlightToggle.IsChecked == true && ViewModel.HasSelection) { highlighter.Show(ViewModel.SelectedBounds); }
        else { highlighter.Hide(); }
        return Task.CompletedTask;
    });

    private async void Screenshot_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await PauseAsync();
        highlighter.Hide();
        App.Window.AppWindow.Hide();
        try
        {
            await Task.Delay(150);
            await ShowScreenshotAsync(true);
            ViewModel.Status = resources.GetString("ScreenshotCaptured");
        }
        finally { App.Window.AppWindow.Show(false); }
    });

    private async Task ShowScreenshotAsync(bool capture)
    {
        var bytes = await inspection.GetScreenshotAsync(capture);
        if (bytes is null)
        {
            ScreenshotImage.Source = null;
            if (capture) { throw new InvalidOperationException("No screenshot is available for this element."); }
            return;
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        ScreenshotImage.Source = bitmap;
    }

    private async void DetectColors_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await PauseAsync();
        highlighter.Hide();
        App.Window.AppWindow.Hide();
        try
        {
            await Task.Delay(150);
            var result = await inspection.DetectColorsAsync();
            ViewModel.Foreground = result.Foreground.ToString();
            ViewModel.Background = result.Background.ToString();
            ColorConfidence.Text = FormatResource("ColorConfidence", result.Confidence);
        }
        finally { App.Window.AppWindow.Show(false); }
    });

    private async void OpenEvents_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await StopRecordingAsync();
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".a11yevent");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            MainPageViewModel.Replace(ViewModel.Events, await inspection.LoadEventsAsync(file.Path));
            ViewModel.Status = FormatResource("EventsLoaded", ViewModel.Events.Count);
        }
    });

    private async void SaveEvents_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await StopRecordingAsync();
        if (ViewModel.Events.Count == 0) { throw new InvalidOperationException("Record or open events before saving."); }
        var picker = new FileSavePicker { SuggestedFileName = "AccessibilityInsights" };
        picker.FileTypeChoices.Add("Accessibility Insights events", [".a11yevent"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await inspection.SaveEventsAsync(file.Path);
            ViewModel.Status = FormatResource("SavedFile", file.Name);
        }
    });

    private void OnEventReceived(EventRow row)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (closing || !ViewModel.IsRecording)
            {
                return;
            }

            var truncated = ViewModel.Events.Count >= 10000;
            if (truncated)
            {
                ViewModel.Events.RemoveAt(0);
            }

            ViewModel.Events.Add(row);
            if (!recordingTabStops)
            {
                ViewModel.Status = truncated ? resources.GetString("EventsLimit") :
                    FormatResource("EventCount", ViewModel.Events.Count);
            }
        }))
        {
            System.Diagnostics.Trace.TraceError("The UI dispatcher rejected an event recording update.");
        }
    }

    private string FormatResource(string key, params object[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, resources.GetString(key), arguments);

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (InspectPanel is null)
        {
            return;
        }

        var mode = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        InspectPanel.Visibility = mode == "Inspect" ? Visibility.Visible : Visibility.Collapsed;
        TestPanel.Visibility = mode == "Test" ? Visibility.Visible : Visibility.Collapsed;
        ContrastPanel.Visibility = mode == "Contrast" ? Visibility.Visible : Visibility.Collapsed;
        EventsPanel.Visibility = mode == "Events" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = mode == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (App.Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = ThemePicker.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    private async Task ExecuteAsync(Func<Task> action, bool showBusy = true)
    {
        if (showBusy && ViewModel.IsBusy)
        {
            return;
        }

        if (showBusy)
        {
            ViewModel.IsBusy = true;
        }

        try
        {
            ViewModel.Error = string.Empty;
            await action();
        }
        catch (Exception exception) when (exception is COMException or IOException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or
            Newtonsoft.Json.JsonException or System.Text.Json.JsonException or FormatException or Win32Exception)
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
            ViewModel.Error = exception.Message;
            timer.Stop();
            ViewModel.IsSelecting = false;
            SelectionButton.Label = resources.GetString("StartSelection");
        }
        finally
        {
            if (showBusy)
            {
                ViewModel.IsBusy = false;
            }
        }
    }

    private async void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closing)
        {
            return;
        }

        args.Cancel = true;
        closing = true;
        timer.Stop();
        try
        {
            await inspection.DisposeAsync();
            inspection.EventReceived -= OnEventReceived;
            inspection.TabStopReceived -= OnTabStopReceived;
            highlighter.Dispose();
            hotkeys?.Dispose();
            App.Window.Close();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or Win32Exception)
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
            ViewModel.Error = exception.Message;
            closing = false;
        }
    }
}
