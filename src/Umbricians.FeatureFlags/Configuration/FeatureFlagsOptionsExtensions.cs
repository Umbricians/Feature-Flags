namespace Umbricians.FeatureFlags.Configuration;

internal static class FeatureFlagsOptionsExtensions
{
    /// <summary>Whether editors' settings from the backoffice are switched off, so every flag uses its default.</summary>
    public static bool OverridesAreReadOnly(this FeatureFlagsOptions options) => options.Overrides != FeatureFlagsOverrides.Enabled;
}
