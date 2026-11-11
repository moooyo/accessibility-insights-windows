// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace AccessibilityInsights.Core.Settings;

public sealed class AppSettingsService : IAppSettingsService, IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string appVersion;
    private AppSettings? current;
    private bool disposed;

    public AppSettingsService(LaunchOptions options)
        : this(new SettingsPaths(options))
    {
    }

    public AppSettingsService(SettingsPaths paths, string? appVersion = null)
    {
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.appVersion = appVersion ?? GetAppVersion();
    }

    public SettingsPaths Paths { get; }
    public bool IsInitialized => current is not null;
    public event EventHandler<AppSettingsChangeSet>? SettingsChanged;

    public async Task<AppSettings> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current is not null)
            {
                return current.Clone();
            }

            Directory.CreateDirectory(Paths.ConfigurationFolder);
            Directory.CreateDirectory(Paths.UserDataFolder);

            if (!File.Exists(Paths.ConfigurationFile))
            {
                current = CreateDefault();
                await WriteAsync(current, cancellationToken).ConfigureAwait(false);
                return current.Clone();
            }

            try
            {
                string json = await File.ReadAllTextAsync(Paths.ConfigurationFile, cancellationToken).ConfigureAwait(false);
                current = AppSettingsSerializer.Deserialize(json, Paths, appVersion);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                BackupInvalidConfiguration();
                current = CreateDefault();
            }

            await WriteAsync(current, cancellationToken).ConfigureAwait(false);
            return current.Clone();
        }
        finally
        {
            gate.Release();
        }
    }

    public AppSettings GetCurrent()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return (current ?? throw new InvalidOperationException("Settings have not been initialized.")).Clone();
    }

    public async Task<AppSettingsChangeSet> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(disposed, this);
        AppSettingsChangeSet changeSet;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings before = (current ?? throw new InvalidOperationException("Settings have not been initialized.")).Clone();
            AppSettings after = settings.Clone();
            Validate(after);
            after.Version = AppSettings.CurrentVersion;
            after.AppVersion = appVersion;
            await WriteAsync(after, cancellationToken).ConfigureAwait(false);
            current = after;

            changeSet = new AppSettingsChangeSet(after.Clone(), AppSettingsSerializer.GetChangedKeys(before, after));
        }
        finally
        {
            gate.Release();
        }

        SettingsChanged?.Invoke(this, changeSet);
        return changeSet;
    }

    public async Task<AppSettingsChangeSet> UpdateAsync(
        Action<AppSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(disposed, this);
        AppSettingsChangeSet changeSet;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings before = (current ?? throw new InvalidOperationException("Settings have not been initialized.")).Clone();
            AppSettings after = before.Clone();
            update(after);
            Validate(after);
            after.Version = AppSettings.CurrentVersion;
            after.AppVersion = appVersion;
            await WriteAsync(after, cancellationToken).ConfigureAwait(false);
            current = after;

            changeSet = new AppSettingsChangeSet(after.Clone(), AppSettingsSerializer.GetChangedKeys(before, after));
        }
        finally
        {
            gate.Release();
        }

        SettingsChanged?.Invoke(this, changeSet);
        return changeSet;
    }

    public AppSettings CreateDefault() => AppSettings.CreateDefault(Paths, appVersion);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Paths.ConfigurationFolder);
        string temporaryPath = Paths.ConfigurationFile + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                AppSettingsSerializer.Serialize(settings),
                Utf8WithoutBom,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, Paths.ConfigurationFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void BackupInvalidConfiguration()
    {
        if (!File.Exists(Paths.ConfigurationFile))
        {
            return;
        }

        string backupPath = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Paths.ConfigurationFile}.{DateTime.Now.Ticks}.bak");
        File.Move(Paths.ConfigurationFile, backupPath);
    }

    private static void Validate(AppSettings settings)
    {
        string?[] hotKeys =
        [
            settings.HotKeyForActivatingMainWindow,
            settings.HotKeyForRecord,
            settings.HotKeyForPause,
            settings.HotKeyForSnap,
            settings.HotKeyForMoveToParent,
            settings.HotKeyForMoveToPreviousSibling,
            settings.HotKeyForMoveToNextSibling,
            settings.HotKeyForMoveToFirstChild,
            settings.HotKeyForMoveToLastChild,
        ];
        if (hotKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Shortcut values cannot be empty.");
        }

        if (hotKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hotKeys.Length)
        {
            throw new InvalidDataException("Shortcut values must be unique.");
        }
        if (!settings.SelectionByFocus && !settings.SelectionByMouse)
        {
            throw new InvalidDataException("At least one live selection method must be enabled.");
        }

        settings.MouseSelectionDelayMilliSeconds =
            Math.Max(settings.MouseSelectionDelayMilliSeconds, AppSettings.MinimumSelectionDelayMilliseconds);
        settings.CoreProperties ??= [.. AppSettings.DefaultCoreProperties];
        settings.CoreTPAttributes ??= [];
        settings.TestReportPath = Path.GetFullPath(settings.TestReportPath);
        settings.EventRecordPath = Path.GetFullPath(settings.EventRecordPath);
    }

    private static string GetAppVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(AppSettingsService).Assembly;
        return (!string.IsNullOrEmpty(assembly.Location)
                ? FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion
                : null) ??
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            string.Empty;
    }
}
