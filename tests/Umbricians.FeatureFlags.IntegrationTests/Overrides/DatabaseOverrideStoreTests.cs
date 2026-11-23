using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests.Overrides;

/// <summary>
/// The store against SQL Server, through the scopes of a running Umbraco: the same queries the SQLite tests run, plus
/// what only a real server and real scopes can show.
/// </summary>
public class DatabaseOverrideStoreTests : IntegrationTest
{
    private static readonly Guid Editor = Guid.NewGuid();

    private IFeatureFlagOverrideStore Store => Site.Services.GetRequiredService<IFeatureFlagOverrideStore>();

    [Test]
    public async Task A_saved_override_is_read_back_with_who_saved_it_and_when_in_utc()
    {
        var when = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);

        await Store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", "{\"enabled\":true}", Editor, when), default);

        FeatureFlagOverride saved = (await Store.GetAllAsync(default)).Single();
        Assert.That(saved, Has.Property(nameof(FeatureFlagOverride.FlagName)).EqualTo("Checkout.NewFlow")
            .And.Property(nameof(FeatureFlagOverride.Definition)).EqualTo("{\"enabled\":true}")
            .And.Property(nameof(FeatureFlagOverride.UpdatedBy)).EqualTo(Editor)
            .And.Property(nameof(FeatureFlagOverride.UpdatedUtc)).EqualTo(when));
        Assert.That(saved.UpdatedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public async Task Saving_a_flag_again_replaces_its_override_whatever_the_case_of_its_name()
    {
        await Store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", "{\"enabled\":false}", null, DateTime.UtcNow), default);

        await Store.SaveAsync(new FeatureFlagOverride("checkout.newflow", "{\"enabled\":true}", Editor, DateTime.UtcNow), default);

        FeatureFlagOverride saved = (await Store.GetAllAsync(default)).Single();
        Assert.That(saved, Has.Property(nameof(FeatureFlagOverride.FlagName)).EqualTo("checkout.newflow")
            .And.Property(nameof(FeatureFlagOverride.Definition)).EqualTo("{\"enabled\":true}")
            .And.Property(nameof(FeatureFlagOverride.UpdatedBy)).EqualTo(Editor));
    }

    [Test]
    public async Task Deleting_a_flags_override_leaves_the_others_and_deleting_one_that_is_not_there_does_nothing()
    {
        await Store.SaveAsync(new FeatureFlagOverride("A", "{}", null, DateTime.UtcNow), default);
        await Store.SaveAsync(new FeatureFlagOverride("B", "{}", null, DateTime.UtcNow), default);

        await Store.DeleteAsync("a", default);
        await Store.DeleteAsync("Missing", default);

        Assert.That((await Store.GetAllAsync(default)).Select(saved => saved.FlagName), Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public async Task A_definition_far_longer_than_an_ordinary_column_holds_is_kept_whole()
    {
        string definition = "{\"description\":\"" + new string('x', 200_000) + "\"}";

        await Store.SaveAsync(new FeatureFlagOverride("Long", definition, null, DateTime.UtcNow), default);

        Assert.That((await Store.GetAllAsync(default)).Single().Definition, Is.EqualTo(definition));
    }

    [Test]
    public async Task Text_in_other_alphabets_and_emoji_survives_the_round_trip()
    {
        const string name = "Café.Größe.日本語";
        const string definition = "{\"description\":\"Ünïcödé, Ελληνικά, кириллица, 日本語, 🚀🏳️‍🌈\"}";

        await Store.SaveAsync(new FeatureFlagOverride(name, definition, null, DateTime.UtcNow), default);

        FeatureFlagOverride saved = (await Store.GetAllAsync(default)).Single();
        Assert.That(saved.FlagName, Is.EqualTo(name));
        Assert.That(saved.Definition, Is.EqualTo(definition));
    }

    [Test]
    public async Task A_save_that_is_not_completed_leaves_nothing_behind()
    {
        // Saving completes its scope itself, so a failed save, here a name too long for the column, must not be kept.
        string tooLong = new('n', 300);

        Assert.That(async () => await Store.SaveAsync(new FeatureFlagOverride(tooLong, "{}", null, DateTime.UtcNow), default), Throws.Exception);

        Assert.That(await Store.GetAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task Many_saves_to_the_same_flag_at_once_all_succeed_and_leave_one_row()
    {
        string[] definitions = Enumerable.Range(0, 16).Select(number => $"{{\"enabled\":true,\"number\":{number}}}").ToArray();

        await Task.WhenAll(definitions.Select(definition =>
            Task.Run(() => Store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", definition, Editor, DateTime.UtcNow), default))));

        FeatureFlagOverride saved = (await Store.GetAllAsync(default)).Single();
        Assert.That(definitions, Does.Contain(saved.Definition));
        Assert.That(await CountRowsAsync("Checkout.NewFlow"), Is.EqualTo(1));
    }

    [Test]
    public async Task The_last_save_to_a_flag_wins_after_a_burst_of_saves_at_once()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(number =>
            Task.Run(() => Store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", $"{{\"number\":{number}}}", null, DateTime.UtcNow), default))));

        await Store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", "{\"number\":99}", Editor, DateTime.UtcNow), default);

        FeatureFlagOverride saved = (await Store.GetAllAsync(default)).Single();
        Assert.That(saved.Definition, Is.EqualTo("{\"number\":99}"));
        Assert.That(saved.UpdatedBy, Is.EqualTo(Editor));
    }

    [Test]
    public async Task Saves_to_different_flags_at_once_all_succeed_and_each_has_one_row()
    {
        string[] names = Enumerable.Range(0, 24).Select(number => $"Flag.Number{number}").ToArray();

        await Task.WhenAll(names.Select(name =>
            Task.Run(() => Store.SaveAsync(new FeatureFlagOverride(name, $"{{\"id\":\"{name}\"}}", null, DateTime.UtcNow), default))));

        IReadOnlyCollection<FeatureFlagOverride> saved = await Store.GetAllAsync(default);
        Assert.That(saved.Select(row => row.FlagName), Is.EquivalentTo(names));
        Assert.That(saved.All(row => row.Definition == $"{{\"id\":\"{row.FlagName}\"}}"));
    }

    [Test]
    public async Task Saving_and_deleting_the_same_flag_at_once_never_fails_or_leaves_two_rows()
    {
        await Task.WhenAll(Enumerable.Range(0, 12).Select(number => Task.Run(() => number % 2 == 0
            ? Store.SaveAsync(new FeatureFlagOverride("Checkout.NewFlow", "{}", null, DateTime.UtcNow), default)
            : Store.DeleteAsync("Checkout.NewFlow", default))));

        Assert.That(await CountRowsAsync("Checkout.NewFlow"), Is.LessThanOrEqualTo(1));
    }

    private async Task<int> CountRowsAsync(string flagName)
    {
        await using SqlConnection connection = await Site.OpenConnectionAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {FeatureFlagOverrideDto.TableName} WHERE flagName = @name";
        command.Parameters.AddWithValue("@name", flagName);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
