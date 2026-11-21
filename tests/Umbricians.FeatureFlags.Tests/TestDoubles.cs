using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbricians.FeatureFlags.Composers;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests;

/// <summary>Keeps overrides in a dictionary, and can be told to fail like a database whose table doesn't exist yet.</summary>
internal sealed class InMemoryOverrideStore : IFeatureFlagOverrideStore
{
    private readonly Dictionary<string, FeatureFlagOverride> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public bool Fail { get; set; }

    public int Reads => _reads;

    /// <summary>While set, reads wait until it completes, like a slow database.</summary>
    public TaskCompletionSource? Hold { get; set; }

    private int _reads;

    public async Task<IReadOnlyCollection<FeatureFlagOverride>> GetAllAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        if (Hold is { } hold)
        {
            await hold.Task;
        }

        if (Fail)
        {
            throw new InvalidOperationException("No such table.");
        }

        return _overrides.Values.ToArray();
    }

    public Task SaveAsync(FeatureFlagOverride featureFlagOverride, CancellationToken cancellationToken)
    {
        _overrides[featureFlagOverride.FlagName] = featureFlagOverride;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string flagName, CancellationToken cancellationToken)
    {
        _overrides.Remove(flagName);
        return Task.CompletedTask;
    }

    /// <summary>Saves the settings as the backoffice would.</summary>
    public InMemoryOverrideStore With(string flagName, FlagSettings settings) => WithJson(flagName, FlagDefinitions.ToJson(flagName, settings));

    public InMemoryOverrideStore WithJson(string flagName, string json)
    {
        _overrides[flagName] = new FeatureFlagOverride(flagName, json, null, DateTime.UtcNow);
        return this;
    }

    public IReadOnlyCollection<FeatureFlagOverride> All => _overrides.Values;
}

/// <summary>Says the visitor is whoever the test sets.</summary>
internal sealed class FixedTargetingContextAccessor : ITargetingContextAccessor
{
    public TargetingContext Context { get; set; } = new() { Groups = [] };

    public ValueTask<TargetingContext> GetContextAsync() => new(Context);

    public void SignIn(string userId, params string[] groups) => Context = new TargetingContext { UserId = userId, Groups = groups };

    public void SignOut() => Context = new TargetingContext { Groups = [] };
}

/// <summary>A site with Feature Flags registered, built from appsettings JSON.</summary>
internal sealed class TestSite
{
    public TestSite(string appsettings = "{}", Action<IServiceCollection>? before = null, Action<IServiceCollection>? after = null)
    {
        Configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(appsettings)))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton<IFeatureFlagOverrideStore>(Store);
        services.AddSingleton<ITargetingContextAccessor>(Visitor);
        before?.Invoke(services);
        services.AddFeatureFlags(Configuration);
        after?.Invoke(services);
        Services = services;
        Provider = services.BuildServiceProvider();
    }

    public IConfiguration Configuration { get; }

    public InMemoryOverrideStore Store { get; } = new();

    public FixedTargetingContextAccessor Visitor { get; } = new();

    public IServiceCollection Services { get; }

    public ServiceProvider Provider { get; }

    public IFeatureManager Features => Provider.GetRequiredService<IFeatureManager>();

    public Task<bool> IsOnAsync(string flag) => Features.IsEnabledAsync(flag);

    /// <summary>What happens on every server when an override is saved.</summary>
    public void Refresh() => Provider.GetRequiredService<OverrideCache>().ReloadAsync().GetAwaiter().GetResult();
}

/// <summary>
/// A site's own flag source that can't list its flags: listing yields <c>A</c> and then throws. Asked for flags one at a
/// time it knows <c>A</c>, <c>B</c> and <c>C</c>, throws for <c>Broken</c>, and knows nothing else.
/// </summary>
internal sealed class FailingListSource : IFeatureDefinitionProvider
{
    public Task<FeatureDefinition> GetFeatureDefinitionAsync(string featureName) => featureName switch
    {
        "A" or "B" or "C" => Task.FromResult(FlagDefinitions.Simple(featureName, true)),
        "Broken" => throw new InvalidOperationException("Can't read this flag."),
        _ => Task.FromResult<FeatureDefinition>(null!),
    };

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        yield return FlagDefinitions.Simple("A", true);
        await Task.Yield();
        throw new InvalidOperationException("Can't list the flags.");
    }
}

/// <summary>Remembers what was logged, so a test can say what a site would be told.</summary>
internal sealed class CollectingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
