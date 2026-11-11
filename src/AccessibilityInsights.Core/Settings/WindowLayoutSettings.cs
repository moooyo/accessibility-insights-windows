// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Text;
using System.Text.Json;

namespace AccessibilityInsights.Core.Settings;

public enum SavedWindowState
{
    Normal,
    Minimized,
    Maximized,
}

public sealed class MainWindowLayout
{
    public string? Version { get; set; }
    public string? AppVersion { get; set; }
    public double Height { get; set; }
    public double Width { get; set; }
    public double ColumnSnapWidth { get; set; }
    public double RowPropertiesHeight { get; set; }
    public double RowPatternsHeight { get; set; }
    public double RowTestHeight { get; set; }
    public SavedWindowState WinState { get; set; }

    public static MainWindowLayout CreateLive(string appVersion) => new()
    {
        AppVersion = appVersion,
        Width = 460,
        Height = 500,
        ColumnSnapWidth = 410,
        RowPropertiesHeight = 46 * 7,
        RowPatternsHeight = 46 * 3,
        RowTestHeight = 0,
        WinState = SavedWindowState.Normal,
    };

    public static MainWindowLayout CreateSnapshot(string appVersion) => new()
    {
        AppVersion = appVersion,
        Width = 870,
        Height = 720,
        ColumnSnapWidth = 410,
        RowPropertiesHeight = 46 * 7,
        RowPatternsHeight = 46 * 3,
        RowTestHeight = 260,
        WinState = SavedWindowState.Normal,
    };
}

public sealed class WindowLayoutSettings
{
    public const string CurrentVersion = "1.0.5";

    public string? Version { get; set; }
    public string? AppVersion { get; set; }
    public MainWindowLayout LayoutSnapshot { get; set; } = new();
    public MainWindowLayout LayoutLive { get; set; } = new();
    public MainWindowLayout LayoutEvent { get; set; } = new();
    public double Left { get; set; }
    public double Top { get; set; }
    public double Height { get; set; }
    public double Width { get; set; }
    public SavedWindowState WinState { get; set; }

    public static WindowLayoutSettings CreateDefault(double top, double left, string appVersion) => new()
    {
        Version = CurrentVersion,
        AppVersion = appVersion,
        Top = top,
        Left = left,
        Width = 870,
        Height = 720,
        WinState = SavedWindowState.Normal,
        LayoutLive = MainWindowLayout.CreateLive(appVersion),
        LayoutSnapshot = MainWindowLayout.CreateSnapshot(appVersion),
        LayoutEvent = MainWindowLayout.CreateSnapshot(appVersion),
    };

    public void UpgradeIfNeeded(double top, double left, string appVersion)
    {
        if (string.Equals(Version, CurrentVersion, StringComparison.Ordinal))
        {
            return;
        }

        WindowLayoutSettings defaults = CreateDefault(top, left, appVersion);
        Version = defaults.Version;
        AppVersion = defaults.AppVersion;
        Top = defaults.Top;
        Left = defaults.Left;
        Width = defaults.Width;
        Height = defaults.Height;
        WinState = defaults.WinState;
        LayoutLive = defaults.LayoutLive;
        LayoutSnapshot = defaults.LayoutSnapshot;
        LayoutEvent = defaults.LayoutEvent;
    }
}

public interface IWindowLayoutSettingsService
{
    Task<WindowLayoutSettings> LoadAsync(
        double currentTop,
        double currentLeft,
        CancellationToken cancellationToken = default);
    Task SaveAsync(WindowLayoutSettings settings, CancellationToken cancellationToken = default);
}

public sealed class WindowLayoutSettingsService : IWindowLayoutSettingsService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly SettingsPaths paths;
    private readonly string appVersion;

    public WindowLayoutSettingsService(SettingsPaths paths, IAppSettingsService appSettings)
        : this(paths, appSettings?.CreateDefault().AppVersion ??
            throw new ArgumentNullException(nameof(appSettings)))
    {
    }

    public WindowLayoutSettingsService(SettingsPaths paths, string appVersion)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));
    }

    public async Task<WindowLayoutSettings> LoadAsync(
        double currentTop,
        double currentLeft,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.ConfigurationFolder);
        WindowLayoutSettings settings;
        try
        {
            settings = File.Exists(paths.LayoutFile)
                ? JsonSerializer.Deserialize<WindowLayoutSettings>(
                    await File.ReadAllTextAsync(paths.LayoutFile, cancellationToken).ConfigureAwait(false),
                    SerializerOptions) ??
                    throw new JsonException("The layout settings document is empty.")
                : WindowLayoutSettings.CreateDefault(currentTop, currentLeft, appVersion);
            settings.UpgradeIfNeeded(currentTop, currentLeft, appVersion);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Backup(paths.LayoutFile);
            settings = WindowLayoutSettings.CreateDefault(currentTop, currentLeft, appVersion);
        }

        await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    public async Task SaveAsync(
        WindowLayoutSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(paths.ConfigurationFolder);
        string temporaryPath = paths.LayoutFile + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions),
                Utf8WithoutBom,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, paths.LayoutFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Backup(string path)
    {
        if (File.Exists(path))
        {
            File.Move(path, $"{path}.{DateTime.Now.Ticks}.bak");
        }
    }
}
