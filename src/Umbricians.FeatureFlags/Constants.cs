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

    /// <summary>
    /// The package id, which also names its migration plan.
    /// </summary>
    public const string PackageId = "Umbricians.FeatureFlags";

    /// <summary>
    /// User group permissions, granted in Users → User groups → Permissions → Feature Flags. They are stored with the
    /// group's other permissions; the backoffice registers matching <c>entityUserPermission</c> extensions.
    /// </summary>
    public static class Permissions
    {
        /// <summary>See the flags and their settings.</summary>
        public const string View = "Umbricians.FeatureFlags.View";

        /// <summary>Change a flag's settings, reset it to its default and remove undeclared flags.</summary>
        public const string Manage = "Umbricians.FeatureFlags.Manage";

        /// <summary>Every Feature Flags permission, for granting them all to a user group at once.</summary>
        public static IReadOnlyList<string> All { get; } = [View, Manage];
    }
}
