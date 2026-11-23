namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Where editors' settings for flags are kept. The package keeps them in the Umbraco database; a site can register its
/// own store to keep them somewhere else, and its registration wins over the package's.
/// </summary>
/// <remarks>
/// The package reads every override once, keeps them in memory and reads them again when one changes, on every server,
/// so a store only needs to be fast enough for that, not for every flag check.
/// </remarks>
public interface IFeatureFlagOverrideStore
{
    /// <summary>Every saved override.</summary>
    Task<IReadOnlyCollection<FeatureFlagOverride>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Saves an override, replacing any saved before for the same flag. Flag names are compared ignoring case.</summary>
    Task SaveAsync(FeatureFlagOverride featureFlagOverride, CancellationToken cancellationToken);

    /// <summary>Removes the override for a flag, if there is one.</summary>
    Task DeleteAsync(string flagName, CancellationToken cancellationToken);
}

/// <summary>An editor's setting for a flag, which replaces the flag's default on this site.</summary>
/// <param name="FlagName">The flag's name.</param>
/// <param name="Definition">The flag in Microsoft's <c>feature_management</c> JSON.</param>
/// <param name="UpdatedBy">The key of the backoffice user who saved it, when there was one.</param>
/// <param name="UpdatedUtc">When it was saved.</param>
public sealed record FeatureFlagOverride(string FlagName, string Definition, Guid? UpdatedBy, DateTime UpdatedUtc);
