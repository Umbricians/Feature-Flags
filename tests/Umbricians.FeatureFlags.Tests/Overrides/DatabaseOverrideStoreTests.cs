using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Overrides;

/// <summary>
/// The store against a real SQLite database, in the table the package's migration creates.
/// </summary>
public class DatabaseOverrideStoreTests
{
    private static readonly Guid Editor = Guid.NewGuid();

    private TestDatabase _database = null!;
    private DatabaseOverrideStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _database = new TestDatabase();
        await _database.CreateOverridesTableAsync();
        _store = new DatabaseOverrideStore(_database.Scopes);
    }

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task A_site_with_no_saved_overrides_has_none()
    {
        Assert.That(await _store.GetAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task A_saved_override_is_read_back_with_who_saved_it_and_when_in_utc()
    {
        var when = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);

        await _store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", "{\"enabled\":true}", Editor, when), default);

        FeatureFlagOverride saved = (await _store.GetAllAsync(default)).Single();
        Assert.That(saved, Has.Property(nameof(FeatureFlagOverride.FlagName)).EqualTo("Checkout.NewFlow")
            .And.Property(nameof(FeatureFlagOverride.Definition)).EqualTo("{\"enabled\":true}")
            .And.Property(nameof(FeatureFlagOverride.UpdatedBy)).EqualTo(Editor)
            .And.Property(nameof(FeatureFlagOverride.UpdatedUtc)).EqualTo(when));
        Assert.That(saved.UpdatedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(_database.Completions, Is.EqualTo(1));
    }

    [Test]
    public async Task Saving_a_flag_again_replaces_its_override_whatever_the_case_of_its_name()
    {
        await _store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", "{\"enabled\":false}", null, DateTime.UtcNow), default);

        await _store.SaveAsync(new FeatureFlagOverride("checkout.newflow", "{\"enabled\":true}", Editor, DateTime.UtcNow), default);

        FeatureFlagOverride saved = (await _store.GetAllAsync(default)).Single();
        Assert.That(saved, Has.Property(nameof(FeatureFlagOverride.FlagName)).EqualTo("checkout.newflow")
            .And.Property(nameof(FeatureFlagOverride.Definition)).EqualTo("{\"enabled\":true}")
            .And.Property(nameof(FeatureFlagOverride.UpdatedBy)).EqualTo(Editor));
    }

    [Test]
    public async Task Deleting_a_flags_override_leaves_the_others_and_deleting_one_that_is_not_there_does_nothing()
    {
        await _store.SaveAsync(new FeatureFlagOverride("A", "{}", null, DateTime.UtcNow), default);
        await _store.SaveAsync(new FeatureFlagOverride("B", "{}", null, DateTime.UtcNow), default);

        await _store.DeleteAsync("a", default);
        await _store.DeleteAsync("Missing", default);

        Assert.That((await _store.GetAllAsync(default)).Select(saved => saved.FlagName), Is.EqualTo(new[] { "B" }));
    }
}
