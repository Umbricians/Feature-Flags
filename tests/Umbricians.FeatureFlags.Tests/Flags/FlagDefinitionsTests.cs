using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>Which definitions the backoffice can edit, and what it reads them as.</summary>
public class FlagDefinitionsTests
{
    [TestCase("""{ "id": "F", "enabled": false }""", false, null)]
    [TestCase("""{ "id": "F", "enabled": true }""", true, null)]
    public async Task Reads_off_and_on_for_everyone(string json, bool enabled, object? audience)
    {
        FlagSettings? settings = FlagDefinitions.ToSettings(await ParseAsync(json), out string? reason);

        Assert.That(settings, Is.EqualTo(new FlagSettings(enabled, null)));
        Assert.That(reason, Is.Null);
    }

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void Reads_back_what_it_writes_including_an_audience_kept_while_off(bool enabled, bool withAudience)
    {
        var written = new FlagSettings(enabled, withAudience ? new FlagAudience(["a", "b"], ["g"]) : null);

        FlagSettings? read = FlagDefinitions.ReadOverride(FlagDefinitions.ToJson("F", written), out string? reason);

        Assert.That(read!.SameAs(written), Is.True, reason);
    }

    [Test]
    public void Says_why_an_override_it_can_not_read_is_read_only()
    {
        Assert.That(FlagDefinitions.ReadOverride("not json", out string? reason), Is.Null);
        Assert.That(reason, Does.Contain("can't be read"));
    }

    [TestCase("""{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } }""", "Microsoft.TimeWindow")]
    [TestCase("""{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Targeting", "parameters": { "Audience": { "DefaultRolloutPercentage": 20 } } } ] } }""", "percentage of everyone")]
    [TestCase("""{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Targeting", "parameters": { "Audience": { "Groups": [ { "Name": "g", "RolloutPercentage": 50 } ] } } } ] } }""", "percentage of a group")]
    [TestCase("""{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Targeting", "parameters": { "Audience": { "Users": ["a"], "Exclusion": { "Users": ["b"] } } } } ] } }""", "excludes")]
    [TestCase("""{ "id": "F", "enabled": true, "variants": [ { "name": "Big" } ], "allocation": { "default_when_enabled": "Big" } }""", "variants")]
    public async Task Leaves_what_it_can_not_edit_read_only(string json, string because)
    {
        FlagSettings? settings = FlagDefinitions.ToSettings(await ParseAsync(json), out string? reason);

        Assert.That(settings, Is.Null);
        Assert.That(reason, Does.Contain(because));
    }

    [Test]
    public void Reads_flags_declared_in_code()
    {
        Assert.That(FlagDefinitions.ToSettings(FlagDefinitions.Simple("F", true), out _), Is.EqualTo(new FlagSettings(true, null)));
        Assert.That(FlagDefinitions.ToSettings(FlagDefinitions.Simple("F", false), out _), Is.EqualTo(FlagSettings.Off));
    }

    private static async Task<FeatureDefinition> ParseAsync(string json) =>
        (await FlagDefinitions.ParseAsync([("F", json)]))["F"];
}
