using Microsoft.Extensions.DependencyInjection;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Overrides;

/// <summary>What editors' changes do to flags, through the service the API calls.</summary>
public class OverrideServiceTests
{
    private const string Member = "8C2F4A10-3B6E-4D1A-9F5C-2E7B1D0A6C34";
    private const string Group = "7f3a9c21-4e8b-4d6f-a1c5-0b2e9d4f8a67";
    private const string Flags = """
        { "feature_management": { "feature_flags": [
            { "id": "Checkout.NewFlow", "enabled": false },
            { "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } } ] } }
        """;

    private static readonly Guid Editor = Guid.NewGuid();

    [Test]
    public async Task Saving_switches_the_flag_straight_away_and_records_who_saved_it()
    {
        var site = new TestSite(Flags);
        await site.IsOnAsync("Checkout.NewFlow");

        OverrideResult result = await Service(site).SaveAsync("checkout.newflow", new FlagSettings(true, null), Editor, default);

        Assert.That(result.Outcome, Is.EqualTo(OverrideOutcome.Saved));
        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.True);
        Assert.That(site.Store.All.Single(), Has.Property(nameof(FeatureFlagOverride.FlagName)).EqualTo("Checkout.NewFlow")
            .And.Property(nameof(FeatureFlagOverride.UpdatedBy)).EqualTo(Editor));
    }

    [Test]
    public async Task Saving_writes_keys_the_way_targeting_compares_them()
    {
        var site = new TestSite(Flags);

        await Service(site).SaveAsync("Checkout.NewFlow", new FlagSettings(true, new FlagAudience([Member, Member, " "], [Group])), Editor, default);
        site.Visitor.SignIn(Member.ToLowerInvariant());

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.True);
        Assert.That(FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience!.Members,
            Is.EqualTo(new[] { Member.ToLowerInvariant() }));
    }

    [Test]
    public async Task Switching_off_keeps_the_audience_for_switching_back_on()
    {
        var site = new TestSite(Flags);
        OverrideService service = Service(site);
        await service.SaveAsync("Checkout.NewFlow", new FlagSettings(true, new FlagAudience([Member], [])), Editor, default);

        await service.SetEnabledAsync("Checkout.NewFlow", false, Editor, default);
        site.Visitor.SignIn(Member.ToLowerInvariant());
        bool whileOff = await site.IsOnAsync("Checkout.NewFlow");
        await service.SetEnabledAsync("Checkout.NewFlow", true, Editor, default);

        Assert.That((whileOff, await site.IsOnAsync("Checkout.NewFlow")), Is.EqualTo((false, true)));
    }

    [Test]
    public async Task Resetting_goes_back_to_the_default()
    {
        var site = new TestSite(Flags);
        OverrideService service = Service(site);
        await service.SaveAsync("Checkout.NewFlow", new FlagSettings(true, null), Editor, default);

        await service.ResetAsync("Checkout.NewFlow", default);

        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.False);
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task A_flag_the_backoffice_can_not_edit_is_refused_but_can_still_be_reset()
    {
        var site = new TestSite(Flags);
        site.Store.With("Sale", FlagSettings.Off);
        site.Refresh();
        OverrideService service = Service(site);

        // An override makes the flag editable again, so start from one the backoffice can't read.
        site.Store.WithJson("Sale", """{ "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } }""");
        site.Refresh();
        OverrideResult save = await service.SaveAsync("Sale", FlagSettings.Off, Editor, default);
        OverrideResult reset = await service.ResetAsync("Sale", default);

        Assert.That(save.Outcome, Is.EqualTo(OverrideOutcome.ReadOnly));
        Assert.That(save.Reason, Does.Contain("Microsoft.TimeWindow"));
        Assert.That(reset.Outcome, Is.EqualTo(OverrideOutcome.Saved));
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task Nothing_can_be_changed_when_overrides_are_read_only()
    {
        var site = new TestSite("""
            { "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } },
              "feature_management": { "feature_flags": [ { "id": "Checkout.NewFlow", "enabled": false } ] } }
            """);
        site.Store.With("Checkout.NewFlow", new FlagSettings(true, null));
        OverrideService service = Service(site);

        OverrideResult save = await service.SaveAsync("Checkout.NewFlow", new FlagSettings(true, null), Editor, default);
        OverrideResult reset = await service.ResetAsync("Checkout.NewFlow", default);

        Assert.That((save.Outcome, reset.Outcome), Is.EqualTo((OverrideOutcome.ReadOnly, OverrideOutcome.ReadOnly)));
        Assert.That(site.Store.All, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_flag_nobody_declares_can_not_be_saved_but_its_setting_can_be_removed()
    {
        var site = new TestSite(Flags);
        site.Store.With("Promo.Removed", new FlagSettings(true, null));
        OverrideService service = Service(site);

        OverrideResult save = await service.SaveAsync("Promo.Removed", FlagSettings.Off, Editor, default);
        OverrideResult remove = await service.ResetAsync("Promo.Removed", default);

        Assert.That((save.Outcome, remove.Outcome), Is.EqualTo((OverrideOutcome.NotFound, OverrideOutcome.Saved)));
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task Deleting_the_last_member_of_an_audience_leaves_the_flag_on_for_nobody()
    {
        var site = new TestSite(Flags);
        OverrideService service = Service(site);
        string member = Member.ToLowerInvariant();
        await service.SaveAsync("Checkout.NewFlow", new FlagSettings(true, new FlagAudience([member], [])), Editor, default);

        await service.RemoveFromAudiencesAsync([member], default);
        site.Visitor.SignIn(member);
        bool forDeleted = await site.IsOnAsync("Checkout.NewFlow");
        site.Visitor.SignIn("someone-else");
        bool forOthers = await site.IsOnAsync("Checkout.NewFlow");

        Assert.That((forDeleted, forOthers), Is.EqualTo((false, false)));
        Assert.That(FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience, Is.Not.Null
            .And.Property(nameof(FlagAudience.Members)).Empty);
    }

    [Test]
    public async Task Deleting_a_member_group_takes_it_out_of_audiences()
    {
        var site = new TestSite(Flags);
        OverrideService service = Service(site);
        await service.SaveAsync("Checkout.NewFlow", new FlagSettings(true, new FlagAudience([], [Group, "crm:premium"])), Editor, default);

        await service.RemoveFromAudiencesAsync([Group], default);

        Assert.That(FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience!.Groups,
            Is.EqualTo(new[] { "crm:premium" }));
    }

    private static OverrideService Service(TestSite site) => site.Provider.GetRequiredService<OverrideService>();
}
