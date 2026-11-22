using Microsoft.Extensions.DependencyInjection;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>The flag list as the backoffice asks for it: what it refuses, what its words mean, and its summary.</summary>
public class FlagListTests
{
    private const string Flags = """
        { "feature_management": { "feature_flags": [
            { "id": "Checkout.NewFlow", "enabled": false, "description": "The redesigned checkout.", "tags": [ "beta" ] },
            { "id": "Checkout.GiftCards", "enabled": true, "section": "Payments" },
            { "id": "Search.Suggestions", "enabled": true, "tags": [ "Alpha", "beta" ] },
            { "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Percentage", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } } ] } }
        """;

    [Test]
    public async Task Pages_after_searching_filtering_and_sorting_every_flag()
    {
        var site = new TestSite(Flags);
        site.Store.With("Search.Suggestions", FlagSettings.Off).With("Promo.Removed", FlagSettings.Off);
        FlagList list = List(site);

        FlagListResult firstPage = await list.PageAsync(new FlagListRequest(Take: 2), default);
        FlagListResult search = await list.PageAsync(new FlagListRequest(Filter: "REDESIGNED"), default);
        FlagListResult on = await list.PageAsync(new FlagListRequest(State: "everyone"), default);
        FlagListResult changed = await list.PageAsync(new FlagListRequest(Marks: "changed"), default);
        FlagListResult undeclared = await list.PageAsync(new FlagListRequest(Show: "undeclared"), default);
        FlagListResult checkout = await list.PageAsync(new FlagListRequest(Section: "checkout"), default);
        FlagListResult noSection = await list.PageAsync(new FlagListRequest(Section: ""), default);
        FlagListResult tagged = await list.PageAsync(new FlagListRequest(Tag: "Beta"), default);
        FlagListResult bySection = await list.PageAsync(new FlagListRequest(OrderBy: "section"), default);

        Assert.That(firstPage.Total, Is.EqualTo(4));
        Assert.That(Names(firstPage), Is.EqualTo(new[] { "Checkout.GiftCards", "Checkout.NewFlow" }));
        Assert.That(Names(search), Is.EqualTo(new[] { "Checkout.NewFlow" }));
        Assert.That(Names(on), Is.EqualTo(new[] { "Checkout.GiftCards" }));
        Assert.That(Names(changed), Is.EqualTo(new[] { "Search.Suggestions" }));
        Assert.That(Names(undeclared), Is.EqualTo(new[] { "Promo.Removed" }));
        Assert.That(Names(checkout), Is.EqualTo(new[] { "Checkout.NewFlow" }));
        Assert.That(Names(noSection), Is.EqualTo(new[] { "Sale" }));
        Assert.That(Names(tagged), Is.EqualTo(new[] { "Checkout.NewFlow", "Search.Suggestions" }));
        Assert.That(Names(bySection), Is.EqualTo(new[] { "Checkout.NewFlow", "Checkout.GiftCards", "Search.Suggestions", "Sale" }));
    }

    [Test]
    public async Task Narrows_to_flags_with_every_mark_asked_for()
    {
        var site = new TestSite(Flags, before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Search.Suggestions", false), FlagDetails.None)));
        site.Store.With("Search.Suggestions", FlagSettings.Off).With("Sale", FlagSettings.Off);

        FlagListResult changed = await List(site).PageAsync(new FlagListRequest(Marks: "changed"), default);
        FlagListResult changedAndTwice = await List(site).PageAsync(new FlagListRequest(Marks: "changed,twice"), default);
        FlagListResult differs = await List(site).PageAsync(new FlagListRequest(Marks: "differs"), default);

        Assert.That(Names(changed), Is.EquivalentTo(new[] { "Search.Suggestions", "Sale" }));
        Assert.That(Names(changedAndTwice), Is.EqualTo(new[] { "Search.Suggestions" }));
        Assert.That(Names(differs), Is.EquivalentTo(new[] { "Search.Suggestions", "Sale" }));
    }

    [Test]
    public async Task Lists_the_most_recently_changed_first()
    {
        var site = new TestSite(Flags);
        site.Store.With("Sale", FlagSettings.Off);
        await Task.Delay(5);
        site.Store.With("Checkout.GiftCards", FlagSettings.Off);

        FlagListResult result = await List(site).PageAsync(new FlagListRequest(Take: 2, OrderBy: "updated"), default);

        Assert.That(Names(result), Is.EqualTo(new[] { "Checkout.GiftCards", "Sale" }));
    }

    [TestCase(-1, 25)]
    [TestCase(0, 0)]
    [TestCase(0, 101)]
    public async Task Refuses_a_page_outside_the_limits(int skip, int take)
    {
        FlagListResult result = await List(new TestSite(Flags)).PageAsync(new FlagListRequest(skip, take), default);

        Assert.That(result.Problem, Has.Property(nameof(FlagListProblem.Title)).EqualTo("Invalid page")
            .And.Property(nameof(FlagListProblem.Detail)).EqualTo("skip must be 0 or more, and take between 1 and 100."));
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Accepts_the_largest_page()
    {
        FlagListResult result = await List(new TestSite(Flags)).PageAsync(new FlagListRequest(Take: FlagList.MaxPageSize), default);

        Assert.That(result.Problem, Is.Null);
        Assert.That(result.Items, Has.Count.EqualTo(4));
    }

    [TestCase("maybe", null, null, null)]
    [TestCase(null, "all", null, null)]
    [TestCase(null, null, "changed,nope", null)]
    [TestCase(null, null, null, "size")]
    public async Task Refuses_filter_values_it_does_not_know(string? state, string? show, string? marks, string? orderBy)
    {
        FlagListResult result = await List(new TestSite(Flags)).PageAsync(new FlagListRequest(State: state, Show: show, Marks: marks, OrderBy: orderBy), default);

        Assert.That(result.Problem, Has.Property(nameof(FlagListProblem.Title)).EqualTo("Invalid filter")
            .And.Property(nameof(FlagListProblem.Detail)).EqualTo("state, show, marks or orderBy has a value the API doesn't know."));
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Reads_names_ignoring_case_and_spaces()
    {
        var site = new TestSite(Flags, before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Search.Suggestions", false), FlagDetails.None)));
        site.Store.With("Search.Suggestions", FlagSettings.Off);

        FlagListResult everyone = await List(site).PageAsync(new FlagListRequest(State: "EVERYONE", Show: "Declared", OrderBy: "NAME"), default);
        FlagListResult marked = await List(site).PageAsync(new FlagListRequest(Marks: " changed , twice "), default);

        Assert.That(Names(everyone), Is.EqualTo(new[] { "Checkout.GiftCards" }));
        Assert.That(Names(marked), Is.EqualTo(new[] { "Search.Suggestions" }));
    }

    [Test]
    public async Task An_empty_section_means_flags_in_no_section_and_none_means_every_section()
    {
        FlagList list = List(new TestSite(Flags));

        Assert.That(Names(await list.PageAsync(new FlagListRequest(Section: ""), default)), Is.EqualTo(new[] { "Sale" }));
        Assert.That((await list.PageAsync(new FlagListRequest(Section: null), default)).Total, Is.EqualTo(4));
        Assert.That(Names(await list.PageAsync(new FlagListRequest(Section: "PAYMENTS"), default)), Is.EqualTo(new[] { "Checkout.GiftCards" }));
    }

    [Test]
    public async Task Summarises_sections_with_no_section_last_and_tags_in_order()
    {
        var site = new TestSite("""
            { "feature_management": { "feature_flags": [
                { "id": "Plain", "enabled": true, "tags": [ "beta" ] },
                { "id": "zeta.One", "enabled": true },
                { "id": "Alpha.One", "enabled": true, "tags": [ "Beta", "alpha" ] },
                { "id": "alpha.Two", "enabled": true } ] } }
            """);

        FlagListSummary summary = await List(site).SummaryAsync(default);

        Assert.That(summary.Sections.Select(s => (s.Name, s.Count)), Is.EqualTo(new (string?, int)[] { ("Alpha", 2), ("zeta", 1), (null, 1) }));
        Assert.That(summary.Tags, Is.EqualTo(new[] { "alpha", "beta" }));
    }

    [Test]
    public async Task The_summary_counts_undeclared_flags_apart_and_says_whether_overrides_are_read_only()
    {
        var site = new TestSite(Flags);
        site.Store.With("Promo.Removed", FlagSettings.Off).With("Old", FlagSettings.Off);
        var readOnly = new TestSite("""{ "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } } }""");

        FlagListSummary summary = await List(site).SummaryAsync(default);

        Assert.That((summary.Total, summary.Undeclared, summary.ReadOnly), Is.EqualTo((4, 2, false)));
        Assert.That((await List(readOnly).SummaryAsync(default)).ReadOnly, Is.True);
    }

    private static string[] Names(FlagListResult result) => result.Items.Select(flag => flag.Name).ToArray();

    private static FlagList List(TestSite site) => site.Provider.GetRequiredService<FlagList>();
}
