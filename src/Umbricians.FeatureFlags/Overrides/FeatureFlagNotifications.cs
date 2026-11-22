using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>Published before an override is saved. A handler can cancel the save by calling <see cref="CancelableNotification.CancelOperation(EventMessage)"/>, or by adding a message to <see cref="CancelableNotification.Messages"/> and setting <see cref="CancelableNotification.Cancel"/>; nothing is saved, and the messages go back to whoever asked for the change, the editor included. Published for an editor's save or switch, and for code's, on the server that makes the change.</summary>
public sealed class FeatureFlagOverrideSavingNotification : CancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="flagName">The flag's name, as its flag source spells it.</param>
    /// <param name="settingsBefore">The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</param>
    /// <param name="jsonBefore">The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</param>
    /// <param name="settingsAfter">The override's settings after the change; null for a reset, which leaves no override.</param>
    /// <param name="jsonAfter">The override as it is stored after the change; null for a reset.</param>
    /// <param name="isDeclared">False when nobody declares the flag any more, so only its override is left.</param>
    /// <param name="userKey">The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</param>
    /// <param name="when">When the change was made.</param>
    /// <param name="cause">Why the override changed.</param>
    /// <param name="messages">Where handlers add the messages that go back to whoever asked for the change.</param>
    public FeatureFlagOverrideSavingNotification(
        string flagName,
        FlagSettings? settingsBefore,
        string? jsonBefore,
        FlagSettings? settingsAfter,
        string? jsonAfter,
        bool isDeclared,
        Guid? userKey,
        DateTimeOffset when,
        FeatureFlagChangeCause cause,
        EventMessages messages) : base(messages)
    {
        FlagName = flagName;
        SettingsBefore = settingsBefore;
        JsonBefore = jsonBefore;
        SettingsAfter = settingsAfter;
        JsonAfter = jsonAfter;
        IsDeclared = isDeclared;
        UserKey = userKey;
        When = when;
        Cause = cause;
    }

    /// <summary>The flag's name, as its flag source spells it.</summary>
    public string FlagName { get; }

    /// <summary>The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</summary>
    public FlagSettings? SettingsBefore { get; }

    /// <summary>The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</summary>
    public string? JsonBefore { get; }

    /// <summary>The override's settings after the change; null for a reset, which leaves no override.</summary>
    public FlagSettings? SettingsAfter { get; }

    /// <summary>The override as it is stored after the change; null for a reset.</summary>
    public string? JsonAfter { get; }

    /// <summary>False when nobody declares the flag any more, so only its override is left.</summary>
    public bool IsDeclared { get; }

    /// <summary>The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</summary>
    public Guid? UserKey { get; }

    /// <summary>When the change was made.</summary>
    public DateTimeOffset When { get; }

    /// <summary>Why the override changed.</summary>
    public FeatureFlagChangeCause Cause { get; }
}

/// <summary>Published after an override is saved, and after this server has read the overrides again, so a check made by a handler sees the change. It can't be cancelled. Published on the server that made the change; to react on every server, handle <see cref="FeatureFlagsCacheRefresherNotification"/>. Also published, with the cause <see cref="FeatureFlagChangeCause.MemberCleanup"/>, when a deleted member or group is taken out of an audience.</summary>
public sealed class FeatureFlagOverrideSavedNotification : StatefulNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="flagName">The flag's name, as its flag source spells it.</param>
    /// <param name="settingsBefore">The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</param>
    /// <param name="jsonBefore">The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</param>
    /// <param name="settingsAfter">The override's settings after the change; null for a reset, which leaves no override.</param>
    /// <param name="jsonAfter">The override as it is stored after the change; null for a reset.</param>
    /// <param name="isDeclared">False when nobody declares the flag any more, so only its override is left.</param>
    /// <param name="userKey">The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</param>
    /// <param name="when">When the change was made.</param>
    /// <param name="cause">Why the override changed.</param>
    /// <param name="messages">Where handlers add the messages that go back to whoever asked for the change.</param>
    public FeatureFlagOverrideSavedNotification(
        string flagName,
        FlagSettings? settingsBefore,
        string? jsonBefore,
        FlagSettings? settingsAfter,
        string? jsonAfter,
        bool isDeclared,
        Guid? userKey,
        DateTimeOffset when,
        FeatureFlagChangeCause cause,
        EventMessages messages)
    {
        FlagName = flagName;
        SettingsBefore = settingsBefore;
        JsonBefore = jsonBefore;
        SettingsAfter = settingsAfter;
        JsonAfter = jsonAfter;
        IsDeclared = isDeclared;
        UserKey = userKey;
        When = when;
        Cause = cause;
        Messages = messages;
    }

    /// <summary>The flag's name, as its flag source spells it.</summary>
    public string FlagName { get; }

    /// <summary>The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</summary>
    public FlagSettings? SettingsBefore { get; }

    /// <summary>The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</summary>
    public string? JsonBefore { get; }

    /// <summary>The override's settings after the change; null for a reset, which leaves no override.</summary>
    public FlagSettings? SettingsAfter { get; }

    /// <summary>The override as it is stored after the change; null for a reset.</summary>
    public string? JsonAfter { get; }

    /// <summary>False when nobody declares the flag any more, so only its override is left.</summary>
    public bool IsDeclared { get; }

    /// <summary>The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</summary>
    public Guid? UserKey { get; }

    /// <summary>When the change was made.</summary>
    public DateTimeOffset When { get; }

    /// <summary>Why the override changed.</summary>
    public FeatureFlagChangeCause Cause { get; }

    /// <summary>Messages for whoever asked for the change.</summary>
    public EventMessages Messages { get; }
}

/// <summary>Published before an override is reset, so the flag goes back to its default; removing the override of a flag nobody declares counts as a reset. A handler can cancel it like <see cref="FeatureFlagOverrideSavingNotification"/>: nothing is reset, and the messages go back to whoever asked.</summary>
public sealed class FeatureFlagOverrideResettingNotification : CancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="flagName">The flag's name, as its flag source spells it.</param>
    /// <param name="settingsBefore">The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</param>
    /// <param name="jsonBefore">The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</param>
    /// <param name="settingsAfter">The override's settings after the change; null for a reset, which leaves no override.</param>
    /// <param name="jsonAfter">The override as it is stored after the change; null for a reset.</param>
    /// <param name="isDeclared">False when nobody declares the flag any more, so only its override is left.</param>
    /// <param name="userKey">The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</param>
    /// <param name="when">When the change was made.</param>
    /// <param name="cause">Why the override changed.</param>
    /// <param name="messages">Where handlers add the messages that go back to whoever asked for the change.</param>
    public FeatureFlagOverrideResettingNotification(
        string flagName,
        FlagSettings? settingsBefore,
        string? jsonBefore,
        FlagSettings? settingsAfter,
        string? jsonAfter,
        bool isDeclared,
        Guid? userKey,
        DateTimeOffset when,
        FeatureFlagChangeCause cause,
        EventMessages messages) : base(messages)
    {
        FlagName = flagName;
        SettingsBefore = settingsBefore;
        JsonBefore = jsonBefore;
        SettingsAfter = settingsAfter;
        JsonAfter = jsonAfter;
        IsDeclared = isDeclared;
        UserKey = userKey;
        When = when;
        Cause = cause;
    }

    /// <summary>The flag's name, as its flag source spells it.</summary>
    public string FlagName { get; }

    /// <summary>The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</summary>
    public FlagSettings? SettingsBefore { get; }

    /// <summary>The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</summary>
    public string? JsonBefore { get; }

    /// <summary>The override's settings after the change; null for a reset, which leaves no override.</summary>
    public FlagSettings? SettingsAfter { get; }

    /// <summary>The override as it is stored after the change; null for a reset.</summary>
    public string? JsonAfter { get; }

    /// <summary>False when nobody declares the flag any more, so only its override is left.</summary>
    public bool IsDeclared { get; }

    /// <summary>The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</summary>
    public Guid? UserKey { get; }

    /// <summary>When the change was made.</summary>
    public DateTimeOffset When { get; }

    /// <summary>Why the override changed.</summary>
    public FeatureFlagChangeCause Cause { get; }
}

/// <summary>Published after an override is reset, and after this server has read the overrides again. It can't be cancelled. Published on the server that made the change; to react on every server, handle <see cref="FeatureFlagsCacheRefresherNotification"/>.</summary>
public sealed class FeatureFlagOverrideResetNotification : StatefulNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="flagName">The flag's name, as its flag source spells it.</param>
    /// <param name="settingsBefore">The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</param>
    /// <param name="jsonBefore">The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</param>
    /// <param name="settingsAfter">The override's settings after the change; null for a reset, which leaves no override.</param>
    /// <param name="jsonAfter">The override as it is stored after the change; null for a reset.</param>
    /// <param name="isDeclared">False when nobody declares the flag any more, so only its override is left.</param>
    /// <param name="userKey">The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</param>
    /// <param name="when">When the change was made.</param>
    /// <param name="cause">Why the override changed.</param>
    /// <param name="messages">Where handlers add the messages that go back to whoever asked for the change.</param>
    public FeatureFlagOverrideResetNotification(
        string flagName,
        FlagSettings? settingsBefore,
        string? jsonBefore,
        FlagSettings? settingsAfter,
        string? jsonAfter,
        bool isDeclared,
        Guid? userKey,
        DateTimeOffset when,
        FeatureFlagChangeCause cause,
        EventMessages messages)
    {
        FlagName = flagName;
        SettingsBefore = settingsBefore;
        JsonBefore = jsonBefore;
        SettingsAfter = settingsAfter;
        JsonAfter = jsonAfter;
        IsDeclared = isDeclared;
        UserKey = userKey;
        When = when;
        Cause = cause;
        Messages = messages;
    }

    /// <summary>The flag's name, as its flag source spells it.</summary>
    public string FlagName { get; }

    /// <summary>The override's settings before the change; null when the flag had no override, or its saved setting can't be read as settings.</summary>
    public FlagSettings? SettingsBefore { get; }

    /// <summary>The override as it was stored before the change, in Microsoft's <c>feature_management</c> JSON; null when the flag had no override.</summary>
    public string? JsonBefore { get; }

    /// <summary>The override's settings after the change; null for a reset, which leaves no override.</summary>
    public FlagSettings? SettingsAfter { get; }

    /// <summary>The override as it is stored after the change; null for a reset.</summary>
    public string? JsonAfter { get; }

    /// <summary>False when nobody declares the flag any more, so only its override is left.</summary>
    public bool IsDeclared { get; }

    /// <summary>The key of the backoffice user who made the change; null when code made it without crediting a user, and for member clean-up.</summary>
    public Guid? UserKey { get; }

    /// <summary>When the change was made.</summary>
    public DateTimeOffset When { get; }

    /// <summary>Why the override changed.</summary>
    public FeatureFlagChangeCause Cause { get; }

    /// <summary>Messages for whoever asked for the change.</summary>
    public EventMessages Messages { get; }
}
