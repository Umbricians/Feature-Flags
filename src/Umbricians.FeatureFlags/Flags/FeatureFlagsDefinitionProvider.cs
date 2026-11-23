using System.Runtime.ExceptionServices;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// The flag definitions Microsoft.FeatureManagement checks, as <see cref="FlagResolution"/> decides them: a flag is
/// declared by the site's own flag source, or else in code; an editor's override replaces its definition, unless
/// overrides are read-only. An override for a flag nobody declares has no effect.
/// </summary>
/// <remarks>
/// Microsoft asks for a definition on every check and keeps no copy, so this answers from the
/// <see cref="OverrideCache"/> and the flag source, never the database.
/// </remarks>
internal sealed class FeatureFlagsDefinitionProvider(FlagResolution resolution) : IFeatureDefinitionProvider
{
    public async Task<FeatureDefinition> GetFeatureDefinitionAsync(string featureName) =>
        // Microsoft treats a missing flag as off (or throws, if the site asked it to).
        (await resolution.FindAsync(featureName))?.Definition!;

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        ResolvedFlags all = await resolution.GetAllAsync(CancellationToken.None);
        if (all.ListingError is not null)
        {
            // Microsoft sees the flag source's exception when it can't list, as it would without Feature Flags.
            ExceptionDispatchInfo.Throw(all.ListingError);
        }

        foreach (ResolvedFlag flag in all.Flags)
        {
            if (flag.Definition is not null)
            {
                yield return flag.Definition;
            }
        }
    }
}
