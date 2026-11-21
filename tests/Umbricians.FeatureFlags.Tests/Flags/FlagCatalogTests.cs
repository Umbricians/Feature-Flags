using Microsoft.Extensions.DependencyInjection;
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
            .And.Property(nameof(CatalogFlag.Editable)).False);
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
            .And.Property(nameof(CatalogFlag.Editable)).False);
    }

    [Test]
    public async Task Pages_after_searching_filtering_and_sorting_every_flag()
    {
        var site = new TestSite(Flags);
        site.Store.With("Search.Suggestions", FlagSettings.Off).With("Promo.Removed", FlagSettings.Off);
        IReadOnlyList<CatalogFlag> all = await Catalog(site).GetAllAsync(default);

        (IReadOnlyList<CatalogFlag> firstPage, int total) = new FlagQuery().Run(all, 0, 2);
        (IReadOnlyList<CatalogFlag> search, _) = new FlagQuery(Filter: "REDESIGNED").Run(all, 0, 10);
        (IReadOnlyList<CatalogFlag> on, _) = new FlagQuery(State: FlagDisplayState.Everyone).Run(all, 0, 10);
        (IReadOnlyList<CatalogFlag> changed, _) = new FlagQuery(Marks: FlagMarks.Changed).Run(all, 0, 10);
        (IReadOnlyList<CatalogFlag> undeclared, _) = new FlagQuery(Undeclared: true).Run(all, 0, 10);
        (IReadOnlyList<CatalogFlag> checkout, _) = new FlagQuery(Section: "checkout").Run(all, 0, 10);
        (IReadOnlyList<CatalogFlag> noSection, _) = new FlagQuery(NoSectionOnly: true).Run(all, 0, 10);
        (IReadOnlyList<CatalogFlag> tagged, _) = new FlagQuery(Tag: "Beta").Run(all, 0, 10);

        Assert.That(total, Is.EqualTo(4));
        Assert.That(firstPage.Select(f => f.Name), Is.EqualTo(new[] { "Checkout.GiftCards", "Checkout.NewFlow" }));
        Assert.That(search.Select(f => f.Name), Is.EqualTo(new[] { "Checkout.NewFlow" }));
        Assert.That(on.Select(f => f.Name), Is.EqualTo(new[] { "Checkout.GiftCards" }));
        Assert.That(changed.Select(f => f.Name), Is.EqualTo(new[] { "Search.Suggestions" }));
        Assert.That(undeclared.Select(f => f.Name), Is.EqualTo(new[] { "Promo.Removed" }));
        Assert.That(checkout.Select(f => f.Name), Is.EqualTo(new[] { "Checkout.NewFlow" }));
        Assert.That(noSection.Select(f => f.Name), Is.EqualTo(new[] { "Sale" }));
        Assert.That(tagged.Select(f => f.Name), Is.EqualTo(new[] { "Checkout.NewFlow" }));
    }

    [Test]
    public async Task Narrows_to_flags_with_every_mark_asked_for()
    {
        var site = new TestSite(Flags, before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Search.Suggestions", false), FlagDetails.None)));
        site.Store.With("Search.Suggestions", FlagSettings.Off).With("Sale", FlagSettings.Off);
        IReadOnlyList<CatalogFlag> all = await Catalog(site).GetAllAsync(default);

        (IReadOnlyList<CatalogFlag> changed, _) = new FlagQuery(Marks: FlagMarks.Changed).Run(all, 0, 10);
        (IReadOnlyList<CatalogFlag> changedAndTwice, _) = new FlagQuery(Marks: FlagMarks.Changed | FlagMarks.DeclaredTwice).Run(all, 0, 10);

        Assert.That(changed.Select(f => f.Name), Is.EquivalentTo(new[] { "Search.Suggestions", "Sale" }));
        Assert.That(changedAndTwice.Select(f => f.Name), Is.EqualTo(new[] { "Search.Suggestions" }));
    }

    [Test]
    public async Task Lists_the_most_recently_changed_first()
    {
        var site = new TestSite(Flags);
        site.Store.With("Sale", FlagSettings.Off);
        await Task.Delay(5);
        site.Store.With("Checkout.GiftCards", FlagSettings.Off);

        (IReadOnlyList<CatalogFlag> items, _) = new FlagQuery(OrderBy: FlagQueryOrder.Updated).Run(await Catalog(site).GetAllAsync(default), 0, 2);

        Assert.That(items.Select(f => f.Name), Is.EqualTo(new[] { "Checkout.GiftCards", "Sale" }));
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
        Assert.That(flags["Broken"], Has.Property(nameof(CatalogFlag.Editable)).False
            .And.Property(nameof(CatalogFlag.State)).EqualTo(FlagDisplayState.Custom));
    }

    private static FlagCatalog Catalog(TestSite site) => site.Provider.GetRequiredService<FlagCatalog>();
}
