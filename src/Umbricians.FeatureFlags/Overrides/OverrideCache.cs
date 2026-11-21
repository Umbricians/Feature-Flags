using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Every override, in memory, so checking a flag never reads the store. The overrides are read when the site starts,
/// and again in the background whenever one changes: the cache refresher calls <see cref="Reload"/> on every server.
/// Until a new read finishes, checks keep using the last one. If the store can't be read, for example before the
/// package's migration has run, the last good read stays in use (or every flag uses its default, if there is none yet)
/// and the store is tried again in the background a little later.
/// </summary>
internal sealed class OverrideCache
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<OverrideCache> _logger;
    private readonly object _gate = new();
    private volatile Snapshot? _snapshot;
    private Task _reading = Task.CompletedTask;
    private bool _readingNow;
    private bool _readAgain;

    public OverrideCache(IServiceScopeFactory scopes, TimeProvider time, ILogger<OverrideCache> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// The overrides. Only the very first call on a server can wait for the store, if the site hasn't finished its first
    /// read yet; every later call answers from memory.
    /// </summary>
    public async ValueTask<Snapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        Snapshot? snapshot = _snapshot;
        if (snapshot is null)
        {
            await ReadAsync().WaitAsync(cancellationToken);
            return _snapshot!;
        }

        if (snapshot.RetryAt is { } retryAt && retryAt <= _time.GetUtcNow())
        {
            RetryInBackground();
        }

        return snapshot;
    }

    /// <summary>Reads the overrides again in the background, keeping the current ones in use until it finishes.</summary>
    public void Reload() => _ = ReadAsync();

    /// <summary>Reads the overrides again and waits until they are in use, so this server sees a change it just made.</summary>
    public Task ReloadAsync() => ReadAsync();

    private void RetryInBackground()
    {
        lock (_gate)
        {
            if (_readingNow)
            {
                return;
            }
        }

        Reload();
    }

    /// <summary>
    /// Starts a read, or, when one is already running, asks it to read once more when it finishes, so a change made
    /// while it was reading is never missed. Either way the task completes once a read that began after this call is in
    /// use.
    /// </summary>
    private Task ReadAsync()
    {
        lock (_gate)
        {
            if (_readingNow)
            {
                _readAgain = true;
                return _reading;
            }

            _readingNow = true;

            // The read runs on its own, outside whatever request or Umbraco scope asked for it.
            using (ExecutionContext.SuppressFlow())
            {
                _reading = Task.Run(ReadUntilCurrentAsync);
            }

            return _reading;
        }
    }

    private async Task ReadUntilCurrentAsync()
    {
        while (true)
        {
            lock (_gate)
            {
                _readAgain = false;
            }

            try
            {
                Snapshot read = await LoadAsync(CancellationToken.None);
                Snapshot? current = _snapshot;
                _snapshot = read.RetryAt is not null && current is not null
                    ? current with { RetryAt = read.RetryAt }
                    : read;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Feature Flags couldn't read the settings saved in the backoffice.");
                _snapshot ??= new Snapshot(new Dictionary<string, SavedOverride>(StringComparer.OrdinalIgnoreCase), _time.GetUtcNow() + RetryAfterFailure);
            }

            lock (_gate)
            {
                if (!_readAgain)
                {
                    _readingNow = false;
                    return;
                }
            }
        }
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
                "Feature Flags couldn't read the settings saved in the backoffice, so flags keep the settings read last, or "
                + "their defaults if there are none yet. If the package was just installed, its migration may not have run yet.");
            return new Snapshot(new Dictionary<string, SavedOverride>(StringComparer.OrdinalIgnoreCase), _time.GetUtcNow() + RetryAfterFailure);
        }

        IReadOnlyDictionary<string, SavedOverride> entries = await SavedOverride.ReadAllAsync(
            overrides,
            (flagName, exception) => _logger.LogWarning(exception,
                "Feature Flags couldn't read the setting saved for the flag '{FlagName}', so it uses its default.", flagName));

        return new Snapshot(entries, null);
    }

    /// <summary>The overrides as last read. <see cref="RetryAt"/> is set when the latest read failed.</summary>
    public sealed record Snapshot(IReadOnlyDictionary<string, SavedOverride> Overrides, DateTimeOffset? RetryAt);
}
