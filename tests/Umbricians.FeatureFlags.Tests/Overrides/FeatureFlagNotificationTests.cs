using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Overrides;

/// <summary>What the service publishes when an override changes, and what a handler can do about it.</summary>
public class FeatureFlagNotificationTests
{
    private const string Flags = """
        { "feature_management": { "feature_flags": [ { "id": "Checkout.NewFlow", "enabled": false } ] } }
        """;

    private static readonly Guid Editor = Guid.NewGuid();

    [Test]
    public async Task A_save_reads_the_store_then_publishes_Saving_then_stores_then_reads_again_then_publishes_Saved()
    {
        var site = new TestSite(Flags);
        await site.IsOnAsync("Checkout.NewFlow");
        var log = new List<string>();
        site.Store.Log = log;
        site.Events.Log = log;

        await Service(site).SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null), Editor, default);

        Assert.That(log, Is.EqualTo(new[]
        {
            "read", nameof(FeatureFlagOverrideSavingNotification), "store", "read", nameof(FeatureFlagOverrideSavedNotification),
        }));
    }

    [Test]
    public async Task A_reset_reads_the_store_then_publishes_Resetting_then_stores_then_reads_again_then_publishes_Reset()
    {
        var site = new TestSite(Flags);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, null));
        await site.IsOnAsync("Checkout.NewFlow");
        var log = new List<string>();
        site.Store.Log = log;
        site.Events.Log = log;

        await Service(site).ResetAsync("Checkout.NewFlow", Editor);

        Assert.That(log, Is.EqualTo(new[]
        {
            "read", nameof(FeatureFlagOverrideResettingNotification), "store", "read", nameof(FeatureFlagOverrideResetNotification),
        }));
    }

    [Test]
    public async Task A_handler_that_has_the_Saved_notification_sees_the_change_in_a_check()
    {
        var site = new TestSite(Flags);
        bool? seen = null;
        site.Events.On<FeatureFlagOverrideSavedNotification>(_ => seen = site.IsOnAsync("Checkout.NewFlow").GetAwaiter().GetResult());

        await Service(site).SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null));

        Assert.That(seen, Is.True);
    }

    [Test]
    public async Task Cancelling_a_save_writes_nothing_and_returns_the_handlers_messages()
    {
        var site = new TestSite(Flags);
        site.Events.On<FeatureFlagOverrideSavingNotification>(saving =>
            saving.CancelOperation(new EventMessage("Frozen", "Checkout is frozen until Monday.", EventMessageType.Error)));

        FeatureFlagResult result = await Service(site).SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null), Editor, default);

        Assert.That(result.Status, Is.EqualTo(FeatureFlagResultStatus.Cancelled));
        Assert.That(result.Messages, Is.EqualTo(new[] { "Checkout is frozen until Monday." }));
        Assert.That(site.Store.All, Is.Empty);
        Assert.That(await site.IsOnAsync("Checkout.NewFlow"), Is.False);
        Assert.That(site.Events.Of<FeatureFlagOverrideSavedNotification>(), Is.Empty);
    }

    [Test]
    public async Task Cancelling_a_switch_writes_nothing()
    {
        var site = new TestSite(Flags);
        site.Events.On<FeatureFlagOverrideSavingNotification>(saving => saving.Cancel = true);

        FeatureFlagResult result = await Service(site).SwitchAsync("Checkout.NewFlow", true, FeatureFlagChangeCause.Editor, Editor, default);

        Assert.That(result.Status, Is.EqualTo(FeatureFlagResultStatus.Cancelled));
        Assert.That(result.Messages, Is.Empty);
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task Cancelling_a_reset_keeps_the_override_and_returns_the_messages()
    {
        var site = new TestSite(Flags);
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, null));
        site.Events.On<FeatureFlagOverrideResettingNotification>(resetting =>
            resetting.CancelOperation(new EventMessage("Kept", "Ask Sam first.", EventMessageType.Warning)));

        FeatureFlagResult result = await Service(site).ResetAsync("Checkout.NewFlow", FeatureFlagChangeCause.Editor, Editor, default);

        Assert.That(result.Status, Is.EqualTo(FeatureFlagResultStatus.Cancelled));
        Assert.That(result.Messages, Is.EqualTo(new[] { "Ask Sam first." }));
        Assert.That(site.Store.All, Has.Count.EqualTo(1));
        Assert.That(site.Events.Of<FeatureFlagOverrideResetNotification>(), Is.Empty);
    }

    [Test]
    public async Task A_change_the_rules_refuse_publishes_nothing()
    {
        var site = new TestSite(Flags);

        await Service(site).SaveAsync("Nobody", FlagSettings.Off);
        await Service(site).ResetAsync("Checkout.NewFlow");

        Assert.That(site.Events.Published, Is.Empty);
    }

    [Test]
    public async Task Changes_through_the_interface_have_the_cause_Code()
    {
        var site = new TestSite(Flags);
        IFeatureFlagService service = site.Provider.GetRequiredService<IFeatureFlagService>();

        await service.SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null), Editor);
        await service.SwitchAsync("Checkout.NewFlow", false);
        await service.ResetAsync("Checkout.NewFlow");

        Assert.That(site.Events.Published.Select(Cause), Is.All.EqualTo(FeatureFlagChangeCause.Code));
        Assert.That(site.Events.Published, Has.Count.EqualTo(6));
    }

    [Test]
    public async Task Changes_through_the_internal_path_have_the_cause_they_are_given()
    {
        var site = new TestSite(Flags);

        await Service(site).SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null), FeatureFlagChangeCause.Editor, Editor, default);
        await Service(site).ResetAsync("Checkout.NewFlow", FeatureFlagChangeCause.Editor, Editor, default);

        Assert.That(site.Events.Published.Select(Cause), Is.All.EqualTo(FeatureFlagChangeCause.Editor));
        Assert.That(site.Events.Published.Select(UserKey), Is.All.EqualTo(Editor));
    }

    [Test]
    public async Task A_first_save_has_no_settings_before_and_a_reset_has_none_after()
    {
        var site = new TestSite(Flags);
        FeatureFlagService service = Service(site);
        FlagSettings on = FlagSettings.ForAudience(true, null);

        await service.SaveAsync("checkout.newflow", on, Editor, default);
        await service.ResetAsync("Checkout.NewFlow", Editor);

        FeatureFlagOverrideSavedNotification saved = site.Events.Of<FeatureFlagOverrideSavedNotification>().Single();
        Assert.That(saved.FlagName, Is.EqualTo("Checkout.NewFlow"));
        Assert.That(saved.SettingsBefore, Is.Null);
        Assert.That(saved.JsonBefore, Is.Null);
        Assert.That(saved.SettingsAfter!.Enabled, Is.True);
        Assert.That(saved.JsonAfter, Is.EqualTo(FlagDefinitions.ToJson("Checkout.NewFlow", on)));
        Assert.That(saved.IsDeclared, Is.True);

        FeatureFlagOverrideResetNotification reset = site.Events.Of<FeatureFlagOverrideResetNotification>().Single();
        Assert.That(reset.SettingsBefore!.Enabled, Is.True);
        Assert.That(reset.JsonBefore, Is.EqualTo(saved.JsonAfter));
        Assert.That(reset.SettingsAfter, Is.Null);
        Assert.That(reset.JsonAfter, Is.Null);
    }

    [Test]
    public async Task A_second_save_has_the_first_as_its_settings_before()
    {
        var site = new TestSite(Flags);
        FeatureFlagService service = Service(site);
        var audience = new FlagAudience(["8c2f4a10-3b6e-4d1a-9f5c-2e7b1d0a6c34"], []);
        await service.SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, audience), Editor, default);

        await service.SwitchAsync("Checkout.NewFlow", false, FeatureFlagChangeCause.Editor, Editor, default);

        FeatureFlagOverrideSavingNotification saving = site.Events.Of<FeatureFlagOverrideSavingNotification>().Last();
        Assert.That(saving.SettingsBefore!.Enabled, Is.True);
        Assert.That(saving.SettingsBefore.Audience!.Members, Is.EqualTo(audience.Members));
        Assert.That(saving.SettingsAfter!.Enabled, Is.False);
        Assert.That(saving.SettingsAfter.Audience!.Members, Is.EqualTo(audience.Members));
    }

    [Test]
    public async Task The_settings_before_are_what_the_store_holds_even_when_the_cache_is_a_change_behind()
    {
        var site = new TestSite(Flags);
        FeatureFlagService service = Service(site);
        await service.SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null), Editor, default);

        // Another change reached the store, as a first save in the same open scope does, without the cache having read it.
        site.Store.With("Checkout.NewFlow", FlagSettings.Off);
        await service.SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null), Editor, default);

        Assert.That(site.Events.Of<FeatureFlagOverrideSavingNotification>().Last().SettingsBefore!.Enabled, Is.False);
    }

    [Test]
    public async Task A_setting_that_cannot_be_read_has_no_settings_before_but_keeps_its_stored_json()
    {
        var site = new TestSite(Flags);
        const string stored = """{ "id": "Checkout.NewFlow", "enabled": true, "variants": [ { "name": "A" } ] }""";
        site.Store.WithJson("Checkout.NewFlow", stored);
        site.Refresh();

        await Service(site).ResetAsync("Checkout.NewFlow", Editor);

        FeatureFlagOverrideResettingNotification resetting = site.Events.Of<FeatureFlagOverrideResettingNotification>().Single();
        Assert.That(resetting.SettingsBefore, Is.Null);
        Assert.That(resetting.JsonBefore, Is.EqualTo(stored));
    }

    [Test]
    public async Task Removing_an_undeclared_flags_override_is_a_reset_of_a_flag_that_is_not_declared()
    {
        var site = new TestSite(Flags);
        site.Store.With("Promo.Removed", FlagSettings.ForAudience(true, null));
        site.Refresh();

        await Service(site).ResetAsync("Promo.Removed", FeatureFlagChangeCause.Editor, Editor, default);

        Assert.That(site.Events.Of<FeatureFlagOverrideResettingNotification>().Single().IsDeclared, Is.False);
        Assert.That(site.Events.Of<FeatureFlagOverrideResetNotification>().Single(), Has.Property(nameof(FeatureFlagOverrideResetNotification.FlagName)).EqualTo("Promo.Removed"));
    }

    [Test]
    public async Task The_notifications_say_when_from_the_sites_clock_and_share_state_between_Saving_and_Saved()
    {
        var clock = new FixedTime(new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.Zero));
        var site = new TestSite(Flags, after: services => services.AddSingleton<TimeProvider>(clock));
        site.Events.On<FeatureFlagOverrideSavingNotification>(saving => saving.State["seen"] = "yes");

        await Service(site).SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null));

        Assert.That(site.Events.Of<FeatureFlagOverrideSavingNotification>().Single().When, Is.EqualTo(clock.GetUtcNow()));
        FeatureFlagOverrideSavedNotification saved = site.Events.Of<FeatureFlagOverrideSavedNotification>().Single();
        Assert.That(saved.When, Is.EqualTo(clock.GetUtcNow()));
        Assert.That(saved.State["seen"], Is.EqualTo("yes"));
        Assert.That(site.Store.All.Single().UpdatedUtc, Is.EqualTo(clock.GetUtcNow().UtcDateTime));
    }

    [Test]
    public async Task Taking_a_deleted_member_out_of_audiences_publishes_only_Saved_with_the_cause_MemberCleanup()
    {
        var site = new TestSite(Flags);
        FeatureFlagService service = Service(site);
        string leaving = "8c2f4a10-3b6e-4d1a-9f5c-2e7b1d0a6c34";
        string staying = "7f3a9c21-4e8b-4d6f-a1c5-0b2e9d4f8a67";
        await service.SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, new FlagAudience([leaving, staying], [])), Editor, default);
        site.Events.Published.Clear();

        await service.RemoveFromAudiencesAsync([leaving], default);

        INotification published = site.Events.Published.Single();
        var saved = (FeatureFlagOverrideSavedNotification)published;
        Assert.That(saved.Cause, Is.EqualTo(FeatureFlagChangeCause.MemberCleanup));
        Assert.That(saved.UserKey, Is.Null);
        Assert.That(saved.IsDeclared, Is.True);
        Assert.That(saved.SettingsBefore!.Audience!.Members, Is.EqualTo(new[] { leaving, staying }));
        Assert.That(saved.SettingsAfter!.Audience!.Members, Is.EqualTo(new[] { staying }));
        Assert.That(saved.JsonAfter, Is.EqualTo(site.Store.All.Single().Definition));
    }

    [Test]
    public async Task Clean_up_that_changes_nothing_publishes_nothing()
    {
        var site = new TestSite(Flags);
        await Service(site).SaveAsync("Checkout.NewFlow", FlagSettings.ForAudience(true, null), Editor, default);
        site.Events.Published.Clear();

        await Service(site).RemoveFromAudiencesAsync(["someone"], default);

        Assert.That(site.Events.Published, Is.Empty);
    }

    private static FeatureFlagService Service(TestSite site) => site.Provider.GetRequiredService<FeatureFlagService>();

    private static FeatureFlagChangeCause Cause(INotification notification) => notification switch
    {
        FeatureFlagOverrideSavingNotification n => n.Cause,
        FeatureFlagOverrideSavedNotification n => n.Cause,
        FeatureFlagOverrideResettingNotification n => n.Cause,
        FeatureFlagOverrideResetNotification n => n.Cause,
        _ => throw new InvalidOperationException(notification.GetType().Name),
    };

    private static Guid? UserKey(INotification notification) => notification switch
    {
        FeatureFlagOverrideSavingNotification n => n.UserKey,
        FeatureFlagOverrideSavedNotification n => n.UserKey,
        FeatureFlagOverrideResettingNotification n => n.UserKey,
        FeatureFlagOverrideResetNotification n => n.UserKey,
        _ => throw new InvalidOperationException(notification.GetType().Name),
    };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
