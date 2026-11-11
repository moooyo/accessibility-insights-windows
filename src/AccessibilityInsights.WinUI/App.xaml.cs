// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core;
using AccessibilityInsights.Core.IssueReporting;
using AccessibilityInsights.Core.Settings;
using AccessibilityInsights.Core.Updates;
using AccessibilityInsights_WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Globalization;
using Windows.ApplicationModel.Activation;
using Windows.Graphics;

namespace AccessibilityInsights_WinUI;

public partial class App : Application
{
    private bool closing;
    private bool shutdownComplete;
    private readonly LaunchOptions launchOptions;
    private readonly Exception? startupError;
    private WindowLayoutSettings? windowLayout;
    private ReleaseChannel releaseChannel;
    private readonly CancellationTokenSource startupCancellation = new();
    private Task startupTask = Task.CompletedTask;
    private ContentDialog? startupDialog;
    public static MainWindow Window { get; private set; } = null!;
    public static ServiceProvider Services { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        InitializeComponent();
        var uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The application UI dispatcher is unavailable.");
        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            launchOptions = activation.Kind == ExtendedActivationKind.File &&
                activation.Data is IFileActivatedEventArgs fileActivation
                ? new LaunchOptions
                {
                    FileToOpen = fileActivation.Files.Count == 1
                        ? fileActivation.Files[0].Path
                        : throw new ArgumentException("Open one accessibility file at a time."),
                }
                : activation.Data is ILaunchActivatedEventArgs platformLaunch
                    ? LaunchOptions.ParseRawArguments(platformLaunch.Arguments)
                    : LaunchOptions.Parse(Environment.GetCommandLineArgs().Skip(1));
            launchOptions.Validate();
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0}", exception);
            startupError = exception;
            launchOptions = new LaunchOptions();
        }

        Services = new ServiceCollection()
            .AddSingleton(launchOptions)
            .AddSingleton<AppStrings>()
            .AddSingleton(provider => new SettingsPaths(provider.GetRequiredService<LaunchOptions>()))
            .AddSingleton<IAppSettingsService>(provider =>
                new AppSettingsService(provider.GetRequiredService<SettingsPaths>()))
            .AddSingleton<IWindowLayoutSettingsService, WindowLayoutSettingsService>()
            .AddSingleton<IRecorderSettingsService, RecorderSettingsService>()
            .AddSingleton<IReleaseChannelChangeService, ReleaseChannelChangeService>()
            .AddSingleton<IUpdateCheckPolicy, AllowAllUpdateCheckPolicy>()
            .AddSingleton<IUpdateManifestProvider, SetupLibraryUpdateManifestProvider>()
            .AddSingleton<IStartupUpdateChecker, StartupUpdateChecker>()
            .AddSingleton<IInstalledVersionProvider, AssemblyInstalledVersionProvider>()
            .AddSingleton<IStartupUpdateDecisionService, StartupUpdateDecisionService>()
            .AddSingleton<IStartupUpdateInstaller, StartupUpdateInstaller>()
            .AddSingleton<InspectionService>()
            .AddSingleton<IInspectionService>(provider => provider.GetRequiredService<InspectionService>())
            .AddSingleton<ILiveInspectionService>(provider => provider.GetRequiredService<InspectionService>())
            .AddSingleton<IInspectionImagingService>(provider => provider.GetRequiredService<InspectionService>())
            .AddSingleton<IRecordingService>(provider => provider.GetRequiredService<InspectionService>())
            .AddSingleton(new DispatcherQueueService(uiDispatcherQueue))
            .AddSingleton<IUiDispatcher>(provider => provider.GetRequiredService<DispatcherQueueService>())
            .AddSingleton<IRecordingFilePicker, RecordingFilePicker>()
            .AddSingleton<LiveInspectionCoordinator>()
            .AddSingleton<ILiveHotkeyService, LiveHotkeyService>()
            .AddSingleton<IScreenshotFileSaver, ScreenshotFileSaver>()
            .AddSingleton<ISnapshotFilePicker, SnapshotFilePicker>()
            .AddSingleton<IScreenColorPicker, ScreenColorPicker>()
            .AddSingleton<IExternalUriLauncher, SystemUriLauncher>()
            .AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>()
            .AddSingleton<IAzureDevOpsIssueBrowser, AzureDevOpsIssueBrowser>()
            .AddSingleton(provider => new GitHubIssueReporter(
                provider.GetRequiredService<IExternalUriLauncher>(),
                provider.GetRequiredService<AppStrings>().Get("IssueReporterGitHubName")))
            .AddSingleton(provider => new AzureDevOpsIssueReporter(
                provider.GetRequiredService<IAzureDevOpsClient>(),
                provider.GetRequiredService<IAzureDevOpsIssueBrowser>(),
                provider.GetRequiredService<AppStrings>().Get("IssueReporterAzureBoardsName")))
            .AddSingleton<IIssueReporter>(provider => provider.GetRequiredService<GitHubIssueReporter>())
            .AddSingleton<IIssueReporter>(provider => provider.GetRequiredService<AzureDevOpsIssueReporter>())
            .AddSingleton<IssueReportingTelemetry>()
            .AddSingleton<IIssueReportingTelemetry>(provider => provider.GetRequiredService<IssueReportingTelemetry>())
            .AddSingleton<IApplicationTelemetry>(provider => provider.GetRequiredService<IssueReportingTelemetry>())
            .AddSingleton<IIssueReportingService, IssueReportingService>()
            .AddSingleton(provider => new ContrastViewModel(
                provider.GetRequiredService<IScreenColorPicker>(),
                provider.GetRequiredService<AppStrings>().Get))
            .AddSingleton(provider => new WorkbenchViewModel(
                provider.GetRequiredService<IInspectionService>(),
                provider.GetRequiredService<ISnapshotFilePicker>(),
                provider.GetRequiredService<AppStrings>().Get))
            .AddSingleton(provider => new RecordingViewModel(
                provider.GetRequiredService<IRecordingService>(),
                provider.GetRequiredService<IRecordingFilePicker>(),
                provider.GetRequiredService<WorkbenchViewModel>(),
                provider.GetRequiredService<AppStrings>().Get,
                provider.GetRequiredService<IUiDispatcher>()))
            .AddSingleton<ISettingsActivationService, WinUISettingsActivationService>()
            .AddSingleton(provider => new SettingsViewModel(
                provider.GetRequiredService<IAppSettingsService>(),
                provider.GetRequiredService<AppStrings>().Get))
            .AddSingleton(provider => new IssueReportingViewModel(
                provider.GetRequiredService<IIssueReportingService>(),
                provider.GetRequiredService<AppStrings>().Get))
            .BuildServiceProvider();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.AppWindow.Closing += OnClosing;
        ((FrameworkElement)Window.Content).Loaded += OnRootLoaded;
        Window.Activate();
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        var root = (FrameworkElement)Window.Content;
        root.Loaded -= OnRootLoaded;
        startupTask = InitializeApplicationAsync(root, startupCancellation.Token);
        await startupTask;
    }

    private async Task InitializeApplicationAsync(FrameworkElement root, CancellationToken cancellationToken)
    {
        root.IsHitTestVisible = false;
        try
        {
            if (startupError is not null)
            {
                throw startupError;
            }

            launchOptions.AttachDebugger();
            var settings = Services.GetRequiredService<IAppSettingsService>();
            settings.SettingsChanged += OnSettingsChanged;
            AppSettings appSettings = await settings.InitializeAsync();
            Services.GetRequiredService<IApplicationTelemetry>().PublishEvent("Mainwindow_Startup");
            releaseChannel = appSettings.ReleaseChannel;
            foreach (ISettingsActivationService activationService in
                Services.GetServices<ISettingsActivationService>())
            {
                await activationService.ApplyAsync(
                    appSettings,
                    SettingsActivationReason.Startup,
                    CancellationToken.None);
            }

            await Services.GetRequiredService<IRecorderSettingsService>().LoadAsync();
            await Services.GetRequiredService<IIssueReportingService>().InitializeAsync();
            windowLayout = await Services.GetRequiredService<IWindowLayoutSettingsService>().LoadAsync(
                Window.AppWindow.Position.Y,
                Window.AppWindow.Position.X);
            ApplyWindowLayout(windowLayout);

            cancellationToken.ThrowIfCancellationRequested();
            if (!await CheckForStartupUpdateAsync(cancellationToken))
            {
                return;
            }

            if (launchOptions.FileToOpen is { } path)
            {
                if (string.Equals(Path.GetExtension(path), ".a11yevent", StringComparison.OrdinalIgnoreCase))
                {
                    Window.Shell.NavigateSection("Events");
                    await Services.GetRequiredService<RecordingViewModel>().OpenFileAsync(path);
                }
                else
                {
                    await Services.GetRequiredService<WorkbenchViewModel>().OpenFileAsync(path);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the window cancels startup before services are disposed.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0}", exception);
            Environment.ExitCode = 1;
            if (!closing)
            {
                var strings = Services.GetRequiredService<AppStrings>();
                await ShowStartupDialogAsync(new ContentDialog
                {
                    XamlRoot = root.XamlRoot,
                    Title = strings.Get("StartupFailed"),
                    Content = exception.Message,
                    CloseButtonText = strings.Get("Close"),
                });
                Window.Close();
            }
        }
        finally
        {
            if (!closing)
            {
                root.IsHitTestVisible = true;
            }
        }
    }

    private async Task<bool> CheckForStartupUpdateAsync(CancellationToken cancellationToken)
    {
        UpdateCheckResult result;
        var strings = Services.GetRequiredService<AppStrings>();
        try
        {
            result = await Services.GetRequiredService<IStartupUpdateDecisionService>()
                .CheckAsync(cancellationToken)
                .WaitAsync(StartupUpdateCheckDefaults.DecisionTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Trace.TraceError("{0}", exception);
            Services.GetRequiredService<WorkbenchViewModel>().ReportError(
                new InvalidOperationException(strings.Get("StartupUpdateCheckFailed.Message"), exception));
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!result.IsUpgradeAvailable)
        {
            return true;
        }

        var manifest = result.Manifest
            ?? throw new InvalidDataException("An available update must include its verified manifest.");
        var required = result.Option == AutoUpdateOption.RequiredUpgrade;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture,
                strings.Get("StartupUpdateAvailableVersion"), manifest.CurrentVersion),
            TextWrapping = TextWrapping.Wrap,
        });
        if (required)
        {
            content.Children.Add(new TextBlock
            {
                Text = strings.Get("StartupUpdateRequiredDescription"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        var notes = new HyperlinkButton
        {
            Content = strings.Get("StartupUpdateReleaseNotes.Content"),
            NavigateUri = manifest.ReleaseNotesUri,
        };
        AutomationProperties.SetAutomationId(notes, "StartupUpdateReleaseNotes");
        content.Children.Add(notes);
        var dialog = new ContentDialog
        {
            XamlRoot = Window.Content.XamlRoot,
            Title = strings.Get(required ? "StartupUpdateRequired.Title" : "StartupUpdateOptional.Title"),
            Content = content,
            PrimaryButtonText = strings.Get("StartupUpdateNow.Content"),
            CloseButtonText = strings.Get(required ? "StartupUpdateClose.Content" : "StartupUpdateLater.Content"),
            DefaultButton = ContentDialogButton.Primary,
        };
        AutomationProperties.SetAutomationId(dialog, "StartupUpdateDialog");
        AutomationProperties.SetName(dialog, strings.Get("StartupUpdateAutomationName"));
        var choice = await ShowStartupDialogAsync(dialog);
        cancellationToken.ThrowIfCancellationRequested();
        if (choice != ContentDialogResult.Primary)
        {
            if (required)
            {
                Window.Close();
                return false;
            }
            Services.GetRequiredService<IApplicationTelemetry>().PublishEvent(
                "Upgrade_Update_Dismiss",
                new Dictionary<string, string> { ["MSIVersion"] = result.InstalledVersion.ToString() });
            return true;
        }

        try
        {
            await Services.GetRequiredService<IStartupUpdateInstaller>().InstallAsync(manifest, cancellationToken);
            Window.Close();
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Trace.TraceError("{0}", exception);
            Services.GetRequiredService<WorkbenchViewModel>().ReportError(exception);
            if (required)
            {
                await ShowStartupDialogAsync(new ContentDialog
                {
                    XamlRoot = Window.Content.XamlRoot,
                    Title = strings.Get("StartupUpdateRequired.Title"),
                    Content = strings.Get("StartupUpdateRequiredFailure"),
                    CloseButtonText = strings.Get("StartupUpdateClose.Content"),
                });
                Window.Close();
                return false;
            }
            return true;
        }
    }

    private async Task<ContentDialogResult> ShowStartupDialogAsync(ContentDialog dialog)
    {
        startupDialog = dialog;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            startupDialog = null;
        }
    }

    private async void OnSettingsChanged(object? sender, AppSettingsChangeSet args)
    {
        try
        {
            var changingChannel = args.ChangedKeys.Contains("ReleaseChannel");
            if (changingChannel)
            {
                foreach (IReleaseChannelChangeService releaseService in
                    Services.GetServices<IReleaseChannelChangeService>())
                {
                    await releaseService.ChangeChannelAsync(
                        releaseChannel,
                        args.Settings.ReleaseChannel,
                        CancellationToken.None);
                }
                releaseChannel = args.Settings.ReleaseChannel;
            }

            foreach (ISettingsActivationService activationService in
                Services.GetServices<ISettingsActivationService>())
            {
                await activationService.ApplyAsync(
                    args.Settings,
                    SettingsActivationReason.UserSave,
                    CancellationToken.None);
            }
            if (changingChannel)
            {
                Window.Close();
            }
        }
        catch (Exception exception)
        {
            Services.GetRequiredService<WorkbenchViewModel>().ReportError(exception);
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (shutdownComplete)
        {
            return;
        }

        args.Cancel = true;
        if (closing)
        {
            return;
        }

        closing = true;
        ((FrameworkElement)Window.Content).IsHitTestVisible = false;
        startupDialog?.Hide();
        var strings = Services.GetRequiredService<AppStrings>();
        try
        {
            await startupCancellation.CancelAsync();
            await startupTask;
            Services.GetRequiredService<IAppSettingsService>().SettingsChanged -= OnSettingsChanged;
            if (windowLayout is not null)
            {
                CaptureWindowLayout(windowLayout);
                await Services.GetRequiredService<IWindowLayoutSettingsService>().SaveAsync(windowLayout);
            }
            await Services.DisposeAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0}", exception);
            var dialog = new ContentDialog
            {
                XamlRoot = Window.Content.XamlRoot,
                Title = strings.Get("ShutdownFailed"),
                Content = exception.Message,
                CloseButtonText = strings.Get("Close"),
            };
            await dialog.ShowAsync();
        }
        finally
        {
            startupCancellation.Dispose();
            shutdownComplete = true;
            Window.Close();
        }
    }

    private static void ApplyWindowLayout(WindowLayoutSettings layout)
    {
        Window.Width = Math.Max(Window.MinWidth, layout.Width);
        Window.Height = Math.Max(Window.MinHeight, layout.Height);
        Window.AppWindow.Move(new PointInt32((int)layout.Left, (int)layout.Top));
        if (Window.AppWindow.Presenter is OverlappedPresenter presenter &&
            layout.WinState == SavedWindowState.Maximized)
        {
            presenter.Maximize();
        }
    }

    private static void CaptureWindowLayout(WindowLayoutSettings layout)
    {
        layout.Left = Window.AppWindow.Position.X;
        layout.Top = Window.AppWindow.Position.Y;
        layout.Width = Window.Width;
        layout.Height = Window.Height;
        layout.WinState = Window.AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Maximized
            ? SavedWindowState.Maximized
            : SavedWindowState.Normal;
    }
}
