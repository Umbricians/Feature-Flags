using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Saves and removes editors' overrides, and tells every server to read them again.
/// </summary>
internal sealed class OverrideService
{
    private readonly IFeatureFlagOverrideStore _store;
    private readonly FlagCatalog _catalog;
    private readonly OverrideCache _cache;
    private readonly DistributedCache? _distributedCache;
    private readonly TimeProvider _time;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;

    public OverrideService(
        IFeatureFlagOverrideStore store,
        FlagCatalog catalog,
        OverrideCache cache,
        TimeProvider time,
        IOptionsMonitor<FeatureFlagsOptions> options,
        DistributedCache? distributedCache = null)
    {
        _store = store;
        _catalog = catalog;
        _cache = cache;
        _time = time;
        _options = options;
        _distributedCache = distributedCache;
    }

    /// <summary>Saves an editor's settings for a flag.</summary>
    public async Task<OverrideResult> SaveAsync(string flagName, FlagSettings settings, Guid? userKey, CancellationToken cancellationToken)
    {
        CatalogFlag? flag = await _catalog.FindAsync(flagName, cancellationToken);
        if (flag is null || !flag.IsDeclared)
        {
            return OverrideResult.NotFound;
        }

        return await SaveFlagAsync(flag, settings, userKey, cancellationToken);
    }

    /// <summary>Switches a flag on or off, keeping its audience.</summary>
    public async Task<OverrideResult> SetEnabledAsync(string flagName, bool enabled, Guid? userKey, CancellationToken cancellationToken)
    {
        CatalogFlag? flag = await _catalog.FindAsync(flagName, cancellationToken);
        if (flag is null || !flag.IsDeclared)
        {
            return OverrideResult.NotFound;
        }

        if (!flag.Editable || flag.Settings is null)
        {
            return OverrideResult.ReadOnly(flag.ReadOnlyReason);
        }

        return await SaveFlagAsync(flag, flag.Settings with { Enabled = enabled }, userKey, cancellationToken);
    }

    /// <summary>Saves settings for a flag that has been found and is declared, if the backoffice may change it.</summary>
    private async Task<OverrideResult> SaveFlagAsync(CatalogFlag flag, FlagSettings settings, Guid? userKey, CancellationToken cancellationToken)
    {
        if (!flag.Editable)
        {
            return OverrideResult.ReadOnly(flag.ReadOnlyReason);
        }

        FlagSettings normalized = Normalize(settings);
        await _store.SaveAsync(
            new FeatureFlagOverride(flag.Name, FlagDefinitions.ToJson(flag.Name, normalized), userKey, _time.GetUtcNow().UtcDateTime),
            cancellationToken);
        await RefreshAsync();
        return OverrideResult.Saved;
    }

    /// <summary>Removes a flag's override, so it goes back to its default; for an undeclared flag, removes it.</summary>
    public async Task<OverrideResult> ResetAsync(string flagName, CancellationToken cancellationToken)
    {
        CatalogFlag? flag = await _catalog.FindAsync(flagName, cancellationToken);
        if (flag is null)
        {
            return OverrideResult.NotFound;
        }

        // Any flag can go back to its default, even one whose override the backoffice can't edit, unless the site
        // has switched backoffice settings off altogether.
        if (_options.CurrentValue.OverridesAreReadOnly())
        {
            return OverrideResult.ReadOnly(flag.ReadOnlyReason);
        }

        if (flag.Override is null)
        {
            return OverrideResult.Saved;
        }

        await _store.DeleteAsync(flag.Name, cancellationToken);
        await RefreshAsync();
        return OverrideResult.Saved;
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
                saved with { Definition = FlagDefinitions.ToJson(saved.FlagName, settings with { Audience = audience }) },
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

    /// <summary>Writes keys the way targeting compares them, and drops blanks and repeats.</summary>
    private static FlagSettings Normalize(FlagSettings settings)
    {
        if (settings.Audience is null)
        {
            return settings;
        }

        static string[] Clean(IEnumerable<string> ids) => ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => Guid.TryParse(id, out Guid key) ? TargetingIds.For(key) : id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return settings with { Audience = new FlagAudience(Clean(settings.Audience.Members), Clean(settings.Audience.Groups)) };
    }
}

/// <summary>What happened to a change an editor asked for.</summary>
internal sealed record OverrideResult(OverrideOutcome Outcome, string? Reason = null)
{
    public static OverrideResult Saved { get; } = new(OverrideOutcome.Saved);

    public static OverrideResult NotFound { get; } = new(OverrideOutcome.NotFound);

    public static OverrideResult ReadOnly(string? reason) => new(OverrideOutcome.ReadOnly, reason);
}

internal enum OverrideOutcome
{
    Saved,
    NotFound,
    ReadOnly,
}
