// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AccessibilityInsights.Core.Settings;

public enum SettingsActivationReason
{
    Startup,
    UserSave,
}

public interface ISettingsActivationService
{
    ValueTask ApplyAsync(AppSettings settings, SettingsActivationReason reason, CancellationToken cancellationToken);
}

public interface IReleaseChannelChangeService
{
    Task ChangeChannelAsync(ReleaseChannel currentChannel, ReleaseChannel requestedChannel, CancellationToken cancellationToken);
}

public sealed record AppSettingsChangeSet(AppSettings Settings, IReadOnlySet<string> ChangedKeys);

public interface IAppSettingsService
{
    SettingsPaths Paths { get; }
    bool IsInitialized { get; }
    event EventHandler<AppSettingsChangeSet>? SettingsChanged;

    Task<AppSettings> InitializeAsync(CancellationToken cancellationToken = default);
    AppSettings GetCurrent();
    Task<AppSettingsChangeSet> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<AppSettingsChangeSet> UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default);
    AppSettings CreateDefault();
}
