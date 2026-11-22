using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// A flag as the backoffice sees it: each declared flag with its default and any override, and each override for a flag
/// nobody declares any more. Read it with <see cref="IFeatureFlagService"/>.
/// </summary>
/// <param name="Name">The flag's name, as its flag source spells it.</param>
/// <param name="IsDeclared">False for an override whose flag nobody declares any more.</param>
/// <param name="DeclaredIn">Where the flag is declared; the site's flag source wins when code declares it too.</param>
/// <param name="DeclaredTwice">Whether code and the flag source both declare it.</param>
/// <param name="Section">The section it is listed in: the one it was given, or the text before the first dot of its name; null for none.</param>
/// <param name="Description">What the flag is for, when it has been described.</param>
/// <param name="Tags">The tags it has been given.</param>
/// <param name="IsPublic">Whether it is declared public, so the headless endpoint may serve it.</param>
/// <param name="State">How it is evaluated now.</param>
/// <param name="DefaultState">How its default is evaluated.</param>
/// <param name="Settings">What applies now, or null when it uses more than <see cref="FlagSettings"/> can say.</param>
/// <param name="DefaultSettings">Its default as <see cref="FlagSettings"/>, or null when it uses more than they can say.</param>
/// <param name="Override">Its override: who saved it and when, with the definition as stored; null when it has none.</param>
/// <param name="DiffersFromDefault">Whether its override says something different from its default.</param>
/// <param name="Actions">What may be done with it, each with the reason when not.</param>
public sealed record FeatureFlag(
    string Name,
    bool IsDeclared,
    FeatureFlagDeclaredIn DeclaredIn,
    bool DeclaredTwice,
    string? Section,
    string? Description,
    IReadOnlyList<string> Tags,
    bool IsPublic,
    FeatureFlagState State,
    FeatureFlagState DefaultState,
    FlagSettings? Settings,
    FlagSettings? DefaultSettings,
    FeatureFlagOverride? Override,
    bool DiffersFromDefault,
    FeatureFlagActions Actions);

/// <summary>How a flag is evaluated.</summary>
public enum FeatureFlagState
{
    /// <summary>Off for everyone.</summary>
    Off,

    /// <summary>On for everyone.</summary>
    Everyone,

    /// <summary>On only for an audience of members and groups.</summary>
    Audience,

    /// <summary>On when conditions other than an audience hold. Shown as <see cref="Custom"/> until the backoffice knows which conditions are registered.</summary>
    Conditions,

    /// <summary>Something the backoffice can't edit, such as a time window nobody registered an editor for, or variants.</summary>
    Custom,
}
