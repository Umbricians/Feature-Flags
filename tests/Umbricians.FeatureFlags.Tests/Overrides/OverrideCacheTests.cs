using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Overrides;

/// <summary>What the cache knows about each override once it has read it.</summary>
public class OverrideCacheTests
{
    private static async Task<SavedOverride> ReadAsync(TestSite site, string flagName)
    {
        OverrideCache.Snapshot snapshot = await site.Provider.GetRequiredService<OverrideCache>().GetAsync();
        return snapshot.Overrides[flagName];
    }

    [Test]
    public async Task Reads_an_override_switched_off_into_a_disabled_definition_and_settings_that_keep_its_audience()
    {
        var site = new TestSite();
        site.Store.With("F", FlagSettings.ForAudience(false, new FlagAudience(["m"], ["g"])));

        SavedOverride saved = await ReadAsync(site, "f");

        Assert.That(saved.Definition!.Status, Is.EqualTo(FeatureStatus.Disabled));
        Assert.That(saved.Settings!.Audience!.Members, Is.EqualTo(new[] { "m" }));
        Assert.That(saved.Settings.Audience.Groups, Is.EqualTo(new[] { "g" }));
        Assert.That(saved.NotEditableReason, Is.Null);
    }

    [Test]
    public async Task An_override_whose_json_can_not_be_read_has_no_definition_or_settings_and_says_why()
    {
        var site = new TestSite();
        site.Store.WithJson("F", "not json");

        SavedOverride saved = await ReadAsync(site, "F");

        Assert.That(saved.Definition, Is.Null);
        Assert.That(saved.Settings, Is.Null);
        Assert.That(saved.NotEditableReason, Does.Contain("can't be read"));
    }

    [Test]
    public async Task An_override_the_backoffice_can_not_edit_has_a_definition_and_conditions_but_a_reason()
    {
        var site = new TestSite();
        site.Store.WithJson("F", """{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } }""");

        SavedOverride saved = await ReadAsync(site, "F");

        Assert.That(saved.Definition, Is.Not.Null);
        Assert.That(saved.Settings!.Conditions.Single().Name, Is.EqualTo("Microsoft.TimeWindow"));
        Assert.That(saved.NotEditableReason, Does.Contain("Microsoft.TimeWindow"));
    }

    [Test]
    public async Task Tells_the_caller_about_each_override_it_can_not_read()
    {
        var invalid = new List<string>();

        IReadOnlyDictionary<string, SavedOverride> saved = await SavedOverride.ReadAllAsync(
            new InMemoryOverrideStore().With("Good", FlagSettings.ForAudience(true, null)).WithJson("Bad", "{").All,
            (flagName, _) => invalid.Add(flagName));

        Assert.That(invalid, Is.EqualTo(new[] { "Bad" }));
        Assert.That(saved["good"].Definition, Is.Not.Null);
    }

    [Test]
    public async Task Uses_no_overrides_and_plans_a_retry_when_the_store_can_not_be_read()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureFlagOverrideStore>(new ThrowingStore()));

        OverrideCache.Snapshot snapshot = await site.Provider.GetRequiredService<OverrideCache>().GetAsync();

        Assert.That(snapshot.Overrides, Is.Empty);
        Assert.That(snapshot.RetryAt, Is.Not.Null);
    }

    [Test]
    public async Task Reads_the_store_again_in_the_background_once_the_retry_time_has_passed()
    {
        var time = new FakeTimeProvider();
        var store = new ThrowingStore();
        var site = new TestSite(before: services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IFeatureFlagOverrideStore>(store);
        });
        OverrideCache cache = site.Provider.GetRequiredService<OverrideCache>();
        await cache.GetAsync();
        store.Overrides = new InMemoryOverrideStore().With("F", FlagSettings.ForAudience(true, null)).All;

        time.Advance(TimeSpan.FromMinutes(1));
        await cache.GetAsync();
        OverrideCache.Snapshot snapshot = await cache.GetAsync();
        for (int i = 0; i < 100 && snapshot.RetryAt is not null; i++)
        {
            await Task.Delay(20);
            snapshot = await cache.GetAsync();
        }

        Assert.That(snapshot.RetryAt, Is.Null);
        Assert.That(snapshot.Overrides.ContainsKey("F"), Is.True);
    }

    [Test]
    public async Task Keeps_the_last_good_read_when_a_reload_can_not_read_the_store()
    {
        var store = new ThrowingStore { Overrides = new InMemoryOverrideStore().With("F", FlagSettings.ForAudience(true, null)).All };
        var site = new TestSite(before: services => services.AddSingleton<IFeatureFlagOverrideStore>(store));
        OverrideCache cache = site.Provider.GetRequiredService<OverrideCache>();
        await cache.GetAsync();

        store.Overrides = null;
        await cache.ReloadAsync();
        OverrideCache.Snapshot snapshot = await cache.GetAsync();

        Assert.That(snapshot.Overrides.ContainsKey("F"), Is.True);
        Assert.That(snapshot.RetryAt, Is.Not.Null);
    }

    private sealed class ThrowingStore : IFeatureFlagOverrideStore
    {
        public IReadOnlyCollection<FeatureFlagOverride>? Overrides { get; set; }

        public Task<IReadOnlyCollection<FeatureFlagOverride>> GetAllAsync(CancellationToken cancellationToken) =>
            Overrides is { } overrides ? Task.FromResult(overrides) : throw new InvalidOperationException("no table");

        public Task SaveAsync(FeatureFlagOverride featureFlagOverride, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(string flagName, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
