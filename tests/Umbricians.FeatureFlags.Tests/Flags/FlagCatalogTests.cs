using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>The flag list as the backoffice shows it.</summary>
public class FlagCatalogTests
{
    private const string Flags = """
        { "feature_management": { "feature_flags": [
            { "id": "Checkout.NewFlow", "enabled": false, "description": "The redesigned checkout.", "tags": [ "beta" ] },
            { "id": "Checkout.GiftCards", "enabled": true, "section": "Payments" },
            { "id": "Search.Suggestions", "enabled": true },
            { "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } } ] } }
        """;

    [Test]
    public async Task Lists_every_declared_flag_with_its_details_from_configuration_and_code()
    {
        var site = new TestSite(Flags, before: services =>
        {
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("DarkMode", false), new FlagDetails("Dark colours.", null, ["ui"])));
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Search.Suggestions", false), new FlagDetails("From code.", null, [])));
        });

        Dictionary<string, FeatureFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags.Keys, Is.EquivalentTo(new[] { "Checkout.NewFlow", "Checkout.GiftCards", "Search.Suggestions", "Sale", "DarkMode" }));
        Assert.That(flags["Checkout.NewFlow"].Tags, Is.EqualTo(new[] { "beta" }));
        Assert.That(flags["Checkout.GiftCards"].Section, Is.EqualTo("Payments"));
        Assert.That(flags["DarkMode"], Has.Property(nameof(FeatureFlag.DeclaredIn)).EqualTo(FeatureFlagDeclaredIn.Code)
            .And.Property(nameof(FeatureFlag.Section)).Null);
        Assert.That(flags["Search.Suggestions"], Has.Property(nameof(FeatureFlag.DeclaredTwice)).True
            .And.Property(nameof(FeatureFlag.DeclaredIn)).EqualTo(FeatureFlagDeclaredIn.Configuration)
            .And.Property(nameof(FeatureFlag.State)).EqualTo(FeatureFlagState.Everyone));
        Assert.That(flags["Search.Suggestions"].Description, Is.EqualTo("From code."));
        Assert.That(flags["Sale"], Has.Property(nameof(FeatureFlag.State)).EqualTo(FeatureFlagState.Custom)
            .And.Property(nameof(FeatureFlag.Actions)).Property(nameof(FeatureFlagActions.Save)).Property(nameof(FeatureFlagAction.Allowed)).False);
    }

    [Test]
    public async Task A_flag_is_public_when_code_configuration_or_make_public_says_so()
    {
        var site = new TestSite(
            """
            { "feature_management": { "feature_flags": [
                { "id": "FromConfig", "enabled": false, "public": true },
                { "id": "Named", "enabled": false },
                { "id": "ConfigSaysNo", "enabled": false, "public": false },
                { "id": "Private", "enabled": false } ] } }
            """,
            before: services =>
            {
                services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("FromCode", false), new FlagDetails(null, null, [], IsPublic: true)));
                services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("ConfigSaysNo", false), new FlagDetails(null, null, [], IsPublic: true)));
                services.AddSingleton(new PublicFlagNames(["NAMED"]));
            });

        Dictionary<string, FeatureFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags.ToDictionary(flag => flag.Key, flag => flag.Value.IsPublic),
            Is.EqualTo(new Dictionary<string, bool> { ["FromConfig"] = true, ["Named"] = true, ["Private"] = false, ["FromCode"] = true, ["ConfigSaysNo"] = false }));
    }

    [Test]
    public async Task Conditions_the_backoffice_does_not_know_stay_custom_while_a_single_audience_is_an_audience()
    {
        var site = new TestSite(Flags);
        site.Store.With("Sale", FlagSettings.ForAudience(true, new FlagAudience(["m"], [])))
            .With("Search.Suggestions", new FlagSettings(true, [new FlagCondition("Acme.X", new System.Text.Json.Nodes.JsonObject())]));

        Dictionary<string, FeatureFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags["Sale"].State, Is.EqualTo(FeatureFlagState.Audience));
        Assert.That(flags["Search.Suggestions"].State, Is.EqualTo(FeatureFlagState.Custom));
        Assert.That(flags["Search.Suggestions"].Settings!.Conditions.Single().Name, Is.EqualTo("Acme.X"));
        Assert.That(ApiNames.State(FeatureFlagState.Conditions), Is.EqualTo("conditions"));
    }

    [Test]
    public async Task Marks_overrides_and_whether_they_differ_from_the_default()
    {
        var site = new TestSite(Flags);
        site.Store.With("Checkout.NewFlow", FlagSettings.Off).With("Checkout.GiftCards", FlagSettings.Off).With("Promo.Removed", FlagSettings.Off);

        Dictionary<string, FeatureFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags["Checkout.NewFlow"], Has.Property(nameof(FeatureFlag.DiffersFromDefault)).False);
        Assert.That(flags["Checkout.GiftCards"], Has.Property(nameof(FeatureFlag.DiffersFromDefault)).True
            .And.Property(nameof(FeatureFlag.State)).EqualTo(FeatureFlagState.Off));
        Assert.That(flags["Promo.Removed"], Has.Property(nameof(FeatureFlag.IsDeclared)).False
            .And.Property(nameof(FeatureFlag.Actions)).Property(nameof(FeatureFlagActions.Save)).Property(nameof(FeatureFlagAction.Allowed)).False);
    }

    [Test]
    public async Task A_flag_with_a_bad_date_is_listed_read_only_without_hiding_the_others()
    {
        // 1 December 2026 is a Tuesday, so Microsoft can't read this date; it only finds out when the flag is checked.
        var site = new TestSite("""
            { "feature_management": { "feature_flags": [
                { "id": "Good", "enabled": true },
                { "id": "Broken", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Dec 2026 00:00:00 GMT" } } ] } } ] } }
            """);

        Dictionary<string, FeatureFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags.Keys, Is.EquivalentTo(new[] { "Good", "Broken" }));
        Assert.That(flags["Good"].State, Is.EqualTo(FeatureFlagState.Everyone));
        Assert.That(flags["Broken"], Has.Property(nameof(FeatureFlag.Actions)).Property(nameof(FeatureFlagActions.Save)).Property(nameof(FeatureFlagAction.Allowed)).False
            .And.Property(nameof(FeatureFlag.State)).EqualTo(FeatureFlagState.Custom));
    }

    [Test]
    public async Task A_site_flag_source_that_can_not_list_its_flags_still_shows_each_one_it_can_read()
    {
        var site = new TestSite(before: services =>
        {
            services.AddFeatureManagement();
            services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource());
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("B", false), FlagDetails.None));
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Broken", false), FlagDetails.None));
        });
        site.Store.With("C", FlagSettings.Off);

        Dictionary<string, FeatureFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags["A"].DeclaredIn, Is.EqualTo(FeatureFlagDeclaredIn.FlagSource));
        Assert.That(flags["B"], Has.Property(nameof(FeatureFlag.DeclaredIn)).EqualTo(FeatureFlagDeclaredIn.FlagSource)
            .And.Property(nameof(FeatureFlag.DeclaredTwice)).True);
        Assert.That(flags["C"], Has.Property(nameof(FeatureFlag.IsDeclared)).True.And.Property(nameof(FeatureFlag.Override)).Not.Null);
        Assert.That(flags["Broken"], Has.Property(nameof(FeatureFlag.Actions)).Property(nameof(FeatureFlagActions.Save)).Property(nameof(FeatureFlagAction.Allowed)).False
            .And.Property(nameof(FeatureFlag.State)).EqualTo(FeatureFlagState.Custom));
    }

    [Test]
    public async Task Finds_a_flag_by_name_whatever_its_case()
    {
        var site = new TestSite(Flags);

        Assert.That((await Catalog(site).FindAsync("checkout.newflow", default))?.Name, Is.EqualTo("Checkout.NewFlow"));
        Assert.That(await Catalog(site).FindAsync("Nobody", default), Is.Null);
    }

    [Test]
    public async Task Overrides_are_shown_but_not_applied_when_they_are_read_only()
    {
        var site = new TestSite("""
            { "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } },
              "feature_management": { "feature_flags": [ { "id": "F", "enabled": false } ] } }
            """);
        site.Store.With("F", FlagSettings.ForAudience(true, null));

        FeatureFlag flag = (await Catalog(site).FindAsync("F", default))!;

        Assert.That(flag, Has.Property(nameof(FeatureFlag.State)).EqualTo(FeatureFlagState.Off)
            .And.Property(nameof(FeatureFlag.Actions)).Property(nameof(FeatureFlagActions.Save)).Property(nameof(FeatureFlagAction.Allowed)).False
            .And.Property(nameof(FeatureFlag.DiffersFromDefault)).True
            .And.Property(nameof(FeatureFlag.Override)).Not.Null);
    }

    [Test]
    public async Task Says_which_actions_each_flag_allows_and_why()
    {
        var site = new TestSite(Flags);
        site.Store.With("Checkout.GiftCards", FlagSettings.Off).With("Promo.Removed", FlagSettings.Off);
        var readOnlySite = new TestSite("""
            { "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } },
              "feature_management": { "feature_flags": [ { "id": "F", "enabled": false } ] } }
            """);
        readOnlySite.Store.With("F", FlagSettings.Off).With("Gone", FlagSettings.Off);
        site.Store.With("Sale", FlagSettings.Off);
        IReadOnlyList<FeatureFlag> all = await Catalog(site).GetAllAsync(default);
        FeatureFlagActions Of(string name) => all.Single(flag => flag.Name == name).Actions;
        Dictionary<string, FeatureFlag> readOnly = (await Catalog(readOnlySite).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        FeatureFlagActions withOverride = Of("Checkout.GiftCards");
        Assert.That((withOverride.Switch.Allowed, withOverride.Save.Allowed, withOverride.Reset.Allowed, withOverride.Remove.Allowed),
            Is.EqualTo((true, true, true, false)));
        Assert.That(withOverride.Remove.Reason, Does.Contain("reset it"));

        FeatureFlagActions noOverride = Of("Checkout.NewFlow");
        Assert.That((noOverride.Switch.Allowed, noOverride.Save.Allowed, noOverride.Reset.Allowed), Is.EqualTo((true, true, false)));
        Assert.That(noOverride.Reset.Reason, Does.Contain("already uses its default"));

        // The saved setting is readable, so Sale (declared with a time window) is editable once an override replaces it.
        site.Store.WithJson("Sale", """{ "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } }""");
        site.Refresh();
        FeatureFlagActions custom = (await Catalog(site).FindAsync("Sale", default))!.Actions;
        Assert.That((custom.Switch.Allowed, custom.Save.Allowed, custom.Reset.Allowed), Is.EqualTo((false, false, true)));
        Assert.That(custom.Save.Reason, Does.Contain("Microsoft.TimeWindow"));

        FeatureFlagActions undeclared = Of("Promo.Removed");
        Assert.That((undeclared.Switch.Allowed, undeclared.Save.Allowed, undeclared.Reset.Allowed, undeclared.Remove.Allowed),
            Is.EqualTo((false, false, false, true)));
        Assert.That(undeclared.Save.Reason, Does.Contain("No flag source"));

        FeatureFlagActions undeclaredReadOnly = readOnly["Gone"].Actions;
        Assert.That(new[] { undeclaredReadOnly.Switch, undeclaredReadOnly.Save, undeclaredReadOnly.Reset, undeclaredReadOnly.Remove }.Select(a => a.Allowed),
            Is.EqualTo(new[] { false, false, false, false }));
        Assert.That(undeclaredReadOnly.Remove.Reason, Does.Contain("ReadOnly"));
        Assert.That(readOnly["F"].Actions.Reset.Reason, Does.Contain("ReadOnly"));
    }

    private static FlagCatalog Catalog(TestSite site) => site.Provider.GetRequiredService<FlagCatalog>();

    [Test]
    public async Task Changing_the_conditions_of_a_flag_that_was_returned_does_not_change_the_flag_the_next_read_returns()
    {
        var site = new TestSite(Flags);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, new FlagAudience(["m1"], [])));
        site.Refresh();

        FeatureFlag first = (await Catalog(site).FindAsync("Checkout.NewFlow", default))!;
        first.Settings!.Conditions[0].Parameters["Audience"] = "changed";
        FeatureFlag second = (await Catalog(site).FindAsync("Checkout.NewFlow", default))!;

        Assert.That(second.Settings!.Audience!.Members, Is.EqualTo(new[] { "m1" }));
        Assert.That(second.State, Is.EqualTo(FeatureFlagState.Audience));
    }

    [Test]
    public async Task Changing_a_default_that_was_returned_does_not_change_the_flag_the_next_read_returns()
    {
        var site = new TestSite("""
            { "feature_management": { "feature_flags": [
                { "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Targeting", "parameters": { "Audience": { "Users": [ "m1" ], "Groups": [], "DefaultRolloutPercentage": 0 } } } ] } } ] } }
            """);

        FeatureFlag first = (await Catalog(site).FindAsync("Sale", default))!;
        first.DefaultSettings!.Conditions[0].Parameters.Clear();
        FeatureFlag second = (await Catalog(site).FindAsync("Sale", default))!;

        Assert.That(second.DefaultSettings!.Conditions[0].Parameters, Is.Not.Empty);
    }

    [Test]
    public async Task Make_public_wins_over_a_flag_that_says_public_false_in_configuration()
    {
        var site = new TestSite(
            """{ "feature_management": { "feature_flags": [ { "id": "ConfigSaysNo", "enabled": false, "public": false } ] } }""",
            before: services => services.AddSingleton(new PublicFlagNames(["configsaysno"])));

        FeatureFlag flag = (await Catalog(site).FindAsync("ConfigSaysNo", default))!;

        Assert.That(flag.IsPublic, Is.True);
    }
}
