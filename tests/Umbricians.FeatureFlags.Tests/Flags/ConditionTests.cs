using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Controllers;
using Umbricians.FeatureFlags.Extensions;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>Conditions: which flags the backoffice knows, what a developer's own condition adds, and how names match.</summary>
public class ConditionTests
{
    private const string Flags = """
        { "feature_management": { "feature_flags": [
            { "id": "Off", "enabled": false },
            { "id": "Everyone", "enabled": true },
            { "id": "Aud", "enabled": true },
            { "id": "Cultured", "enabled": true },
            { "id": "Windowed", "enabled": true },
            { "id": "Variants", "enabled": true, "variants": [ { "name": "Big" } ], "allocation": { "default_when_enabled": "Big" } } ] } }
        """;

    /// <summary>A developer's condition: the culture the request is in.</summary>
    private sealed class CultureCondition : IFeatureFlagCondition
    {
        public string Name => "Contoso.Culture";

        public string DisplayName => "Culture";

        public IEnumerable<string> Validate(JsonObject parameters) =>
            parameters["Cultures"] is JsonArray { Count: > 0 } ? [] : ["Choose at least one culture."];

        public string Describe(JsonObject parameters) =>
            string.Join(" and ", (parameters["Cultures"] as JsonArray ?? []).Select(culture => culture!.GetValue<string>()));
    }

    private static JsonObject Cultures(params string[] cultures) => new() { ["Cultures"] = new JsonArray(cultures.Select(c => (JsonNode?)c).ToArray()) };

    private static FlagCondition Culture(params string[] cultures) => new("Contoso.Culture", Cultures(cultures));

    private static TestSite Site() => new(Flags, before: services => services.AddSingleton<IFeatureFlagCondition, CultureCondition>());

    private static FeatureFlagService Service(TestSite site) => site.Provider.GetRequiredService<FeatureFlagService>();

    private static async Task<FeatureFlag> FlagAsync(TestSite site, string name) =>
        (await site.Provider.GetRequiredService<FlagCatalog>().FindAsync(name, default))!;

    [Test]
    public async Task Each_state_is_decided_by_what_the_flag_uses_and_which_conditions_are_registered()
    {
        TestSite site = Site();
        site.Store
            .With("Aud", FlagSettings.ForAudience(true, new FlagAudience(["m"], [])))
            .With("Cultured", new FlagSettings(true, [Culture("fr")]))
            .With("Windowed", new FlagSettings(true, [Culture("fr"), new FlagCondition("Microsoft.Percentage", new JsonObject())]));

        Assert.That((await FlagAsync(site, "Off")).State, Is.EqualTo(FeatureFlagState.Off));
        Assert.That((await FlagAsync(site, "Everyone")).State, Is.EqualTo(FeatureFlagState.Everyone));
        Assert.That((await FlagAsync(site, "Aud")).State, Is.EqualTo(FeatureFlagState.Audience));
        FeatureFlag cultured = await FlagAsync(site, "Cultured");
        Assert.That(cultured.State, Is.EqualTo(FeatureFlagState.Conditions));
        Assert.That(cultured.Actions.Switch.Allowed && cultured.Actions.Save.Allowed, Is.True);
        FeatureFlag windowed = await FlagAsync(site, "Windowed");
        Assert.That(windowed.State, Is.EqualTo(FeatureFlagState.Custom));
        Assert.That(windowed.Actions.Switch, Has.Property(nameof(FeatureFlagAction.Allowed)).False
            .And.Property(nameof(FeatureFlagAction.Reason)).Contain("Microsoft.Percentage"));
        Assert.That((await FlagAsync(site, "Variants")).State, Is.EqualTo(FeatureFlagState.Custom));
    }

    [Test]
    public async Task A_filter_nobody_registered_is_custom_and_can_not_be_switched()
    {
        var site = new TestSite(Flags);
        site.Store.With("Cultured", new FlagSettings(true, [Culture("fr")]));

        FeatureFlag flag = await FlagAsync(site, "Cultured");
        FeatureFlagResult result = await Service(site).SwitchAsync("Cultured", false);

        Assert.That(flag.State, Is.EqualTo(FeatureFlagState.Custom));
        Assert.That(result.Status, Is.EqualTo(FeatureFlagResultStatus.NotAllowed));
        Assert.That(result.Reason, Does.Contain("Contoso.Culture"));
        Assert.That(site.Store.All.Single().Definition, Does.Contain("true"));
    }

    [Test]
    public async Task A_registered_condition_flag_can_be_switched_and_keeps_its_conditions()
    {
        TestSite site = Site();
        site.Store.With("Cultured", new FlagSettings(true, [Culture("fr")]));

        FeatureFlagResult result = await Service(site).SwitchAsync("Cultured", false);

        Assert.That(result.IsSaved, Is.True);
        FeatureFlag flag = await FlagAsync(site, "Cultured");
        Assert.That(flag.State, Is.EqualTo(FeatureFlagState.Off));
        Assert.That(flag.Settings!.Conditions.Single().Parameters["Cultures"]![0]!.GetValue<string>(), Is.EqualTo("fr"));
    }

    [Test]
    public async Task A_developers_condition_describes_itself_for_the_list()
    {
        TestSite site = Site();
        site.Store.With("Cultured", new FlagSettings(true, [Culture("fr", "de"), new FlagCondition("Targeting", new FlagAudience(["m"], []).ToParameters())], ConditionMatch.All));

        FeatureFlag flag = await FlagAsync(site, "Cultured");

        Assert.That(flag.Summary, Is.EqualTo("fr and de; 1 member"));
        Assert.That((await FlagAsync(site, "Everyone")).Summary, Is.Null);
    }

    [Test]
    public async Task A_developers_condition_validates_every_save_from_the_service()
    {
        TestSite site = Site();

        FeatureFlagResult bad = await Service(site).SaveAsync("Everyone", new FlagSettings(true, [Culture()]));
        FeatureFlagResult good = await Service(site).SaveAsync("Everyone", new FlagSettings(true, [Culture("fr")]));

        Assert.That(bad.Status, Is.EqualTo(FeatureFlagResultStatus.NotAllowed));
        Assert.That(bad.Problems, Is.EqualTo(new[] { "Culture: Choose at least one culture." }));
        Assert.That(bad.Reason, Is.EqualTo("Culture: Choose at least one culture."));
        Assert.That(good.IsSaved, Is.True);
        Assert.That(site.Store.All, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Saving_a_condition_nobody_registered_is_not_allowed_and_writes_nothing()
    {
        TestSite site = Site();

        FeatureFlagResult result = await Service(site).SaveAsync("Everyone", new FlagSettings(true, [new FlagCondition("Acme.X", new JsonObject())]));

        Assert.That(result.Status, Is.EqualTo(FeatureFlagResultStatus.NotAllowed));
        Assert.That(result.Problems.Single(), Does.Contain("Acme.X"));
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task The_audience_can_not_be_saved_with_a_percentage_over_100()
    {
        TestSite site = Site();
        var percentage = new FlagCondition("Microsoft.Targeting", new JsonObject { ["Audience"] = new JsonObject { ["DefaultRolloutPercentage"] = 120 } });

        FeatureFlagResult result = await Service(site).SaveAsync("Everyone", new FlagSettings(true, [percentage]));

        Assert.That(result.Problems.Single(), Does.StartWith("Audience:").And.Contain("percentage"));
    }

    [Test]
    public async Task A_condition_that_throws_is_a_problem_when_validating_and_left_out_of_the_summary()
    {
        var site = new TestSite(Flags, before: services => services.AddSingleton<IFeatureFlagCondition, ThrowingCondition>());
        site.Store.With("Cultured", new FlagSettings(true, [new FlagCondition("Contoso.Throws", new JsonObject())]));

        FeatureFlagResult result = await Service(site).SaveAsync("Everyone", new FlagSettings(true, [new FlagCondition("Contoso.Throws", new JsonObject())]));

        Assert.That(result.Problems.Single(), Does.Contain("couldn't be checked"));
        Assert.That((await FlagAsync(site, "Cultured")).Summary, Is.Null);
    }

    private sealed class ThrowingCondition : IFeatureFlagCondition
    {
        public string Name => "Contoso.Throws";

        public string DisplayName => "Throws";

        public IEnumerable<string> Validate(JsonObject parameters) => throw new InvalidOperationException("boom");

        public string Describe(JsonObject parameters) => throw new InvalidOperationException("boom");
    }

    [Test]
    public async Task The_controller_turns_problems_into_a_conflict_with_a_problems_list()
    {
        TestSite site = Site();
        var manager = new ManageFlagsController(
            site.Provider.GetRequiredService<FeatureFlagService>(),
            new FlagResponses(Stub.Of<Umbraco.Cms.Core.Services.IUserService>((_, _) => null), site.Provider.GetRequiredService<AudienceGroups>()),
            Stub.Of<Umbraco.Cms.Core.Security.IBackOfficeSecurityAccessor>((_, _) => null));
        FeatureFlagResult result = await Service(site).SaveAsync("Everyone", new FlagSettings(true, [Culture()]));

        var conflict = (Microsoft.AspNetCore.Mvc.ConflictObjectResult)await manager.RespondAsync("Everyone", result, default);

        var problem = (Microsoft.AspNetCore.Mvc.ProblemDetails)conflict.Value!;
        Assert.That(problem.Extensions["problems"], Is.EqualTo(new[] { "Culture: Choose at least one culture." }));
        Assert.That(problem.Detail, Does.Contain("Choose at least one culture"));
        // The backoffice's HTTP client only passes a problem's detail on when it has a type.
        Assert.That(problem.Type, Is.EqualTo("Error"));
    }

    private static ManageFlagsController Manager(TestSite site) => new(
        site.Provider.GetRequiredService<FeatureFlagService>(),
        new FlagResponses(Stub.Of<Umbraco.Cms.Core.Services.IUserService>((_, _) => null), site.Provider.GetRequiredService<AudienceGroups>()),
        Stub.Of<Umbraco.Cms.Core.Security.IBackOfficeSecurityAccessor>((_, _) => null));

    [Test]
    public async Task The_api_saves_conditions_and_how_to_match_them_and_returns_the_flag()
    {
        TestSite site = Site();
        var request = new SaveFlagRequestModel
        {
            Enabled = true,
            Match = "all",
            Conditions =
            [
                new FlagConditionModel { Name = "Microsoft.Targeting", Parameters = FlagSettings.ForAudience(true, new FlagAudience(["m1"], [])).Conditions[0].Parameters },
                new FlagConditionModel { Name = "Contoso.Culture", Parameters = Cultures("fr") },
            ],
        };

        var ok = (Microsoft.AspNetCore.Mvc.OkObjectResult)await Manager(site).Save("Cultured", request, default);

        var flag = (FlagResponseModel)ok.Value!;
        Assert.That(flag.State, Is.EqualTo("conditions"));
        Assert.That(flag.Settings, Has.Property(nameof(FlagSettingsModel.Match)).EqualTo("all"));
        Assert.That(flag.Settings!.Conditions.Select(condition => condition.Name), Is.EqualTo(new[] { "Microsoft.Targeting", "Contoso.Culture" }));
        Assert.That(flag.Summary, Is.EqualTo("1 member; fr"));
    }

    [Test]
    public async Task A_flag_on_with_conditions_can_be_saved_through_the_api_again()
    {
        TestSite site = Site();
        site.Store.With("Cultured", new FlagSettings(true, [Culture("fr")]));

        var result = await Manager(site).Save("Cultured", new SaveFlagRequestModel { Enabled = false }, default);

        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.OkObjectResult>());
        Assert.That(site.Store.All.Single().Definition, Does.Not.Contain("Contoso.Culture"));
    }

    [Test]
    public void Add_condition_registers_each_condition_once_beside_the_audience()
    {
        var services = new ServiceCollection();
        new FeatureFlagsBuilder(services).AddCondition<CultureCondition>().AddCondition<CultureCondition>();

        Assert.That(services.Count(service => service.ServiceType == typeof(IFeatureFlagCondition)), Is.EqualTo(1));
        var site = Site();
        Assert.That(site.Provider.GetRequiredService<ConditionRegistry>().All.Select(c => c.Name), Is.EquivalentTo(new[] { "Microsoft.Targeting", "Microsoft.TimeWindow", "Umbricians.Root", "Contoso.Culture" }));
    }

    [TestCase("Microsoft.Targeting", "Microsoft.Targeting")]
    [TestCase("Targeting", "Microsoft.Targeting")]
    [TestCase("targeting", "Microsoft.Targeting")]
    [TestCase("TargetingFilter", null)]
    [TestCase("Culture", "Contoso.Culture")]
    [TestCase("contoso.CULTURE", "Contoso.Culture")]
    [TestCase("Contoso.Culture", "Contoso.Culture")]
    [TestCase("Microsoft.Culture", null)]
    [TestCase("oso.Culture", null)]
    [TestCase("Time", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void Finds_a_condition_by_the_filter_name_the_way_microsoft_matches_filters(string? filterName, string? expected)
    {
        var registry = Site().Provider.GetRequiredService<ConditionRegistry>();

        Assert.That(registry.Find(filterName)?.Name, Is.EqualTo(expected));
    }

    private sealed class ProductCultureCondition : IFeatureFlagCondition
    {
        public string Name => "Contoso.Product.Culture";

        public string DisplayName => "Product culture";

        public IEnumerable<string> Validate(JsonObject parameters) => [];

        public string Describe(JsonObject parameters) => "";
    }

    [TestCase("Culture", true)]
    [TestCase("culture", true)]
    [TestCase("Product.Culture", false)]
    [TestCase("Contoso.Product.Culture", true)]
    [TestCase("contoso.product.culture", true)]
    public void A_name_with_a_dot_must_match_exactly_and_one_without_matches_the_last_segment(string filterName, bool found)
    {
        var registry = new ConditionRegistry([new ProductCultureCondition()]);

        Assert.That(registry.Find(filterName) is not null, Is.EqualTo(found));
    }

    [Test]
    public async Task Targeting_with_a_percentage_is_an_audience_the_backoffice_can_edit()
    {
        TestSite site = Site();
        var targeting = new FlagCondition("Microsoft.Targeting", new JsonObject
        {
            ["Audience"] = new JsonObject { ["Users"] = new JsonArray(), ["Groups"] = new JsonArray(), ["DefaultRolloutPercentage"] = 50 },
        });
        site.Store.With("Aud", new FlagSettings(true, [targeting]));

        FeatureFlag flag = await FlagAsync(site, "Aud");

        Assert.That(flag.State, Is.EqualTo(FeatureFlagState.Audience));
        Assert.That(flag.Actions.Save.Allowed, Is.True);
    }

    private const string DisabledWithConditions = """
        { "feature_management": { "feature_flags": [
            { "id": "Dormant", "enabled": false, "conditions": { "requirement_type": "All", "client_filters": [
                { "name": "Contoso.Culture", "parameters": { "Cultures": [ "fr" ] } },
                { "name": "Microsoft.Targeting", "parameters": { "Audience": { "Users": [ "m1" ], "DefaultRolloutPercentage": 0 } } } ] } },
            { "id": "Plain", "enabled": false, "conditions": { "client_filters": [ { "name": "AlwaysOn" } ] } } ] } }
        """;

    [Test]
    public async Task A_default_configured_off_keeps_its_conditions_so_switching_it_on_does_not_lose_them()
    {
        var site = new TestSite(DisabledWithConditions, before: services => services.AddSingleton<IFeatureFlagCondition, CultureCondition>());

        FeatureFlag flag = await FlagAsync(site, "Dormant");

        Assert.That(flag.State, Is.EqualTo(FeatureFlagState.Off));
        Assert.That(flag.DefaultSettings!.Conditions.Select(condition => condition.Name), Is.EqualTo(new[] { "Contoso.Culture", "Microsoft.Targeting" }));
        Assert.That(flag.DefaultSettings.Match, Is.EqualTo(ConditionMatch.All));

        Assert.That((await Service(site).SwitchAsync("Dormant", true)).IsSaved, Is.True);

        FeatureFlag after = await FlagAsync(site, "Dormant");
        Assert.That(after.State, Is.EqualTo(FeatureFlagState.Conditions));
        Assert.That(after.Settings!.Conditions.Select(condition => condition.Name), Is.EqualTo(new[] { "Contoso.Culture", "Microsoft.Targeting" }));
        Assert.That(after.Settings.Match, Is.EqualTo(ConditionMatch.All));
    }

    [Test]
    public async Task A_default_configured_off_with_only_an_always_on_filter_has_no_conditions()
    {
        var site = new TestSite(DisabledWithConditions);

        FeatureFlag flag = await FlagAsync(site, "Plain");

        Assert.That(flag.DefaultSettings, Is.EqualTo(FlagSettings.Off));
    }

    /// <summary>A filter that is on when its name matched.</summary>
    [FilterAlias("Contoso.Culture")]
    private sealed class CultureFilter : IFeatureFilter
    {
        public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context) => Task.FromResult(true);
    }

    [TestCase("Contoso.Culture")]
    [TestCase("Culture")]
    [TestCase("culture")]
    [TestCase("CultureFilter")]
    [TestCase("Microsoft.Culture")]
    [TestCase("oso.Culture")]
    [TestCase("Cult")]
    public async Task Matches_the_same_names_microsoft_does(string filterName)
    {
        string json = $$"""{ "feature_management": { "feature_flags": [ { "id": "F", "enabled": true, "conditions": { "client_filters": [ { "name": "{{filterName}}" } ] } } ] } }""";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).Build());
        services.AddFeatureManagement().AddFeatureFilter<CultureFilter>();
        bool microsoftMatches;
        try
        {
            microsoftMatches = await services.BuildServiceProvider().GetRequiredService<IFeatureManager>().IsEnabledAsync("F");
        }
        catch (FeatureManagementException)
        {
            microsoftMatches = false;
        }

        var registry = Site().Provider.GetRequiredService<ConditionRegistry>();

        Assert.That(registry.Find(filterName)?.Name == "Contoso.Culture", Is.EqualTo(microsoftMatches));
    }
}
