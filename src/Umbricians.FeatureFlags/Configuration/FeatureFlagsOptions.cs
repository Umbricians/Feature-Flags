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
