using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Umbricians.FeatureFlags.Controllers;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>Percentages and exclusions in an audience: how they are read, written, checked and evaluated by Microsoft.</summary>
public class AudiencePercentageTests
{
    private const string Member = "8c2f4a10-3b6e-4d1a-9f5c-2e7b1d0a6c34";
    private const string Group = "7f3a9c21-4e8b-4d6f-a1c5-0b2e9d4f8a67";
    private const string Declared = """{ "feature_management": { "feature_flags": [ { "id": "F", "enabled": false } ] } }""";

    private static readonly Guid Editor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    private static FlagAudience Full() => new(["m1", "m2"], ["g1", "g2"])
    {
        DefaultPercentage = 25,
        GroupPercentages = new Dictionary<string, int> { ["g2"] = 40 },
        ExcludedMembers = ["x1"],
        ExcludedGroups = ["xg"],
    };

    [Test]
    public void An_audience_with_percentages_and_exclusions_round_trips_in_Microsofts_shape()
    {
        JsonObject parameters = Full().ToParameters();

        FlagAudience? read = FlagAudience.TryRead(parameters, out string? reason);

        Assert.That(reason, Is.Null);
        Assert.That(read!.SameAs(Full()), Is.True);
        Assert.That(parameters.ToJsonString(), Is.EqualTo(
            """{"Audience":{"Users":["m1","m2"],"Groups":[{"Name":"g1","RolloutPercentage":100},{"Name":"g2","RolloutPercentage":40}],"DefaultRolloutPercentage":25,"Exclusion":{"Users":["x1"],"Groups":["xg"]}}}"""));
    }

    [Test]
    public void Percentages_written_as_text_and_keys_in_any_case_are_read()
    {
        FlagAudience? read = FlagAudience.TryRead(
            Json("""{ "audience": { "users": ["m"], "groups": [ { "name": "g", "rolloutpercentage": "30" } ], "defaultRolloutPercentage": "50", "exclusion": { "users": ["x"], "groups": ["xg"] } } }"""),
            out string? reason);

        Assert.That(reason, Is.Null);
        Assert.That(read!.DefaultPercentage, Is.EqualTo(50));
        Assert.That(read.PercentageOf("G"), Is.EqualTo(30));
        Assert.That((read.ExcludedMembers, read.ExcludedGroups), Is.EqualTo((new[] { "x" }, new[] { "xg" })));
    }

    [Test]
    public void A_group_without_a_percentage_includes_nobody_as_Microsoft_reads_it()
    {
        FlagAudience? read = FlagAudience.TryRead(Json("""{ "Audience": { "Groups": [ { "Name": "g" } ] } }"""), out _);

        Assert.That(read!.PercentageOf("g"), Is.EqualTo(0));
    }

    [TestCase("""{ "Audience": { "DefaultRolloutPercentage": 101 } }""", "everyone else")]
    [TestCase("""{ "Audience": { "DefaultRolloutPercentage": -1 } }""", "everyone else")]
    [TestCase("""{ "Audience": { "DefaultRolloutPercentage": 12.5 } }""", "whole number")]
    [TestCase("""{ "Audience": { "DefaultRolloutPercentage": "lots" } }""", "isn't a number")]
    [TestCase("""{ "Audience": { "DefaultRolloutPercentage": [1] } }""", "isn't a number")]
    [TestCase("""{ "Audience": { "Groups": [ { "Name": "g", "RolloutPercentage": 150 } ] } }""", "group 'g'")]
    public void A_percentage_that_is_not_a_whole_number_from_0_to_100_is_a_problem(string json, string mentions)
    {
        FlagAudience? read = FlagAudience.TryRead(Json(json), out string? reason);

        Assert.That(read, Is.Null);
        Assert.That(reason, Does.Contain(mentions));
        Assert.That(new AudienceCondition().Validate(Json(json)), Is.EqualTo(new[] { reason }));
    }

    [Test]
    public void Audiences_that_differ_only_in_a_percentage_or_exclusion_are_not_the_same()
    {
        Assert.That(Full().SameAs(Full() with { DefaultPercentage = 26 }), Is.False);
        Assert.That(Full().SameAs(Full() with { GroupPercentages = new Dictionary<string, int>() }), Is.False);
        Assert.That(Full().SameAs(Full() with { ExcludedMembers = [] }), Is.False);
        Assert.That(Full().SameAs(Full() with { ExcludedGroups = ["other"] }), Is.False);
        Assert.That(Full().SameAs(Full() with { ExcludedMembers = ["X1"], GroupPercentages = new Dictionary<string, int> { ["G2"] = 40 } }), Is.True);
    }

    [Test]
    public void Described_with_percentages_and_exclusions()
    {
        var condition = new AudienceCondition();

        Assert.That(condition.Describe(Full().ToParameters()), Is.EqualTo("2 members and 2 groups (1 in part) and 25% of everyone else, except 1 member and 1 group"));
        Assert.That(condition.Describe(new FlagAudience([], []) { ExcludedMembers = ["x"] }.ToParameters()), Is.EqualTo("Nobody"));
        Assert.That(condition.Describe(Json("""{ "Audience": { "DefaultRolloutPercentage": 300 } }""")), Is.EqualTo("An audience that can't be read"));
    }

    [Test]
    public async Task A_targeting_only_flag_with_percentages_and_exclusions_is_an_audience_and_saves()
    {
        var site = new TestSite(Declared);
        var service = site.Provider.GetRequiredService<FeatureFlagService>();

        FeatureFlagResult result = await service.SaveAsync("F", FlagSettings.ForAudience(true, Full() with { Members = [Member.ToUpperInvariant()], ExcludedMembers = [Member.ToUpperInvariant()] }), Editor, default);
        FeatureFlag flag = (await site.Provider.GetRequiredService<FlagCatalog>().FindAsync("F", default))!;

        Assert.That(result.Status, Is.EqualTo(FeatureFlagResultStatus.Saved));
        Assert.That(flag.State, Is.EqualTo(FeatureFlagState.Audience));
        Assert.That(flag.Settings!.Audience!.ExcludedMembers, Is.EqualTo(new[] { Member }));
        Assert.That(flag.Settings.Audience.PercentageOf("g2"), Is.EqualTo(40));
    }

    [Test]
    public async Task Saving_an_audience_writes_group_percentages_and_exclusion_groups_against_normalised_ids()
    {
        var site = new TestSite(Declared);
        site.MemberGroups.Add(Guid.Parse(Group), "Premium members");
        var service = site.Provider.GetRequiredService<FeatureFlagService>();

        await service.SaveAsync("F", FlagSettings.ForAudience(true, new FlagAudience([], [Group.ToUpperInvariant()])
        {
            GroupPercentages = new Dictionary<string, int> { [Group.ToUpperInvariant()] = 10 },
            ExcludedGroups = [Group.ToUpperInvariant(), Group],
        }), Editor, default);

        FlagAudience saved = FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience!;
        Assert.That(saved.Groups, Is.EqualTo(new[] { Group }));
        Assert.That(saved.PercentageOf(Group), Is.EqualTo(10));
        Assert.That(saved.ExcludedGroups, Is.EqualTo(new[] { Group }));
    }

    [Test]
    public async Task Stored_JSON_with_percentages_written_as_text_reads_and_evaluates()
    {
        var site = new TestSite(Declared);
        site.Store.WithJson("F", """{ "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Targeting", "parameters": { "Audience": { "Users": [], "Groups": [], "DefaultRolloutPercentage": "100" } } } ] } }""");

        site.Visitor.SignIn("anyone");

        Assert.That(await site.IsOnAsync("F"), Is.True);
        Assert.That(FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience!.DefaultPercentage, Is.EqualTo(100));
    }

    [Test]
    public async Task A_default_percentage_of_100_includes_any_member_and_0_includes_none_but_visitors_without_an_id_all_get_one_answer()
    {
        var site = new TestSite(Declared);
        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience([], []) { DefaultPercentage = 100 }));
        site.Visitor.SignIn("any-member-id");
        bool everyone = await site.IsOnAsync("F");
        site.Visitor.SignOut();
        bool anonymous = await site.IsOnAsync("F");

        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience([], []) { DefaultPercentage = 0 }));
        site.Refresh();
        site.Visitor.SignIn("any-member-id");
        bool none = await site.IsOnAsync("F");

        Assert.That((everyone, anonymous, none), Is.EqualTo((true, true, false)));
    }

    [Test]
    public async Task A_percentage_of_everyone_is_sticky_for_each_member_and_about_that_share()
    {
        var site = new TestSite(Declared);
        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience([], []) { DefaultPercentage = 50 }));
        var first = new List<bool>();
        var second = new List<bool>();

        for (int i = 0; i < 400; i++)
        {
            site.Visitor.SignIn($"member-{i}");
            first.Add(await site.IsOnAsync("F"));
            second.Add(await site.IsOnAsync("F"));
        }

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first.Count(on => on), Is.InRange(140, 260));
    }

    [Test]
    public async Task A_group_at_0_percent_includes_nobody_and_at_100_includes_everyone_in_it()
    {
        var site = new TestSite(Declared);
        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience([], ["g"]) { GroupPercentages = new Dictionary<string, int> { ["g"] = 0 } }));
        site.Visitor.SignIn("m", "g");
        bool none = await site.IsOnAsync("F");

        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience([], ["g"])));
        site.Refresh();
        bool all = await site.IsOnAsync("F");

        Assert.That((none, all), Is.EqualTo((false, true)));
    }

    [Test]
    public async Task An_excluded_member_is_off_even_in_a_group_and_an_excluded_group_is_off_for_its_members()
    {
        var site = new TestSite(Declared);
        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience(["listed"], ["g"]) { DefaultPercentage = 100, ExcludedMembers = ["out"], ExcludedGroups = ["banned"] }));

        site.Visitor.SignIn("out", "g");
        bool excludedMember = await site.IsOnAsync("F");
        site.Visitor.SignIn("someone", "g", "banned");
        bool excludedGroup = await site.IsOnAsync("F");
        site.Visitor.SignIn("listed", "banned");
        bool listedButBanned = await site.IsOnAsync("F");
        site.Visitor.SignIn("someone", "g");
        bool normal = await site.IsOnAsync("F");

        Assert.That((excludedMember, excludedGroup, listedButBanned, normal), Is.EqualTo((false, false, false, true)));
    }

    [Test]
    public async Task Clean_up_takes_a_deleted_member_and_group_out_of_exclusions_and_drops_the_percentage_of_a_deleted_group()
    {
        var site = new TestSite(Declared);
        var service = site.Provider.GetRequiredService<FeatureFlagService>();
        await service.SaveAsync("F", FlagSettings.ForAudience(true, new FlagAudience(["keep"], ["gone", "stay"])
        {
            DefaultPercentage = 10,
            GroupPercentages = new Dictionary<string, int> { ["gone"] = 5, ["stay"] = 60 },
            ExcludedMembers = ["gone", "kept-out"],
            ExcludedGroups = ["gone", "stays-out"],
        }), Editor, default);

        await service.RemoveFromAudiencesAsync(["gone"], default);

        FlagAudience saved = FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience!;
        Assert.That(saved.Groups, Is.EqualTo(new[] { "stay" }));
        Assert.That(saved.GroupPercentages, Is.EqualTo(new Dictionary<string, int> { ["stay"] = 60 }));
        Assert.That(saved.ExcludedMembers, Is.EqualTo(new[] { "kept-out" }));
        Assert.That(saved.ExcludedGroups, Is.EqualTo(new[] { "stays-out" }));
        Assert.That(saved.DefaultPercentage, Is.EqualTo(10));
    }

    [Test]
    public async Task Clean_up_changes_an_audience_whose_only_deleted_id_was_excluded()
    {
        var site = new TestSite(Declared);
        var service = site.Provider.GetRequiredService<FeatureFlagService>();
        await service.SaveAsync("F", FlagSettings.ForAudience(true, new FlagAudience(["keep"], []) { ExcludedMembers = ["gone"] }), Editor, default);

        await service.RemoveFromAudiencesAsync(["gone"], default);

        Assert.That(FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience!.ExcludedMembers, Is.Empty);
    }
}
