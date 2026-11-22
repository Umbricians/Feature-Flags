using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Reads flags and saves and removes their overrides, publishes the notifications about it, and tells every server to
/// read them again. Calls through
/// <see cref="IFeatureFlagService"/> have the cause <see cref="FeatureFlagChangeCause.Code"/>; the Management API calls
/// the methods that take a cause.
/// </summary>
internal sealed class FeatureFlagService : IFeatureFlagService
{
    private readonly IFeatureFlagOverrideStore _store;
    private readonly FlagCatalog _catalog;
    private readonly OverrideCache _cache;
    private readonly DistributedCache? _distributedCache;
    private readonly TimeProvider _time;
    private readonly AudienceGroups _groups;
    private readonly IEventAggregator _events;
    private readonly ICoreScopeProvider? _scopes;
    private readonly ConditionRegistry _conditions;

    public FeatureFlagService(
        IFeatureFlagOverrideStore store,
        FlagCatalog catalog,
        OverrideCache cache,
        TimeProvider time,
        AudienceGroups groups,
        IEventAggregator events,
        ConditionRegistry conditions,
        DistributedCache? distributedCache = null,
        ICoreScopeProvider? scopes = null)
    {
        _store = store;
        _catalog = catalog;
        _cache = cache;
        _time = time;
        _groups = groups;
        _events = events;
        _conditions = conditions;
        _distributedCache = distributedCache;
        _scopes = scopes;
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _catalog.GetAllAsync(cancellationToken);

    public Task<FeatureFlag?> GetAsync(string name, CancellationToken cancellationToken = default) =>
        _catalog.FindAsync(name, cancellationToken);

    public Task<FeatureFlagResult> SaveAsync(string name, FlagSettings settings, Guid? userKey = null, CancellationToken cancellationToken = default) =>
        SaveAsync(name, settings, FeatureFlagChangeCause.Code, userKey, cancellationToken);

    public Task<FeatureFlagResult> SwitchAsync(string name, bool on, Guid? userKey = null, CancellationToken cancellationToken = default) =>
        SwitchAsync(name, on, FeatureFlagChangeCause.Code, userKey, cancellationToken);

    public Task<FeatureFlagResult> ResetAsync(string name, Guid? userKey = null, CancellationToken cancellationToken = default) =>
        ResetAsync(name, FeatureFlagChangeCause.Code, userKey, cancellationToken);

    /// <summary>Saves settings for a flag.</summary>
    public async Task<FeatureFlagResult> SaveAsync(string flagName, FlagSettings settings, FeatureFlagChangeCause cause, Guid? userKey, CancellationToken cancellationToken)
    {
        FeatureFlag? flag = await _catalog.FindAsync(flagName, cancellationToken);
        if (flag is null || !flag.IsDeclared)
        {
            return FeatureFlagResult.NotFound;
        }

        return await SaveFlagAsync(flag, settings, cause, userKey, cancellationToken);
    }

    /// <summary>Switches a flag on or off, keeping its conditions.</summary>
    public async Task<FeatureFlagResult> SwitchAsync(string flagName, bool enabled, FeatureFlagChangeCause cause, Guid? userKey, CancellationToken cancellationToken)
    {
        FeatureFlag? flag = await _catalog.FindAsync(flagName, cancellationToken);
        if (flag is null || !flag.IsDeclared)
        {
            return FeatureFlagResult.NotFound;
        }

        if (!flag.Actions.Switch.Allowed)
        {
            return FeatureFlagResult.NotAllowed(flag.Actions.Switch.Reason);
        }

        return await SaveFlagAsync(flag, flag.Settings! with { Enabled = enabled }, cause, userKey, cancellationToken, validate: false);
    }

    /// <summary>Saves settings for a flag that has been found and is declared, if the rules allow it and the conditions are valid.</summary>
    private async Task<FeatureFlagResult> SaveFlagAsync(FeatureFlag flag, FlagSettings settings, FeatureFlagChangeCause cause, Guid? userKey, CancellationToken cancellationToken, bool validate = true)
    {
        if (!flag.Actions.Save.Allowed)
        {
            return FeatureFlagResult.NotAllowed(flag.Actions.Save.Reason);
        }

        // Switching keeps conditions as they are, so it doesn't re-check them: a stale parameter mustn't stop an editor switching a flag off.
        IReadOnlyList<string> problems = validate ? _conditions.Validate(settings) : [];
        if (problems.Count > 0)
        {
            return FeatureFlagResult.NotAllowed(string.Join(" ", problems), problems);
        }

        FlagSettings normalized = settings.Audience is null
            ? settings
            : FlagSettings.ForAudience(settings.Enabled, (await _groups.ReadAsync(cancellationToken)).Normalize(settings.Audience));
        string json = FlagDefinitions.ToJson(flag.Name, normalized);
        SavedOverride? before = await SavedAsync(flag.Name, cancellationToken);
        DateTimeOffset when = _time.GetUtcNow();

        var messages = new EventMessages();
        var saving = new FeatureFlagOverrideSavingNotification(
            flag.Name, Copy(before?.Settings), before?.Override.Definition, Copy(normalized), json, flag.IsDeclared, userKey, when, cause, messages);
        if (await _events.PublishCancelableAsync(saving))
        {
            return FeatureFlagResult.Cancelled(MessagesIn(messages));
        }

        await _store.SaveAsync(new FeatureFlagOverride(flag.Name, json, userKey, when.UtcDateTime), cancellationToken);
        await AfterStoreAsync(() => _events.PublishAsync(
            new FeatureFlagOverrideSavedNotification(
                flag.Name, Copy(before?.Settings), before?.Override.Definition, Copy(normalized), json, flag.IsDeclared, userKey, when, cause, messages)
                .WithStateFrom(saving),
            cancellationToken));
        return FeatureFlagResult.Saved;
    }

    /// <summary>Removes a flag's override, so it goes back to its default; for an undeclared flag, removes it. Refused when the catalog says so.</summary>
    public async Task<FeatureFlagResult> ResetAsync(string flagName, FeatureFlagChangeCause cause, Guid? userKey, CancellationToken cancellationToken)
    {
        FeatureFlag? flag = await _catalog.FindAsync(flagName, cancellationToken);
        if (flag is null)
        {
            return FeatureFlagResult.NotFound;
        }

        FeatureFlagAction action = flag.IsDeclared ? flag.Actions.Reset : flag.Actions.Remove;
        if (!action.Allowed)
        {
            return FeatureFlagResult.NotAllowed(action.Reason);
        }

        SavedOverride? before = await SavedAsync(flag.Name, cancellationToken);
        DateTimeOffset when = _time.GetUtcNow();

        var messages = new EventMessages();
        var resetting = new FeatureFlagOverrideResettingNotification(
            flag.Name, Copy(before?.Settings), before?.Override.Definition, null, null, flag.IsDeclared, userKey, when, cause, messages);
        if (await _events.PublishCancelableAsync(resetting))
        {
            return FeatureFlagResult.Cancelled(MessagesIn(messages));
        }

        await _store.DeleteAsync(flag.Name, cancellationToken);
        await AfterStoreAsync(() => _events.PublishAsync(
            new FeatureFlagOverrideResetNotification(
                flag.Name, Copy(before?.Settings), before?.Override.Definition, null, null, flag.IsDeclared, userKey, when, cause, messages)
                .WithStateFrom(resetting),
            cancellationToken));
        return FeatureFlagResult.Saved;
    }

    /// <summary>
    /// Takes members or groups that no longer exist out of every override's audience. An audience left empty includes
    /// nobody, so a flag never opens up to everyone because its audience was deleted.
    /// </summary>
    public async Task RemoveFromAudiencesAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var removed = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var changes = new List<(string FlagName, FeatureFlagOverride Before, FlagSettings BeforeSettings, FlagSettings AfterSettings, string AfterJson)>();
        // This reads the store and the JSON itself, not the cache: it writes back, so it must start from what is stored,
        // never from a copy that may be a moment behind.
        foreach (FeatureFlagOverride saved in await _store.GetAllAsync(cancellationToken))
        {
            FlagSettings? settings = FlagDefinitions.ReadOverride(saved.Definition, out _);
            if (settings?.Audience is null)
            {
                continue;
            }

            var audience = new FlagAudience(
                settings.Audience.Members.Where(member => !removed.Contains(member)).ToArray(),
                settings.Audience.Groups.Where(group => !removed.Contains(group)).ToArray());
            if (audience.SameAs(settings.Audience))
            {
                continue;
            }

            FlagSettings after = FlagSettings.ForAudience(settings.Enabled, audience);
            string json = FlagDefinitions.ToJson(saved.FlagName, after);
            await _store.SaveAsync(saved with { Definition = json }, cancellationToken);
            changes.Add((saved.FlagName, saved, settings, after, json));
        }

        if (changes.Count == 0)
        {
            return;
        }

        DateTimeOffset when = _time.GetUtcNow();
        await AfterStoreAsync(async () =>
        {
            foreach ((string flagName, FeatureFlagOverride before, FlagSettings beforeSettings, FlagSettings afterSettings, string afterJson) in changes)
            {
                FeatureFlag? flag = await _catalog.FindAsync(flagName, cancellationToken);
                await _events.PublishAsync(
                    new FeatureFlagOverrideSavedNotification(
                        flagName, beforeSettings, before.Definition, afterSettings, afterJson, flag?.IsDeclared ?? false, null, when,
                        FeatureFlagChangeCause.MemberCleanup, new EventMessages()),
                    cancellationToken);
            }
        });
    }

    /// <summary>
    /// Once a change is stored: every server reads the overrides again, then the Saved or Reset notification is published.
    /// Inside an Umbraco scope the store hasn't committed yet, and reading from another connection would wait for it, so
    /// this waits for the scope to complete instead; a scope that doesn't complete, and so rolls the change back,
    /// publishes nothing and reads nothing.
    /// </summary>
    private async Task AfterStoreAsync(Func<Task> publish)
    {
        if (_scopes?.Context is { } context)
        {
            context.Enlist($"Umbricians.FeatureFlags.{Guid.NewGuid()}", completed =>
            {
                if (completed)
                {
                    RefreshAsync().GetAwaiter().GetResult();
                    publish().GetAwaiter().GetResult();
                }
            });
            return;
        }

        await RefreshAsync();
        await publish();
    }

    /// <summary>Settings for a notification, copied so a handler can't change what the cache holds.</summary>
    private static FlagSettings? Copy(FlagSettings? settings) => settings?.Copy();

    /// <summary>The override as the cache last read it, for the notifications' "before"; null when the flag has none.</summary>
    private async Task<SavedOverride?> SavedAsync(string flagName, CancellationToken cancellationToken) =>
        (await _cache.GetAsync(cancellationToken)).Overrides.GetValueOrDefault(flagName);

    private static IReadOnlyList<string> MessagesIn(EventMessages messages) =>
        messages.GetAll().Select(message => message.Message).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();

    private Task RefreshAsync()
    {
        // The distributed cache tells every server to read the overrides again in the background. This server waits for
        // its own read, so whatever the editor does next sees the change.
        _distributedCache?.RefreshAll(FeatureFlagsCacheRefresher.UniqueId);
        return _cache.ReloadAsync();
    }
}
