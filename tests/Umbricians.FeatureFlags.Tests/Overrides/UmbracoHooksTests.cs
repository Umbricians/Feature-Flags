using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Sync;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Tests.Overrides;

/// <summary>What the package does when Umbraco tells it a member or group was deleted, the site started, or another server saved.</summary>
public class UmbracoHooksTests
{
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();

    [Test]
    public async Task A_deleted_member_is_removed_from_every_audience()
    {
        var site = new TestSite();
        site.Store.With("A", FlagSettings.ForAudience(true, new FlagAudience([TargetingIds.For(Member), "other"], [])))
            .With("B", FlagSettings.ForAudience(true, new FlagAudience([TargetingIds.For(Member)], [])));
        var handler = new MemberDeletedHandler(site.Provider.GetRequiredService<OverrideService>());

        await handler.HandleAsync(new MemberDeletedNotification(Entity<IMember>(Member), new EventMessages()), default);

        Assert.That(Audience(site, "A").Members, Is.EqualTo(new[] { "other" }));
        Assert.That(Audience(site, "B").Members, Is.Empty);
    }

    [Test]
    public async Task A_deleted_member_group_is_removed_from_every_audience()
    {
        var site = new TestSite();
        site.Store.With("A", FlagSettings.ForAudience(true, new FlagAudience([], [TargetingIds.For(Group), "crm:premium"])));
        var handler = new MemberGroupDeletedHandler(site.Provider.GetRequiredService<OverrideService>());

        await handler.HandleAsync(new MemberGroupDeletedNotification(Entity<IMemberGroup>(Group), new EventMessages()), default);

        Assert.That(Audience(site, "A").Groups, Is.EqualTo(new[] { "crm:premium" }));
    }

    [Test]
    public void Starting_the_site_reads_the_overrides_in_the_background()
    {
        var site = new TestSite();
        site.Store.With("Checkout.NewFlow", FlagSettings.ForAudience(true, null));
        var warmup = new OverrideCacheWarmup(site.Provider.GetRequiredService<OverrideCache>());

        warmup.Handle(new UmbracoApplicationStartedNotification(false));

        Assert.That(SpinWait.SpinUntil(() => site.Store.Reads > 0, TimeSpan.FromSeconds(5)), Is.True);
    }

    [TestCase("all")]
    [TestCase("int")]
    [TestCase("guid")]
    public void Another_server_saving_makes_this_one_read_the_overrides_again_and_say_so(string how)
    {
        var site = new TestSite();
        var published = new List<object>();
        var events = Stub.Of<IEventAggregator>((name, args) =>
        {
            if (name is nameof(IEventAggregator.Publish) or nameof(IEventAggregator.PublishAsync))
            {
                published.Add(args[0]!);
                return name == nameof(IEventAggregator.PublishAsync) ? Task.CompletedTask : null;
            }

            return null;
        });
        var factory = Stub.Of<ICacheRefresherNotificationFactory>((name, args) =>
            name == nameof(ICacheRefresherNotificationFactory.Create)
                ? new FeatureFlagsCacheRefresherNotification(args[0]!, (MessageType)args[1]!)
                : null);
        var refresher = new FeatureFlagsCacheRefresher(AppCaches.NoCache, events, factory, site.Provider.GetRequiredService<OverrideCache>());

        switch (how)
        {
            case "all": refresher.RefreshAll(); break;
            case "int": refresher.Refresh(1); refresher.Remove(1); break;
            default: refresher.Refresh(Guid.NewGuid()); break;
        }

        Assert.That(SpinWait.SpinUntil(() => site.Store.Reads > 0, TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(published, Is.Not.Empty);
        Assert.That(refresher.RefresherUniqueId, Is.EqualTo(FeatureFlagsCacheRefresher.UniqueId));
        Assert.That(refresher.Name, Is.EqualTo("Umbricians Feature Flags"));
    }

    private static T Entity<T>(Guid key) where T : class =>
        Stub.Of<T>((name, _) => name == "get_Key" ? key : null);

    private static FlagAudience Audience(TestSite site, string flag) =>
        FlagDefinitions.ReadOverride(site.Store.All.Single(saved => saved.FlagName == flag).Definition, out _)!.Audience!;
}
