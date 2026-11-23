using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>What Microsoft.FeatureManagement says about a flag, given its declaration and any override.</summary>
public class FlagEvaluationTests
{
    private const string Member = "8c2f4a10-3b6e-4d1a-9f5c-2e7b1d0a6c34";
    private const string OtherMember = "1d9e7b42-6a0c-4f3e-8b21-5c4a9e2f7d10";
    private const string BetaTesters = "7f3a9c21-4e8b-4d6f-a1c5-0b2e9d4f8a67";

    private const string DeclaredOff = """{ "feature_management": { "feature_flags": [ { "id": "Checkout.NewFlow", "enabled": false } ] } }""";

    [Test]
    public async Task A_declared_flag_uses_its_default_until_an_editor_changes_it()
    {
        var site = new TestSite(DeclaredOff);

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.False);
    }

    [Test]
    public async Task An_override_on_for_everyone_switches_the_flag_on()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, null));

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.True);
    }

    [Test]
    public async Task An_override_for_chosen_members_is_on_only_for_them()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, new FlagAudience([Member], [])));

        site.Visitor.SignIn(Member);
        bool forMember = await site.IsOnAsync("Checkout.NewFlow");
        site.Visitor.SignIn(OtherMember);
        bool forOther = await site.IsOnAsync("Checkout.NewFlow");
        site.Visitor.SignOut();
        bool forAnonymous = await site.IsOnAsync("Checkout.NewFlow");

        Assert.That((forMember, forOther, forAnonymous), Is.EqualTo((true, false, false)));
    }

    [Test]
    public async Task An_override_for_a_group_is_on_for_its_members()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, new FlagAudience([], [BetaTesters])));

        site.Visitor.SignIn(OtherMember, "Beta testers", BetaTesters);

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.True);
    }

    [Test]
    public async Task A_flag_switched_off_stays_off_for_the_audience_it_keeps()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(false, new FlagAudience([Member], [BetaTesters])));

        site.Visitor.SignIn(Member, BetaTesters);

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.False);
    }

    [Test]
    public async Task An_empty_audience_includes_nobody()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, FlagAudience.Empty));

        site.Visitor.SignIn(Member, BetaTesters);
        bool forMember = await site.IsOnAsync("Checkout.NewFlow");
        site.Visitor.SignOut();
        bool forAnonymous = await site.IsOnAsync("Checkout.NewFlow");

        Assert.That((forMember, forAnonymous), Is.EqualTo((false, false)));
    }

    [Test]
    public async Task An_override_for_a_flag_nobody_declares_has_no_effect()
    {
        var site = new TestSite(DeclaredOff);
        site.Store.With("Promo.Removed", FlagSettings.ForAudience(true, null));

        Assert.That(await site.IsOnAsync("Promo.Removed"), Is.False);
    }

    [Test]
    public async Task Overrides_are_ignored_when_they_are_read_only()
    {
        var site = new TestSite("""
            { "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } },
              "feature_management": { "feature_flags": [ { "id": "Checkout.NewFlow", "enabled": false } ] } }
            """);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, null));

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.False);
    }

    [Test]
    public async Task Flags_declared_in_the_older_FeatureManagement_section_work_too()
    {
        var site = new TestSite("""{ "FeatureManagement": { "BetaSearch": true } }""");

        Assert.That(await site.IsOnAsync("BetaSearch"), Is.True);
    }

    [Test]
    public async Task A_flag_declared_in_code_starts_as_declared_and_takes_overrides()
    {
        var site = new TestSite(before: services => services.AddSingleton(
            new CodeFlagDeclaration(FlagDefinitions.Simple("DarkMode", enabled: true), FlagDetails.None)));

        bool declared = await site.IsOnAsync("DarkMode");
        site.Store.With("DarkMode", FlagSettings.Off);
        site.Refresh();
        bool overridden = await site.IsOnAsync("DarkMode");

        Assert.That((declared, overridden), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task Configuration_wins_when_code_declares_the_same_flag()
    {
        var site = new TestSite(DeclaredOff, before: services => services.AddSingleton(
            new CodeFlagDeclaration(FlagDefinitions.Simple("Checkout.NewFlow", enabled: true), FlagDetails.None)));

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.False);
    }

    [Test]
    public async Task Flags_use_their_defaults_when_the_overrides_can_not_be_read()
    {
        var site = new TestSite("""{ "feature_management": { "feature_flags": [ { "id": "Search", "enabled": true } ] } }""");
        site.Store.With("Search", FlagSettings.Off);
        site.Store.Fail = true;

        Assert.That(await site.IsOnAsync("Search"), Is.True);
    }

    [Test]
    public async Task An_override_whose_json_can_not_be_read_falls_back_to_the_default()
    {
        var site = new TestSite("""{ "feature_management": { "feature_flags": [ { "id": "Search", "enabled": true }, { "id": "Other", "enabled": false } ] } }""");
        site.Store.WithJson("Search", "not json").With("Other", FlagSettings.ForAudience(true, null));

        Assert.That((await site.IsOnAsync("Search"), await site.IsOnAsync("Other")), Is.EqualTo((true, true)));
    }

    [Test]
    public async Task Overrides_are_read_once_and_again_after_a_refresh()
    {
        var site = new TestSite(DeclaredOff);

        await site.IsOnAsync("Checkout.NewFlow");
        await site.IsOnAsync("Checkout.NewFlow");
        int readsBefore = site.Store.Reads;
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, null));
        site.Refresh();
        bool after = await site.IsOnAsync("Checkout.NewFlow");

        Assert.That((readsBefore, site.Store.Reads, after), Is.EqualTo((1, 2, true)));
    }

    [Test]
    public async Task Checking_a_flag_after_a_change_answers_from_memory_while_the_overrides_are_read_again()
    {
        var site = new TestSite(DeclaredOff);
        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.False);

        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, null));
        site.Store.Hold = new TaskCompletionSource();
        OverrideCache cache = site.Provider.GetRequiredService<OverrideCache>();
        cache.Reload();

        Task<bool> check = site.IsOnAsync("Checkout.NewFlow");
        bool answeredWithoutWaiting = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
        bool during = await check;

        site.Store.Hold.SetResult();
        await cache.ReloadAsync();
        bool after = await site.IsOnAsync("Checkout.NewFlow");

        Assert.That((answeredWithoutWaiting, during, after), Is.EqualTo((true, false, true)));
    }

    [Test]
    public async Task A_failed_read_keeps_the_settings_read_last()
    {
        var site = new TestSite("""{ "feature_management": { "feature_flags": [ { "id": "Search", "enabled": true } ] } }""");
        site.Store.With("Search", FlagSettings.Off);
        Assert.That(await site.IsOnAsync("Search"), Is.False);

        site.Store.Fail = true;
        site.Refresh();

        Assert.That(await site.IsOnAsync("Search"), Is.False);
    }

    [Test]
    public async Task Every_declared_flag_is_listed_once_with_its_override()
    {
        var site = new TestSite(DeclaredOff, before: services =>
        {
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("DarkMode", false), FlagDetails.None));
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Checkout.NewFlow", true), FlagDetails.None));
        });
        site.Store.With("DarkMode", FlagSettings.ForAudience(true, null));

        var names = new List<string>();
        await foreach (FeatureDefinition definition in site.Provider.GetRequiredService<IFeatureDefinitionProvider>().GetAllFeatureDefinitionsAsync())
        {
            names.Add(definition.Name);
        }

        Assert.That(names, Is.EquivalentTo(new[] { "Checkout.NewFlow", "DarkMode" }));
        Assert.That(await site.IsOnAsync("DarkMode"), Is.True);
    }
}
