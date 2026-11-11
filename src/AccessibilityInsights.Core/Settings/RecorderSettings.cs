// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Axe.Windows.Core.Types;
using Axe.Windows.Desktop.Types;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccessibilityInsights.Core.Settings;

public enum RecordEntityType
{
    Event,
    Property,
}

public enum RecorderListenScope
{
    None,
    Element,
    Descendants,
    Subtree,
}

public sealed class RecordEntitySetting
{
    private int checkedCount;

    public RecordEntityType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Id { get; set; }
    public bool IsRecorded { get; set; }
    public bool IsCustom { get; set; }

    [JsonIgnore]
    public int CheckedCount
    {
        get => checkedCount;
        set
        {
            if (value >= 0)
            {
                checkedCount = value;
            }
        }
    }
}

public sealed class RecorderSettings
{
    public string? Version { get; set; }
    public string? AppVersion { get; set; }
    public List<RecordEntitySetting> Events { get; set; } = [];
    public List<RecordEntitySetting> Properties { get; set; } = [];
    public bool IsListeningFocusChangedEvent { get; set; }
    public bool IsListeningAllEvents { get; set; }
    public RecorderListenScope ListenScope { get; set; }

    public static RecorderSettings CreateDefault(string appVersion) => new()
    {
        AppVersion = appVersion,
        Events =
        [
            .. EventType.GetInstance().GetKeyValuePairList().Select(item => new RecordEntitySetting
            {
                Type = RecordEntityType.Event,
                Id = item.Key,
                Name = item.Value,
                IsRecorded = false,
            }),
        ],
        Properties =
        [
            .. PropertyType.GetInstance().GetKeyValuePairList().Select(item => new RecordEntitySetting
            {
                Type = RecordEntityType.Property,
                Id = item.Key,
                Name = item.Value,
                IsRecorded = false,
            }),
        ],
        IsListeningFocusChangedEvent = true,
        IsListeningAllEvents = false,
        ListenScope = RecorderListenScope.Subtree,
    };
}

public interface IRecorderSettingsService
{
    Task<RecorderSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RecorderSettings settings, CancellationToken cancellationToken = default);
}

public sealed class RecorderSettingsService : IRecorderSettingsService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly SettingsPaths paths;
    private readonly string appVersion;

    public RecorderSettingsService(SettingsPaths paths, IAppSettingsService appSettings)
        : this(paths, appSettings?.CreateDefault().AppVersion ??
            throw new ArgumentNullException(nameof(appSettings)))
    {
    }

    public RecorderSettingsService(SettingsPaths paths, string appVersion)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));
    }

    public async Task<RecorderSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.ConfigurationFolder);
        try
        {
            MigrateLegacyFile();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("{0}", exception);
        }
        RecorderSettings settings;
        try
        {
            settings = File.Exists(paths.EventConfigurationFile)
                ? JsonSerializer.Deserialize<RecorderSettings>(
                    await File.ReadAllTextAsync(paths.EventConfigurationFile, cancellationToken).ConfigureAwait(false),
                    SerializerOptions) ??
                    throw new JsonException("The recorder settings document is empty.")
                : RecorderSettings.CreateDefault(appVersion);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Backup(paths.EventConfigurationFile);
            settings = RecorderSettings.CreateDefault(appVersion);
        }

        settings.Events ??= [];
        settings.Properties ??= [];
        foreach ((int id, string name) in EventType.GetInstance().GetKeyValuePairList())
        {
            if (settings.Events.All(setting => setting.Id != id))
            {
                settings.Events.Add(new RecordEntitySetting
                {
                    Type = RecordEntityType.Event,
                    Id = id,
                    Name = name,
                    IsRecorded = false,
                });
            }
        }
        settings.IsListeningAllEvents = false;
        await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    public async Task SaveAsync(
        RecorderSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(paths.ConfigurationFolder);
        string temporaryPath = paths.EventConfigurationFile + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions),
                Utf8WithoutBom,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, paths.EventConfigurationFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void MigrateLegacyFile()
    {
        string legacyPath = Path.Combine(paths.UserDataFolder, SettingsPaths.EventConfigurationFileName);
        if (File.Exists(legacyPath) && !File.Exists(paths.EventConfigurationFile))
        {
            Directory.CreateDirectory(paths.ConfigurationFolder);
            File.Move(legacyPath, paths.EventConfigurationFile);
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
