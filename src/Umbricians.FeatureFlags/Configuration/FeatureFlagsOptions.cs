namespace Umbricians.FeatureFlags.Configuration;

/// <summary>
/// Options bound from the <c>Umbricians:FeatureFlags</c> configuration section.
/// </summary>
public class FeatureFlagsOptions
{
    /// <summary>
    /// Whether editors' settings from the backoffice apply. <see cref="FeatureFlagsOverrides.ReadOnly"/> leaves the
    /// site's flag source in charge: the backoffice shows the flags but can't change them, and settings saved earlier
    /// are ignored. Defaults to <see cref="FeatureFlagsOverrides.Enabled"/>.
    /// </summary>
    public FeatureFlagsOverrides Overrides { get; set; } = FeatureFlagsOverrides.Enabled;

    /// <summary>
    /// The headless endpoint, which tells a front end that isn't rendered by Umbraco which public flags are on for its
    /// request. Off until a developer switches it on.
    /// </summary>
    public FeatureFlagsHeadless Headless { get; set; } = new();
}

/// <summary>
/// Settings for the headless endpoint, <c>Umbricians:FeatureFlags:Headless</c>.
/// </summary>
public class FeatureFlagsHeadless
{
    /// <summary>
    /// Whether <c>GET /umbraco/umbriciansfeatureflags/delivery/v1/flags</c> answers. While false, which is the default,
    /// it answers 404, so a site that doesn't use it exposes nothing. It follows the Delivery API's own access settings.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Whether editors' settings from the backoffice apply.
/// </summary>
public enum FeatureFlagsOverrides
{
    /// <summary>Editors' settings win over each flag's default.</summary>
    Enabled,

    /// <summary>Flags are shown but can't be changed, and settings saved earlier are ignored.</summary>
    ReadOnly,
}
