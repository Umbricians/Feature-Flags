using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>What each flag is, where it is declared, and what applies now.</summary>
public class FlagResolutionTests
{
    private const string DeclaredOff = """{ "feature_management": { "feature_flags": [ { "id": "F", "enabled": false } ] } }""";

    [Test]
    public async Task An_override_applies_to_a_declared_flag()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("F", new FlagSettings(true, null));

        ResolvedFlag? flag = await Resolution(site).FindAsync("F");

        Assert.That(flag, Is.Not.Null);
        Assert.That(flag!.IsDeclared, Is.True);
        Assert.That(flag.OverrideApplies, Is.True);
        Assert.That(flag.Definition, Is.SameAs(flag.Override!.Definition));
    }

    [Test]
    public async Task An_override_does_not_apply_when_overrides_are_read_only()
    {
        var site = new TestSite("""
            { "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } },
              "feature_management": { "feature_flags": [ { "id": "F", "enabled": false } ] } }
            """);
        site.Store.With("F", new FlagSettings(true, null));

        ResolvedFlag flag = (await Resolution(site).FindAsync("F"))!;
        ResolvedFlag listed = (await Resolution(site).GetAllAsync(default)).Flags.Single();

        Assert.That(flag.OverrideApplies, Is.False);
        Assert.That(flag.Definition, Is.SameAs(flag.Declared!.Default));
        Assert.That(listed, Has.Property(nameof(ResolvedFlag.OverrideApplies)).False
            .And.Property(nameof(ResolvedFlag.Override)).Not.Null);
    }

    [Test]
    public async Task An_override_whose_json_can_not_be_read_does_not_apply()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.WithJson("F", "not json");

        ResolvedFlag flag = (await Resolution(site).FindAsync("F"))!;

        Assert.That(flag.OverrideApplies, Is.False);
        Assert.That(flag.Override, Is.Not.Null);
        Assert.That(flag.Definition, Is.SameAs(flag.Declared!.Default));
    }

    [Test]
    public async Task The_flag_source_wins_over_code_and_the_flag_is_declared_twice()
    {
        var site = new TestSite(DeclaredOff, before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("F", true), FlagDetails.None)));

        ResolvedFlag flag = (await Resolution(site).FindAsync("F"))!;
        DeclaredFlag listed = (await Resolution(site).GetDeclaredAsync(default)).Flags.Single();

        Assert.That(flag.Declared, Has.Property(nameof(DeclaredFlag.DeclaredIn)).EqualTo(FlagDeclaredIn.Configuration)
            .And.Property(nameof(DeclaredFlag.DeclaredTwice)).True);
        Assert.That(flag.Definition!.Status, Is.EqualTo(FeatureStatus.Disabled));
        Assert.That(listed, Has.Property(nameof(DeclaredFlag.DeclaredIn)).EqualTo(FlagDeclaredIn.Configuration)
            .And.Property(nameof(DeclaredFlag.DeclaredTwice)).True);
    }

    [Test]
    public async Task A_flag_only_code_declares_is_declared_in_code_and_a_flag_nobody_declares_is_not_found()
    {
        var site = new TestSite(before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("DarkMode", true), FlagDetails.None)));

        ResolvedFlag flag = (await Resolution(site).FindAsync("DarkMode"))!;

        Assert.That(flag.Declared, Has.Property(nameof(DeclaredFlag.DeclaredIn)).EqualTo(FlagDeclaredIn.Code)
            .And.Property(nameof(DeclaredFlag.DeclaredTwice)).False);
        Assert.That(await Resolution(site).FindAsync("Nobody"), Is.Null);
    }

    [Test]
    public async Task An_override_saved_under_another_spelling_applies_without_changing_the_cached_definition()
    {
        var site = new TestSite("""{ "feature_management": { "feature_flags": [ { "id": "Checkout.NewFlow", "enabled": false } ] } }""");
        site.Store.WithJson("checkout.newflow", FlagDefinitions.ToJson("checkout.newflow", new FlagSettings(true, null)));

        ResolvedFlag flag = (await Resolution(site).FindAsync("Checkout.NewFlow"))!;
        ResolvedFlag listed = (await Resolution(site).GetAllAsync(default)).Flags.Single();

        Assert.That(flag.OverrideApplies, Is.True);
        Assert.That(flag.Definition!.Name, Is.EqualTo("Checkout.NewFlow"));
        Assert.That(flag.Definition.EnabledFor, Is.EqualTo(flag.Override!.Definition!.EnabledFor));
        Assert.That(flag.Override.Definition.Name, Is.EqualTo("checkout.newflow"));
        Assert.That(listed.Definition!.Name, Is.EqualTo("Checkout.NewFlow"));
        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.True);
    }

    [Test]
    public async Task Overrides_nobody_declares_are_listed_after_the_declared_flags()
    {
        var site = new TestSite(DeclaredOff, before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("DarkMode", true), FlagDetails.None)));
        site.Store.With("Promo.Removed", FlagSettings.Off);

        ResolvedFlags all = await Resolution(site).GetAllAsync(default);

        Assert.That(all.Flags.Select(flag => flag.Name), Is.EqualTo(new[] { "F", "DarkMode", "Promo.Removed" }));
        Assert.That(all.Flags[2], Has.Property(nameof(ResolvedFlag.IsDeclared)).False
            .And.Property(nameof(ResolvedFlag.OverrideApplies)).False
            .And.Property(nameof(ResolvedFlag.Definition)).Null
            .And.Property(nameof(ResolvedFlag.Override)).Not.Null);
        Assert.That(all.ListingError, Is.Null);
    }

    [Test]
    public async Task Listing_declared_flags_never_reads_the_overrides()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.Fail = true;

        DeclaredFlags declared = await Resolution(site).GetDeclaredAsync(default);

        Assert.That(declared.Flags.Select(flag => flag.Name), Is.EqualTo(new[] { "F" }));
        Assert.That(site.Store.Reads, Is.EqualTo(0));
    }

    [Test]
    public async Task A_flag_source_that_can_not_list_still_gives_each_flag_it_can_read_and_each_override_name()
    {
        var site = new TestSite(before: services =>
        {
            services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource());
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("B", false), FlagDetails.None));
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Broken", false), FlagDetails.None));
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("OnlyCode", false), FlagDetails.None));
        });
        site.Store.With("C", FlagSettings.Off).With("Nobody", FlagSettings.Off);

        ResolvedFlags all = await Resolution(site).GetAllAsync(default);
        Dictionary<string, ResolvedFlag> flags = all.Flags.ToDictionary(flag => flag.Name);

        Assert.That(all.ListingError, Is.Not.Null);
        Assert.That(flags.Keys, Is.EquivalentTo(new[] { "A", "B", "C", "Broken", "OnlyCode", "Nobody" }));
        Assert.That(flags["B"].Declared, Has.Property(nameof(DeclaredFlag.DeclaredIn)).EqualTo(FlagDeclaredIn.FlagSource)
            .And.Property(nameof(DeclaredFlag.DeclaredTwice)).True);
        Assert.That(flags["C"], Has.Property(nameof(ResolvedFlag.IsDeclared)).True.And.Property(nameof(ResolvedFlag.OverrideApplies)).True);
        Assert.That(flags["Broken"].Declared!.ReadError, Is.Not.Null);
        Assert.That(flags["Broken"], Has.Property(nameof(ResolvedFlag.Definition)).Null.And.Property(nameof(ResolvedFlag.OverrideApplies)).False);
        Assert.That(flags["OnlyCode"].Declared!.DeclaredIn, Is.EqualTo(FlagDeclaredIn.Code));
        Assert.That(flags["Nobody"].IsDeclared, Is.False);
    }

    [Test]
    public void A_flag_source_that_throws_for_a_flag_surfaces_it_on_a_check()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource()));

        Assert.ThrowsAsync<InvalidOperationException>(() => Resolution(site).FindAsync("Broken"));
    }

    [Test]
    public void Cancelling_stops_the_one_by_one_read()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource()));
        site.Store.With("C", FlagSettings.Off);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => Resolution(site).GetAllAsync(cancelled.Token));
    }

    [Test]
    public async Task Microsoft_lists_each_declared_flag_with_what_applies_now()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("F", new FlagSettings(true, null)).With("Promo.Removed", FlagSettings.Off);

        var listed = new List<FeatureDefinition>();
        await foreach (FeatureDefinition definition in site.Provider.GetRequiredService<IFeatureDefinitionProvider>().GetAllFeatureDefinitionsAsync())
        {
            listed.Add(definition);
        }

        Assert.That(listed.Select(definition => definition.Name), Is.EqualTo(new[] { "F" }));
        Assert.That(listed[0].EnabledFor, Is.Not.Empty);
    }

    [Test]
    public void Microsoft_sees_the_flag_sources_exception_when_it_can_not_list()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource()));
        IFeatureDefinitionProvider provider = site.Provider.GetRequiredService<IFeatureDefinitionProvider>();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (FeatureDefinition unused in provider.GetAllFeatureDefinitionsAsync())
            {
            }
        });
    }

    private static FlagResolution Resolution(TestSite site) => site.Provider.GetRequiredService<FlagResolution>();
}
