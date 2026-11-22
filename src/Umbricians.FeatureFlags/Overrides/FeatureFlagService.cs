using Umbraco.Cms.Core.Cache;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Reads flags and saves and removes their overrides, and tells every server to read them again. Calls through
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

    public FeatureFlagService(
        IFeatureFlagOverrideStore store,
        FlagCatalog catalog,
        OverrideCache cache,
        TimeProvider time,
        AudienceGroups groups,
        DistributedCache? distributedCache = null)
    {
        _store = store;
        _catalog = catalog;
        _cache = cache;
        _time = time;
        _groups = groups;
        _distributedCache = distributedCache;
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

        return await SaveFlagAsync(flag, flag.Settings! with { Enabled = enabled }, cause, userKey, cancellationToken);
    }

    /// <summary>Saves settings for a flag that has been found and is declared, if the rules allow it.</summary>
    private async Task<FeatureFlagResult> SaveFlagAsync(FeatureFlag flag, FlagSettings settings, FeatureFlagChangeCause cause, Guid? userKey, CancellationToken cancellationToken)
    {
        if (!flag.Actions.Save.Allowed)
        {
            return FeatureFlagResult.NotAllowed(flag.Actions.Save.Reason);
        }

        FlagSettings normalized = settings.Audience is null
            ? settings
            : FlagSettings.ForAudience(settings.Enabled, (await _groups.ReadAsync(cancellationToken)).Normalize(settings.Audience));
        await _store.SaveAsync(
            new FeatureFlagOverride(flag.Name, FlagDefinitions.ToJson(flag.Name, normalized), userKey, _time.GetUtcNow().UtcDateTime),
            cancellationToken);
        await RefreshAsync();
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

        await _store.DeleteAsync(flag.Name, cancellationToken);
        await RefreshAsync();
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
        bool changed = false;
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

            await _store.SaveAsync(
                saved with { Definition = FlagDefinitions.ToJson(saved.FlagName, FlagSettings.ForAudience(settings.Enabled, audience)) },
                cancellationToken);
            changed = true;
        }

        if (changed)
        {
            await RefreshAsync();
        }
    }

    private Task RefreshAsync()
    {
        // The distributed cache tells every server to read the overrides again in the background. This server waits for
        // its own read, so whatever the editor does next sees the change.
        _distributedCache?.RefreshAll(FeatureFlagsCacheRefresher.UniqueId);
        return _cache.ReloadAsync();
    }
}
