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

        Assert.That(settings, Is.EqualTo(FlagSettings.ForAudience(enabled, null)));
        Assert.That(reason, Is.Null);
    }

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void Reads_back_what_it_writes_including_an_audience_kept_while_off(bool enabled, bool withAudience)
    {
        var written = FlagSettings.ForAudience(enabled, withAudience ? new FlagAudience(["a", "b"], ["g"]) : null);

        FlagSettings? read = FlagDefinitions.ReadOverride(FlagDefinitions.ToJson("F", written), out string? reason);

        Assert.That(read!.SameAs(written), Is.True, reason);
    }

    [Test]
    public void Says_why_an_override_it_can_not_read_is_read_only()
    {
        Assert.That(FlagDefinitions.ReadOverride("not json", out string? reason), Is.Null);
        Assert.That(reason, Does.Contain("can't be read"));
    }

    [TestCase("""{ "id": "F", "enabled": true, "variants": [ { "name": "Big" } ], "allocation": { "default_when_enabled": "Big" } }""", "variants")]
    public async Task Leaves_what_it_can_not_edit_read_only(string json, string because)
    {
        FlagSettings? settings = FlagDefinitions.ToSettings(await ParseAsync(json), out string? reason);

        Assert.That(reason, Does.Contain(because));
        Assert.That(settings, Is.Null);
    }

    [Test]
    public void Reads_flags_declared_in_code()
    {
        Assert.That(FlagDefinitions.ToSettings(FlagDefinitions.Simple("F", true), out _), Is.EqualTo(FlagSettings.ForAudience(true, null)));
        Assert.That(FlagDefinitions.ToSettings(FlagDefinitions.Simple("F", false), out _), Is.EqualTo(FlagSettings.Off));
    }

    [TestCase(ConditionMatch.Any, "Any")]
    [TestCase(ConditionMatch.All, "All")]
    public void Writes_conditions_in_microsofts_schema_and_reads_them_back(ConditionMatch match, string requirementType)
    {
        var written = new FlagSettings(
            true,
            [
                new FlagCondition("Microsoft.TimeWindow", Json("""{ "Start": "Mon, 01 Jan 2024 00:00:00 GMT" }""")),
                new FlagCondition("Microsoft.Targeting", Json("""{ "Audience": { "Users": ["a"], "DefaultRolloutPercentage": 25 } }""")),
            ],
            match);

        string json = FlagDefinitions.ToJson("F", written);
        FlagSettings? read = FlagDefinitions.ReadOverride(json, out string? reason);

        Assert.That(System.Text.Json.Nodes.JsonNode.Parse(json)!["conditions"]!["requirement_type"]!.GetValue<string>(), Is.EqualTo(requirementType));
        Assert.That(read!.Match, Is.EqualTo(match));
        Assert.That(read.Conditions.Select(c => c.Name), Is.EqualTo(new[] { "Microsoft.TimeWindow", "Microsoft.Targeting" }));
        Assert.That(read.SameAs(written), Is.True);
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void Keeps_a_filter_nobody_here_knows_exactly_as_stored()
    {
        const string stored = """
            { "id": "F", "enabled": false, "conditions": { "requirement_type": "All", "client_filters": [
                { "name": "Acme.Weekdays", "parameters": { "Days": ["Mon", "Tue"], "Limit": 3, "Strict": true, "Nested": { "x": 1.5 } } },
                { "name": "Acme.Bare" } ] } }
            """;

        FlagSettings read = FlagDefinitions.ReadOverride(stored, out _)!;
        FlagSettings again = FlagDefinitions.ReadOverride(FlagDefinitions.ToJson("F", read), out _)!;

        Assert.That(again.Enabled, Is.False);
        Assert.That(again.Match, Is.EqualTo(ConditionMatch.All));
        Assert.That(again.Conditions[0].Parameters.ToJsonString(), Is.EqualTo(read.Conditions[0].Parameters.ToJsonString()));
        Assert.That(again.Conditions[0].Parameters["Limit"]!.GetValue<int>(), Is.EqualTo(3));
        Assert.That(again.Conditions[1], Is.EqualTo(new FlagCondition("Acme.Bare", new System.Text.Json.Nodes.JsonObject())).Using<FlagCondition>((a, b) => a.Name == b.Name && a.Parameters.Count == b.Parameters.Count));
        Assert.That(again.SameAs(read), Is.True);
    }

    [Test]
    public void Keeps_an_audience_while_the_flag_is_off_and_reads_it_by_either_targeting_name()
    {
        FlagSettings? read = FlagDefinitions.ReadOverride(
            """{ "id": "F", "enabled": false, "conditions": { "client_filters": [ { "name": "Targeting", "parameters": { "Audience": { "Users": ["u"], "Groups": [ { "Name": "g", "RolloutPercentage": 100 } ], "DefaultRolloutPercentage": 0 } } } ] } }""",
            out string? reason);

        Assert.That(reason, Is.Null);
        Assert.That(read!.Enabled, Is.False);
        Assert.That(read.Audience, Is.EqualTo(new FlagAudience(["u"], ["g"])).Using<FlagAudience>((a, b) => a.SameAs(b)));
        Assert.That(read.State, Is.EqualTo(FlagState.Off));
    }

    [Test]
    public void An_audience_is_the_same_whatever_its_order_but_other_conditions_compare_as_json()
    {
        FlagSettings one = FlagSettings.ForAudience(true, new FlagAudience(["a", "b"], []));
        FlagSettings two = FlagSettings.ForAudience(true, new FlagAudience(["B", "A"], []));
        FlagSettings window = new(true, [new FlagCondition("Microsoft.TimeWindow", Json("""{ "Start": "x" }"""))]);
        FlagSettings sameWindow = new(true, [new FlagCondition("microsoft.timewindow", Json("""{ "Start": "x" }"""))]);
        FlagSettings otherWindow = new(true, [new FlagCondition("Microsoft.TimeWindow", Json("""{ "Start": "y" }"""))]);

        Assert.That(one.SameAs(two), Is.True);
        Assert.That(one.SameAs(window), Is.False);
        Assert.That(window.SameAs(sameWindow), Is.True);
        Assert.That(window.SameAs(otherWindow), Is.False);
        Assert.That(window.SameAs(window with { Enabled = false }), Is.False);
        Assert.That(window.SameAs(FlagSettings.Off), Is.False);
        Assert.That(window.SameAs(new FlagSettings(true, [.. window.Conditions, .. window.Conditions])), Is.False);
        Assert.That(new FlagSettings(true, [.. window.Conditions, .. window.Conditions]).SameAs(new FlagSettings(true, [.. window.Conditions, .. window.Conditions], ConditionMatch.All)), Is.False);
    }

    [Test]
    public async Task Reads_a_definitions_filters_with_their_parameters_as_the_strings_configuration_holds()
    {
        FlagSettings? settings = FlagDefinitions.ToSettings(
            await ParseAsync("""{ "id": "F", "enabled": true, "conditions": { "requirement_type": "All", "client_filters": [ { "name": "Acme.A", "parameters": { "Limit": 3, "Rate": 1.5, "On": true, "Names": ["x", "y"], "Word": "w" } }, { "name": "Acme.B" } ] } }"""),
            out _);

        Assert.That(settings!.Match, Is.EqualTo(ConditionMatch.All));
        Assert.That(settings.Conditions[0].Parameters.ToJsonString(), Is.EqualTo("""{"Limit":"3","Names":["x","y"],"On":"True","Rate":"1.5","Word":"w"}"""));
        Assert.That(settings.Conditions[1].Parameters, Is.Empty);
    }

    [Test]
    public async Task Keeps_group_names_and_users_that_look_like_numbers_when_read_from_configuration()
    {
        FlagSettings? settings = FlagDefinitions.ToSettings(
            await ParseAsync("""{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Targeting", "parameters": { "Audience": { "Users": ["1001"], "Groups": [ { "Name": "2024", "RolloutPercentage": 100 } ] } } } ] } }"""),
            out _);

        Assert.That(settings!.Audience, Is.EqualTo(new FlagAudience(["1001"], ["2024"])).Using<FlagAudience>((a, b) => a.SameAs(b)));
    }

    [Test]
    public async Task A_default_with_a_value_that_is_not_a_number_can_be_written_as_an_override()
    {
        FlagSettings settings = FlagDefinitions.ToSettings(
            await ParseAsync("""{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Acme.A", "parameters": { "Rate": "NaN", "Infinity": "Infinity" } } ] } }"""),
            out _)!;

        string json = FlagDefinitions.ToJson("F", settings);

        Assert.That(FlagDefinitions.ReadOverride(json, out _)!.SameAs(settings), Is.True);
    }

    [Test]
    public void Stored_JSON_written_by_hand_may_list_members_and_groups_as_numbers()
    {
        FlagSettings? read = FlagDefinitions.ReadOverride(
            """{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Targeting", "parameters": { "Audience": { "Users": [1001], "Groups": [ { "Name": 2024 } ] } } } ] } }""",
            out _);

        Assert.That(read!.Audience, Is.EqualTo(new FlagAudience(["1001"], ["2024"])).Using<FlagAudience>((a, b) => a.SameAs(b)));
    }

    [Test]
    public void Conditions_are_the_same_whether_they_use_a_filters_short_or_long_name()
    {
        var parameters = () => System.Text.Json.Nodes.JsonNode.Parse("""{ "Start": "2024-01-01T00:00:00Z" }""")!.AsObject();
        var limit = () => System.Text.Json.Nodes.JsonNode.Parse("""{ "Audience": { "Users": ["a"], "DefaultRolloutPercentage": 10 } }""")!.AsObject();

        var longNames = new FlagSettings(true, [new FlagCondition("Microsoft.TimeWindow", parameters()), new FlagCondition("Microsoft.Targeting", limit())]);
        var shortNames = new FlagSettings(true, [new FlagCondition("TimeWindow", parameters()), new FlagCondition("Targeting", limit())]);
        var other = new FlagSettings(true, [new FlagCondition("TimeWindow", parameters()), new FlagCondition("Acme.Targeting", limit())]);

        Assert.That(longNames.SameAs(shortNames), Is.True);
        Assert.That(longNames.SameAs(other), Is.False);
    }

    [Test]
    public void JSON_that_is_not_an_object_or_has_variants_has_no_settings()
    {
        Assert.That(FlagDefinitions.ReadOverride("[1]", out string? notObject), Is.Null);
        Assert.That(notObject, Does.Contain("can't be read"));
        Assert.That(FlagDefinitions.ReadOverride("""{ "enabled": "true", "allocation": { } }""", out string? variants), Is.Null);
        Assert.That(variants, Does.Contain("variants"));
        Assert.That(FlagDefinitions.ReadOverride("""{ "enabled": "true" }""", out _)!.Enabled, Is.True);
    }

    private static System.Text.Json.Nodes.JsonObject Json(string json) =>
        (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json)!;

    private static async Task<FeatureDefinition> ParseAsync(string json) =>
        (await FlagDefinitions.ParseAsync([("F", json)]))["F"];
}
