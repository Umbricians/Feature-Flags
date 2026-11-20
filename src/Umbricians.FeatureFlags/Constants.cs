namespace Umbricians.FeatureFlags;

/// <summary>
/// Names Feature Flags shares with sites and with Umbraco.
/// </summary>
public static class Constants
{
    /// <summary>
    /// The appsettings section the package reads its settings from.
    /// </summary>
    public const string ConfigurationSection = "Umbricians:FeatureFlags";

    /// <summary>
    /// The name of the package's Management API document in the backoffice's Swagger UI.
    /// </summary>
    public const string ApiName = "umbricians-feature-flags";
}
