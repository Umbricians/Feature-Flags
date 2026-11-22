# Notifications

Feature Flags publishes a notification before and after an override changes, through Umbraco's own notification
system. Handle them to refuse a change, to keep an audit trail, to clear a cache or to tell a team channel.

| Notification | When | Cancellable |
| --- | --- | --- |
| `FeatureFlagOverrideSavingNotification` | before an override is saved: an editor's save or switch, or code's | yes |
| `FeatureFlagOverrideSavedNotification` | after it is saved, and after this server has read the overrides again | no |
| `FeatureFlagOverrideResettingNotification` | before an override is reset; removing the override of a flag nobody declares any more counts | yes |
| `FeatureFlagOverrideResetNotification` | after the reset, and after this server has read the overrides again | no |

They live in the `Umbricians.FeatureFlags.Overrides` namespace and are handled like any Umbraco notification, with
`INotificationHandler<T>` or `INotificationAsyncHandler<T>` registered in a composer:

```csharp
builder.AddNotificationHandler<FeatureFlagOverrideSavingNotification, MyHandler>();
```

## What each one carries

| Property | Meaning |
| --- | --- |
| `FlagName` | the flag's name, as its flag source spells it |
| `SettingsBefore` | the override's `FlagSettings` before the change; `null` when the flag had no override, or its saved setting can't be read as settings |
| `JsonBefore` | the override as stored before the change (Microsoft's `feature_management` JSON); `null` when there was none |
| `SettingsAfter`, `JsonAfter` | the same after the change; `null` for a reset, which leaves no override |
| `IsDeclared` | `false` when nobody declares the flag any more, so only its override is left |
| `UserKey` | the key of the backoffice user who made the change; `null` from code that didn't credit a user, and for member clean-up |
| `When` | when the change was made, from the site's `TimeProvider` |
| `Cause` | `Editor` (the backoffice), `Code` (`IFeatureFlagService`) or `MemberCleanup` |
| `Messages` | Umbraco's event messages, for whoever asked for the change |

The Saving and Saved notifications of one change share their `State` dictionary, as Umbraco's own pairs do, so a
handler can pass something from one to the other.

## Cancelling a change

A Saving or Resetting handler cancels the change with `CancelOperation`, which takes an `EventMessage`. Nothing is
written. `IFeatureFlagService` returns a `FeatureFlagResult` with the status `Cancelled` and the messages in
`Messages`, and the backoffice shows them to the editor as the reason the change wasn't made.

```csharp
public void Handle(FeatureFlagOverrideSavingNotification notification)
{
    if (notification.FlagName == "Checkout.NewFlow")
    {
        notification.CancelOperation(new EventMessage("Frozen", "Checkout is frozen until Monday.", EventMessageType.Error));
    }
}
```

Only cancel with a message: an editor who is refused without one is told only that a handler cancelled the change.
The Saved and Reset notifications can't be cancelled; the change has been made.

## Member clean-up

When Umbraco deletes a member or member group, Feature Flags takes it out of every audience. Each flag changed gets a
Saved notification with the cause `MemberCleanup` and no user key. Clean-up can't be cancelled, so there is no Saving
notification for it.

## Servers

Like Umbraco's own, these notifications are published only on the server that made the change. On a load-balanced
site, code that must react on every server (to clear a cache of its own, say) handles
`FeatureFlagsCacheRefresherNotification` (in `Umbricians.FeatureFlags.Overrides`) instead. Umbraco publishes it on every
server, including the one that made the change, after the server has read the overrides again. It says only that
overrides changed, not which flag; ask `IFeatureFlagService` what you need to know.

## Example: only some editors may change a section's flags

This refuses changes to the flags in the `Billing` section (names that start with `Billing.`) unless the editor is in
a chosen user group. A change from code has no user, so it is refused too; let it through by checking `Cause`.

```csharp
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Overrides;

public class BillingFlagsGuard(IUserService users) :
    INotificationAsyncHandler<FeatureFlagOverrideSavingNotification>,
    INotificationAsyncHandler<FeatureFlagOverrideResettingNotification>
{
    private const string Section = "Billing.";
    private const string AllowedGroup = "billingAdmins"; // the user group's alias

    public Task HandleAsync(FeatureFlagOverrideSavingNotification notification, CancellationToken cancellationToken) =>
        CheckAsync(notification.FlagName, notification.Cause, notification.UserKey, notification);

    public Task HandleAsync(FeatureFlagOverrideResettingNotification notification, CancellationToken cancellationToken) =>
        CheckAsync(notification.FlagName, notification.Cause, notification.UserKey, notification);

    private async Task CheckAsync(string flagName, FeatureFlagChangeCause cause, Guid? userKey, CancelableNotification notification)
    {
        if (cause != FeatureFlagChangeCause.Editor || !flagName.StartsWith(Section, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IUser? user = userKey is { } key ? await users.GetAsync(key) : null;
        if (user is null || !user.Groups.Any(group => group.Alias == AllowedGroup))
        {
            notification.CancelOperation(new EventMessage(
                "Not allowed", "Only billing admins can change billing flags.", EventMessageType.Error));
        }
    }
}
```

## Example: write every change to Umbraco's audit log

This handles the Saved and Reset notifications, so only changes that were made are logged, and on the server that made
them. Umbraco's audit log wants a user, so changes without one are logged as the super user.

```csharp
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Overrides;

public class FlagAuditHandler(IAuditService audit) :
    INotificationAsyncHandler<FeatureFlagOverrideSavedNotification>,
    INotificationAsyncHandler<FeatureFlagOverrideResetNotification>
{
    public Task HandleAsync(FeatureFlagOverrideSavedNotification notification, CancellationToken cancellationToken) =>
        WriteAsync("saved", notification.FlagName, notification.UserKey, notification.Cause, notification.JsonBefore, notification.JsonAfter);

    public Task HandleAsync(FeatureFlagOverrideResetNotification notification, CancellationToken cancellationToken) =>
        WriteAsync("reset", notification.FlagName, notification.UserKey, notification.Cause, notification.JsonBefore, notification.JsonAfter);

    private Task WriteAsync(string what, string flagName, Guid? userKey, FeatureFlagChangeCause cause, string? before, string? after) =>
        audit.AddAsync(
            AuditType.Custom,
            userKey ?? Constants.Security.SuperUserKey,
            0,
            "FeatureFlag",
            $"Flag {flagName} {what} ({cause})",
            $"before: {before ?? "none"}; after: {after ?? "none"}");
}
```

Register both in a composer:

```csharp
builder
    .AddNotificationAsyncHandler<FeatureFlagOverrideSavingNotification, BillingFlagsGuard>()
    .AddNotificationAsyncHandler<FeatureFlagOverrideResettingNotification, BillingFlagsGuard>()
    .AddNotificationAsyncHandler<FeatureFlagOverrideSavedNotification, FlagAuditHandler>()
    .AddNotificationAsyncHandler<FeatureFlagOverrideResetNotification, FlagAuditHandler>();
```
