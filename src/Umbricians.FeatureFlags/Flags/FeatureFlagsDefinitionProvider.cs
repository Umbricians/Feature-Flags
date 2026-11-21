using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// The flag definitions Microsoft.FeatureManagement checks. A flag is declared by the site's own flag source, or else in
/// code; an editor's override replaces its definition, unless overrides are read-only. An override for a flag nobody
/// declares has no effect.
/// </summary>
/// <remarks>
/// Microsoft asks for a definition on every check and keeps no copy, so this answers from the
/// <see cref="OverrideCache"/> and the flag source, never the database.
/// </remarks>
internal sealed class FeatureFlagsDefinitionProvider : IFeatureDefinitionProvider
{
    private readonly FlagSource _source;
    private readonly CodeFlags _code;
    private readonly OverrideCache _overrides;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;

    public FeatureFlagsDefinitionProvider(FlagSource source, CodeFlags code, OverrideCache overrides, IOptionsMonitor<FeatureFlagsOptions> options)
    {
        _source = source;
        _code = code;
        _overrides = overrides;
        _options = options;
    }

    public async Task<FeatureDefinition> GetFeatureDefinitionAsync(string featureName)
    {
        FeatureDefinition? declared = await _source.Inner.GetFeatureDefinitionAsync(featureName)
            ?? _code.Find(featureName)?.Definition;
        if (declared is null)
        {
            // Microsoft treats a missing flag as off (or throws, if the site asked it to).
            return null!;
        }

        return await WithOverrideAsync(declared);
    }

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (FeatureDefinition declared in _source.Inner.GetAllFeatureDefinitionsAsync())
        {
            if (seen.Add(declared.Name))
            {
                yield return await WithOverrideAsync(declared);
            }
        }

        foreach (CodeFlagDeclaration declaration in _code.All)
        {
            if (seen.Add(declaration.Definition.Name))
            {
                yield return await WithOverrideAsync(declaration.Definition);
            }
        }
    }

    private async ValueTask<FeatureDefinition> WithOverrideAsync(FeatureDefinition declared)
    {
        if (_options.CurrentValue.OverridesAreReadOnly())
        {
            return declared;
        }

        OverrideCache.Snapshot snapshot = await _overrides.GetAsync();
        if (snapshot.Overrides.TryGetValue(declared.Name, out OverrideCache.Entry? entry) && entry.Definition is not null)
        {
            // Keep the name as the flag source spells it.
            entry.Definition.Name = declared.Name;
            return entry.Definition;
        }

        return declared;
    }
}
