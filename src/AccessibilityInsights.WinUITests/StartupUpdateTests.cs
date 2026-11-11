// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Core.Settings;
using AccessibilityInsights.Core.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AccessibilityInsights.WinUITests;

[TestClass]
public sealed class StartupUpdateTests
{
    [TestMethod]
    public void StartupDecisionTimeoutPreservesOriginalTwoSecondDefault() =>
        Assert.AreEqual(TimeSpan.FromSeconds(2), StartupUpdateCheckDefaults.DecisionTimeout);

    [DataTestMethod]
    [DataRow("1.0", "2.0", "3.0", AutoUpdateOption.RequiredUpgrade)]
    [DataRow("2.0", "2.0", "3.0", AutoUpdateOption.OptionalUpgrade)]
    [DataRow("3.0", "2.0", "3.0", AutoUpdateOption.Current)]
    [DataRow("4.0", "2.0", "3.0", AutoUpdateOption.NewerThanCurrent)]
    public async Task CheckPreservesOriginalVersionDecisionOrder(
        string installed,
        string minimum,
        string current,
        AutoUpdateOption expected)
    {
        UpdateManifest manifest = CreateManifest(minimum, current);
        var checker = new StartupUpdateChecker(
            new FakeManifestProvider(manifest),
            new AllowAllUpdateCheckPolicy());

        UpdateCheckResult result = await checker.CheckAsync(
            ReleaseChannel.Production,
            Version.Parse(installed));

        Assert.AreEqual(expected, result.Option);
        Assert.AreSame(manifest, result.Manifest);
        Assert.IsTrue(result.Policy.IsAllowed);
        Assert.AreEqual(ReleaseChannel.Production, result.ReleaseChannel);
    }

    [TestMethod]
    public async Task PolicyBlockedReturnsUnknownWithoutFetchingManifest()
    {
        var provider = new FakeManifestProvider(CreateManifest("2.0", "3.0"));
        var checker = new StartupUpdateChecker(
            provider,
            new FakePolicy(new UpdateCheckPolicyDecision(false, "Managed by administrator")));

        UpdateCheckResult result = await checker.CheckAsync(
            ReleaseChannel.Insider,
            new Version(1, 0));

        Assert.AreEqual(AutoUpdateOption.Unknown, result.Option);
        Assert.IsNull(result.Manifest);
        Assert.IsFalse(result.Policy.IsAllowed);
        Assert.AreEqual(0, provider.CallCount);
    }

    [TestMethod]
    public async Task ProviderFailureIsPropagatedInsteadOfReportedAsCurrent()
    {
        var expected = new IOException("manifest unavailable");
        var checker = new StartupUpdateChecker(
            new ThrowingManifestProvider(expected),
            new AllowAllUpdateCheckPolicy());

        IOException actual = await Assert.ThrowsExceptionAsync<IOException>(() =>
            checker.CheckAsync(ReleaseChannel.Canary, new Version(1, 0)));

        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    public async Task UnverifiedManifestIsRejected()
    {
        UpdateManifest manifest = CreateManifest("2.0", "3.0") with
        {
            Metadata = new UpdateManifestMetadata(
                new Uri("https://example.test/request"),
                new Uri("https://example.test/response"),
                100,
                IsSignatureVerified: false),
        };
        var checker = new StartupUpdateChecker(
            new FakeManifestProvider(manifest),
            new AllowAllUpdateCheckPolicy());

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            checker.CheckAsync(ReleaseChannel.Production, new Version(1, 0)));
    }

    [TestMethod]
    public async Task ResultRetainsSignedManifestMetadataForTelemetryAndInstaller()
    {
        UpdateManifest manifest = CreateManifest("2.0", "3.0");
        var checker = new StartupUpdateChecker(
            new FakeManifestProvider(manifest),
            new AllowAllUpdateCheckPolicy());

        UpdateCheckResult result = await checker.CheckAsync(
            ReleaseChannel.Canary,
            new Version(2, 5));

        Assert.AreEqual(new Uri("https://example.test/request"), result.Manifest!.Metadata.RequestUri);
        Assert.AreEqual(new Uri("https://example.test/response"), result.Manifest.Metadata.ResponseUri);
        Assert.AreEqual(100, result.Manifest.Metadata.DataByteCount);
        Assert.AreEqual(1234, result.Manifest.MsiSizeInBytes);
        Assert.AreEqual("sha512", result.Manifest.MsiSha512);
        Assert.IsTrue(result.Elapsed >= TimeSpan.Zero);
    }

    [TestMethod]
    public async Task DecisionServiceUsesPersistedChannelAndPublishedVersionProvider()
    {
        var provider = new FakeManifestProvider(CreateManifest("2.0", "3.0"));
        var checker = new StartupUpdateChecker(provider, new AllowAllUpdateCheckPolicy());
        var settings = new FakeSettingsService(ReleaseChannel.Canary);
        var service = new StartupUpdateDecisionService(
            checker,
            settings,
            new FakeInstalledVersionProvider(new Version(2, 5)));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.AreEqual(ReleaseChannel.Canary, provider.LastChannel);
        Assert.AreEqual(new Version(2, 5), result.InstalledVersion);
        Assert.AreEqual(AutoUpdateOption.OptionalUpgrade, result.Option);
    }

    [TestMethod]
    public async Task CancelledCheckDoesNotFetchManifest()
    {
        var provider = new FakeManifestProvider(CreateManifest("2.0", "3.0"));
        var checker = new StartupUpdateChecker(provider, new AllowAllUpdateCheckPolicy());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            checker.CheckAsync(ReleaseChannel.Production, new Version(1, 0), cancellation.Token));

        Assert.AreEqual(0, provider.CallCount);
    }

    private static UpdateManifest CreateManifest(string minimum, string current) => new(
        Version.Parse(current),
        Version.Parse(minimum),
        new Uri("https://example.test/installer.msi"),
        new Uri("https://example.test/release-notes"),
        Version.Parse(minimum),
        1234,
        "sha512",
        new UpdateManifestMetadata(
            new Uri("https://example.test/request"),
            new Uri("https://example.test/response"),
            100,
            IsSignatureVerified: true));

    private sealed class FakeManifestProvider(UpdateManifest manifest) : IUpdateManifestProvider
    {
        public int CallCount { get; private set; }
        public ReleaseChannel? LastChannel { get; private set; }

        public Task<UpdateManifest> GetManifestAsync(
            ReleaseChannel releaseChannel,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastChannel = releaseChannel;
            return Task.FromResult(manifest);
        }
    }

    private sealed class ThrowingManifestProvider(Exception exception) : IUpdateManifestProvider
    {
        public Task<UpdateManifest> GetManifestAsync(
            ReleaseChannel releaseChannel,
            CancellationToken cancellationToken) =>
            Task.FromException<UpdateManifest>(exception);
    }

    private sealed class FakePolicy(UpdateCheckPolicyDecision decision) : IUpdateCheckPolicy
    {
        public UpdateCheckPolicyDecision GetDecision() => decision;
    }

    private sealed class FakeInstalledVersionProvider(Version version) : IInstalledVersionProvider
    {
        public Version GetInstalledVersion() => version;
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        private readonly AppSettings settings;

        public FakeSettingsService(ReleaseChannel releaseChannel)
        {
            Paths = new SettingsPaths("C:\\config", "C:\\user");
            settings = AppSettings.CreateDefault(Paths, "1.0.0");
            settings.ReleaseChannel = releaseChannel;
        }

        public SettingsPaths Paths { get; }
        public bool IsInitialized => true;
        public event EventHandler<AppSettingsChangeSet>? SettingsChanged;
        public Task<AppSettings> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings.Clone());
        public AppSettings GetCurrent() => settings.Clone();
        public Task<AppSettingsChangeSet> SaveAsync(
            AppSettings value,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettingsChangeSet(value.Clone(), new HashSet<string>()));
        public Task<AppSettingsChangeSet> UpdateAsync(
            Action<AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            update(settings);
            var changeSet = new AppSettingsChangeSet(settings.Clone(), new HashSet<string>());
            SettingsChanged?.Invoke(this, changeSet);
            return Task.FromResult(changeSet);
        }
        public AppSettings CreateDefault() => settings.Clone();
    }
}
