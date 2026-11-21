using Umbraco.Cms.Core.DependencyInjection;
using Umbricians.FeatureFlags.Composers;

namespace Umbricians.FeatureFlags.Extensions;

/// <summary>
/// Configures Feature Flags from a site's composer or <c>Program.cs</c>.
/// </summary>
public static class FeatureFlagsBuilderExtensions
{
    /// <summary>
    /// Declares flags in code and adds group providers. The package works without calling this; it is only needed for
    /// what configuration can't do.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.AddFeatureFlags(flags => flags
    ///     .Declare("Checkout.NewFlow", description: "The redesigned checkout.")
    ///     .AddGroupProvider&lt;PremiumSubscribers&gt;());
    /// </code>
    /// </example>
    public static IUmbracoBuilder AddFeatureFlags(this IUmbracoBuilder builder, Action<FeatureFlagsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.AddFeatureFlags(builder.Config);
        configure(new FeatureFlagsBuilder(builder.Services));
        return builder;
    }
}
