using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Composers;

/// <summary>
/// Registers Feature Flags with Umbraco.
/// </summary>
public class FeatureFlagsComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddFeatureFlags(builder.Config);

        builder.CacheRefreshers().Add<FeatureFlagsCacheRefresher>();
        builder.AddNotificationAsyncHandler<MemberDeletedNotification, MemberDeletedHandler>();
        builder.AddNotificationAsyncHandler<MemberGroupDeletedNotification, MemberGroupDeletedHandler>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, StartupChecks>();
    }
}
