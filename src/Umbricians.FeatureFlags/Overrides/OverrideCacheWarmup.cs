using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Reads the overrides as soon as the site has started, in the background, so the first request that checks a flag
/// doesn't have to wait for the store.
/// </summary>
internal sealed class OverrideCacheWarmup(OverrideCache cache) : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification) => cache.Reload();
}
