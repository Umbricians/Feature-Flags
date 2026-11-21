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

        Dictionary<string, CatalogFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags.Keys, Is.EquivalentTo(new[] { "Checkout.NewFlow", "Checkout.GiftCards", "Search.Suggestions", "Sale", "DarkMode" }));
        Assert.That(flags["Checkout.NewFlow"].Details, Is.EqualTo(new FlagDetails("The redesigned checkout.", "Checkout", flags["Checkout.NewFlow"].Details.Tags)));
        Assert.That(flags["Checkout.NewFlow"].Details.Tags, Is.EqualTo(new[] { "beta" }));
        Assert.That(flags["Checkout.GiftCards"].Details.Section, Is.EqualTo("Payments"));
        Assert.That(flags["DarkMode"], Has.Property(nameof(CatalogFlag.DeclaredIn)).EqualTo(FlagDeclaredIn.Code)
            .And.Property(nameof(CatalogFlag.Details)).Property(nameof(FlagDetails.Section)).Null);
        Assert.That(flags["Search.Suggestions"], Has.Property(nameof(CatalogFlag.DeclaredTwice)).True
            .And.Property(nameof(CatalogFlag.DeclaredIn)).EqualTo(FlagDeclaredIn.Configuration)
            .And.Property(nameof(CatalogFlag.State)).EqualTo(FlagDisplayState.Everyone));
        Assert.That(flags["Search.Suggestions"].Details.Description, Is.EqualTo("From code."));
        Assert.That(flags["Sale"], Has.Property(nameof(CatalogFlag.State)).EqualTo(FlagDisplayState.Custom)
            .And.Property(nameof(CatalogFlag.Actions)).Property(nameof(FlagActions.Save)).Property(nameof(FlagAction.Allowed)).False);
    }

    [Test]
    public async Task Marks_overrides_and_whether_they_differ_from_the_default()
    {
        var site = new TestSite(Flags);
        site.Store.With("Checkout.NewFlow", FlagSettings.Off).With("Checkout.GiftCards", FlagSettings.Off).With("Promo.Removed", FlagSettings.Off);

        Dictionary<string, CatalogFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags["Checkout.NewFlow"], Has.Property(nameof(CatalogFlag.DiffersFromDefault)).False);
        Assert.That(flags["Checkout.GiftCards"], Has.Property(nameof(CatalogFlag.DiffersFromDefault)).True
            .And.Property(nameof(CatalogFlag.State)).EqualTo(FlagDisplayState.Off));
        Assert.That(flags["Promo.Removed"], Has.Property(nameof(CatalogFlag.IsDeclared)).False
            .And.Property(nameof(CatalogFlag.Actions)).Property(nameof(FlagActions.Save)).Property(nameof(FlagAction.Allowed)).False);
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

        Dictionary<string, CatalogFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags.Keys, Is.EquivalentTo(new[] { "Good", "Broken" }));
        Assert.That(flags["Good"].State, Is.EqualTo(FlagDisplayState.Everyone));
        Assert.That(flags["Broken"], Has.Property(nameof(CatalogFlag.Actions)).Property(nameof(FlagActions.Save)).Property(nameof(FlagAction.Allowed)).False
            .And.Property(nameof(CatalogFlag.State)).EqualTo(FlagDisplayState.Custom));
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

        Dictionary<string, CatalogFlag> flags = (await Catalog(site).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        Assert.That(flags["A"].DeclaredIn, Is.EqualTo(FlagDeclaredIn.FlagSource));
        Assert.That(flags["B"], Has.Property(nameof(CatalogFlag.DeclaredIn)).EqualTo(FlagDeclaredIn.FlagSource)
            .And.Property(nameof(CatalogFlag.DeclaredTwice)).True);
        Assert.That(flags["C"], Has.Property(nameof(CatalogFlag.IsDeclared)).True.And.Property(nameof(CatalogFlag.Override)).Not.Null);
        Assert.That(flags["Broken"], Has.Property(nameof(CatalogFlag.Actions)).Property(nameof(FlagActions.Save)).Property(nameof(FlagAction.Allowed)).False
            .And.Property(nameof(CatalogFlag.State)).EqualTo(FlagDisplayState.Custom));
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
        site.Store.With("F", new FlagSettings(true, null));

        CatalogFlag flag = (await Catalog(site).FindAsync("F", default))!;

        Assert.That(flag, Has.Property(nameof(CatalogFlag.State)).EqualTo(FlagDisplayState.Off)
            .And.Property(nameof(CatalogFlag.Actions)).Property(nameof(FlagActions.Save)).Property(nameof(FlagAction.Allowed)).False
            .And.Property(nameof(CatalogFlag.DiffersFromDefault)).True
            .And.Property(nameof(CatalogFlag.Override)).Not.Null);
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
        IReadOnlyList<CatalogFlag> all = await Catalog(site).GetAllAsync(default);
        FlagActions Of(string name) => all.Single(flag => flag.Name == name).Actions;
        Dictionary<string, CatalogFlag> readOnly = (await Catalog(readOnlySite).GetAllAsync(default)).ToDictionary(flag => flag.Name);

        FlagActions withOverride = Of("Checkout.GiftCards");
        Assert.That((withOverride.Switch.Allowed, withOverride.Save.Allowed, withOverride.Reset.Allowed, withOverride.Remove.Allowed),
            Is.EqualTo((true, true, true, false)));
        Assert.That(withOverride.Remove.Reason, Does.Contain("reset it"));

        FlagActions noOverride = Of("Checkout.NewFlow");
        Assert.That((noOverride.Switch.Allowed, noOverride.Save.Allowed, noOverride.Reset.Allowed), Is.EqualTo((true, true, false)));
        Assert.That(noOverride.Reset.Reason, Does.Contain("already uses its default"));

        // The saved setting is readable, so Sale (declared with a time window) is editable once an override replaces it.
        site.Store.WithJson("Sale", """{ "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } }""");
        site.Refresh();
        FlagActions custom = (await Catalog(site).FindAsync("Sale", default))!.Actions;
        Assert.That((custom.Switch.Allowed, custom.Save.Allowed, custom.Reset.Allowed), Is.EqualTo((false, false, true)));
        Assert.That(custom.Save.Reason, Does.Contain("Microsoft.TimeWindow"));

        FlagActions undeclared = Of("Promo.Removed");
        Assert.That((undeclared.Switch.Allowed, undeclared.Save.Allowed, undeclared.Reset.Allowed, undeclared.Remove.Allowed),
            Is.EqualTo((false, false, false, true)));
        Assert.That(undeclared.Save.Reason, Does.Contain("No flag source"));

        FlagActions undeclaredReadOnly = readOnly["Gone"].Actions;
        Assert.That(new[] { undeclaredReadOnly.Switch, undeclaredReadOnly.Save, undeclaredReadOnly.Reset, undeclaredReadOnly.Remove }.Select(a => a.Allowed),
            Is.EqualTo(new[] { false, false, false, false }));
        Assert.That(undeclaredReadOnly.Remove.Reason, Does.Contain("ReadOnly"));
        Assert.That(readOnly["F"].Actions.Reset.Reason, Does.Contain("ReadOnly"));
    }

    private static FlagCatalog Catalog(TestSite site) => site.Provider.GetRequiredService<FlagCatalog>();
}
