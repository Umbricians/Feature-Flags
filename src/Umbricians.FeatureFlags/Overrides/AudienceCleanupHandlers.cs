using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>Takes deleted members out of every flag's audience.</summary>
internal sealed class MemberDeletedHandler(OverrideService overrides) : INotificationAsyncHandler<MemberDeletedNotification>
{
    public Task HandleAsync(MemberDeletedNotification notification, CancellationToken cancellationToken) =>
        overrides.RemoveFromAudiencesAsync(
            notification.DeletedEntities.Select(member => TargetingIds.For(member.Key)).ToArray(), cancellationToken);
}

/// <summary>Takes deleted member groups out of every flag's audience.</summary>
internal sealed class MemberGroupDeletedHandler(OverrideService overrides) : INotificationAsyncHandler<MemberGroupDeletedNotification>
{
    public Task HandleAsync(MemberGroupDeletedNotification notification, CancellationToken cancellationToken) =>
        overrides.RemoveFromAudiencesAsync(
            notification.DeletedEntities.Select(group => TargetingIds.For(group.Key)).ToArray(), cancellationToken);
}
