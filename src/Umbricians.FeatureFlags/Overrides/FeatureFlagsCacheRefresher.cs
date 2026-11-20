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
        _cache.Invalidate();
        base.RefreshAll();
    }

    public override void Refresh(int id) => RefreshAll();

    public override void Refresh(Guid id) => RefreshAll();

    public override void Remove(int id) => RefreshAll();
}

/// <summary>Published on each server after it reads the overrides again.</summary>
internal sealed class FeatureFlagsCacheRefresherNotification(object messageObject, MessageType messageType)
    : CacheRefresherNotification(messageObject, messageType);
