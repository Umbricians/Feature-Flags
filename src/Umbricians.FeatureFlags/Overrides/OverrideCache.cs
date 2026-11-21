using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Every override, in memory, so a flag check never waits on the store. Loaded on first use and again after
/// <see cref="Invalidate"/>, which the cache refresher calls on every server when an override changes. If the store
/// can't be read, for example before the package's migration has run, flags use their defaults and the store is tried
/// again a little later.
/// </summary>
internal sealed class OverrideCache
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<OverrideCache> _logger;
    private readonly SemaphoreSlim _loading = new(1, 1);
    private volatile Snapshot? _snapshot;
    private int _version;

    public OverrideCache(IServiceScopeFactory scopes, TimeProvider time, ILogger<OverrideCache> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    /// <summary>The overrides, loading them first if needed.</summary>
    public async ValueTask<Snapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        Snapshot? snapshot = _snapshot;
        if (snapshot is not null && (snapshot.RetryAt is null || snapshot.RetryAt > _time.GetUtcNow()))
        {
            return snapshot;
        }

        await _loading.WaitAsync(cancellationToken);
        try
        {
            snapshot = _snapshot;
            if (snapshot is not null && (snapshot.RetryAt is null || snapshot.RetryAt > _time.GetUtcNow()))
            {
                return snapshot;
            }

            int version = Volatile.Read(ref _version);
            snapshot = await LoadAsync(cancellationToken);

            // A change that arrived while loading wins: leave the cache empty so the next check loads again.
            if (version == Volatile.Read(ref _version))
            {
                _snapshot = snapshot;
            }

            return snapshot;
        }
        finally
        {
            _loading.Release();
        }
    }

    /// <summary>Forgets the overrides, so the next check reads them again.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _snapshot = null;
    }

    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<FeatureFlagOverride> overrides;
        try
        {
            using IServiceScope scope = _scopes.CreateScope();
            overrides = await scope.ServiceProvider.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception,
                "Feature Flags couldn't read the settings saved in the backoffice, so every flag uses its default for now. "
                + "If the package was just installed, its migration may not have run yet.");
            return new Snapshot(new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase), _time.GetUtcNow() + RetryAfterFailure);
        }

        Dictionary<string, FeatureDefinition> definitions = await FlagDefinitions.ParseAsync(
            overrides.Select(o => (o.FlagName, o.Definition)),
            (flagName, exception) => _logger.LogWarning(exception,
                "Feature Flags couldn't read the setting saved for the flag '{FlagName}', so it uses its default.", flagName));

        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (FeatureFlagOverride featureFlagOverride in overrides)
        {
            definitions.TryGetValue(featureFlagOverride.FlagName, out FeatureDefinition? definition);
            entries[featureFlagOverride.FlagName] = new Entry(featureFlagOverride, definition);
        }

        return new Snapshot(entries, null);
    }

    /// <summary>The overrides as last read. <see cref="RetryAt"/> is set when reading them failed.</summary>
    public sealed record Snapshot(IReadOnlyDictionary<string, Entry> Overrides, DateTimeOffset? RetryAt);

    /// <summary>
    /// A saved override and its definition, which is null when its JSON can't be read; the flag then uses its default.
    /// </summary>
    public sealed record Entry(FeatureFlagOverride Override, FeatureDefinition? Definition);
}
