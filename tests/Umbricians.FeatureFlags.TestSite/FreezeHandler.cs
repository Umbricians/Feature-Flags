using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.TestSite;

/// <summary>Refuses every save or reset of the flags in the Frozen section, like a site that locks some flags down.</summary>
public sealed class FreezeHandler :
    INotificationHandler<FeatureFlagOverrideSavingNotification>,
    INotificationHandler<FeatureFlagOverrideResettingNotification>
{
    public void Handle(FeatureFlagOverrideSavingNotification notification) =>
        Refuse(notification.FlagName, notification);

    public void Handle(FeatureFlagOverrideResettingNotification notification) =>
        Refuse(notification.FlagName, notification);

    private static void Refuse(string flagName, CancelableNotification notification)
    {
        if (flagName.StartsWith("Frozen.", StringComparison.OrdinalIgnoreCase))
        {
            notification.CancelOperation(new EventMessage("Frozen", $"{flagName} is frozen.", EventMessageType.Error));
        }
    }
}

/// <summary>
/// When Mirror.Source is saved, switches Mirror.Target the same way: a change made from inside a Saved handler, as a site
/// that keeps two flags together would.
/// </summary>
public sealed class MirrorHandler(IFeatureFlagService flags) : INotificationHandler<FeatureFlagOverrideSavedNotification>
{
    public void Handle(FeatureFlagOverrideSavedNotification notification)
    {
        if (string.Equals(notification.FlagName, "Mirror.Source", StringComparison.OrdinalIgnoreCase))
        {
            flags.SwitchAsync("Mirror.Target", notification.SettingsAfter!.Enabled).GetAwaiter().GetResult();
        }
    }
}

/// <summary>Registers the test site's handlers.</summary>
public sealed class FreezeComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder
        .AddNotificationHandler<FeatureFlagOverrideSavingNotification, FreezeHandler>()
        .AddNotificationHandler<FeatureFlagOverrideResettingNotification, FreezeHandler>()
        .AddNotificationHandler<FeatureFlagOverrideSavedNotification, MirrorHandler>();
}
