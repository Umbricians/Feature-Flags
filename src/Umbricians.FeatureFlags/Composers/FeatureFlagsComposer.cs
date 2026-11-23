using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Flags;
#if UMBRACO_18_OR_GREATER
using Umbricians.FeatureFlags.OpenApi;
#endif
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
#if UMBRACO_18_OR_GREATER
        builder.AddFeatureFlagsOpenApi();
#endif

        builder.CacheRefreshers().Add<FeatureFlagsCacheRefresher>();
        builder.AddNotificationAsyncHandler<MemberDeletedNotification, MemberDeletedHandler>();
        builder.AddNotificationAsyncHandler<MemberGroupDeletedNotification, MemberGroupDeletedHandler>();
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, OverrideCacheWarmup>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, StartupChecks>();
    }
}
