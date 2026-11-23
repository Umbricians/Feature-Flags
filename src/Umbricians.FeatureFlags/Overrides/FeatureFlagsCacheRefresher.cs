using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Sync;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Tells every server to read the overrides again when one changes, through Umbraco's own server messaging, so
/// load-balanced sites agree on every flag.
/// </summary>
internal sealed class FeatureFlagsCacheRefresher : CacheRefresherBase<FeatureFlagsCacheRefresherNotification>
{
    public static readonly Guid UniqueId = new("5e0c9b7a-2d41-4f83-9c6e-7a1b3d5f8e24");

    private readonly OverrideCache _cache;

    public FeatureFlagsCacheRefresher(
        AppCaches appCaches,
        IEventAggregator eventAggregator,
        ICacheRefresherNotificationFactory factory,
        OverrideCache cache)
        : base(appCaches, eventAggregator, factory) => _cache = cache;

    public override Guid RefresherUniqueId => UniqueId;

    public override string Name => "Umbricians Feature Flags";

    public override void RefreshAll()
    {
        // Wait for the read, so a handler of the notification sees the new overrides. It runs on its own, outside this thread's
        // context, so waiting here can't deadlock.
        _cache.ReloadAsync().GetAwaiter().GetResult();
        base.RefreshAll();
    }

    public override void Refresh(int id) => RefreshAll();

    public override void Refresh(Guid id) => RefreshAll();

    public override void Remove(int id) => RefreshAll();
}

/// <summary>
/// Published on every server when any server changes an override, including the one that made the change, after that
/// server has read the overrides again. Unlike the Saved and Reset notifications, which are published only on the server
/// that made the change, handle this one to react on every server of a load-balanced site. It says only that overrides
/// changed, not which; read them with <see cref="IFeatureFlagService"/>.
/// </summary>
/// <param name="messageObject">What Umbraco's server messaging sent; not used by Feature Flags.</param>
/// <param name="messageType">How Umbraco's server messaging says the cache changed.</param>
public sealed class FeatureFlagsCacheRefresherNotification(object messageObject, MessageType messageType)
    : CacheRefresherNotification(messageObject, messageType);
