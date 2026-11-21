using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Extensions;

/// <summary>
/// Declares flags in code and adds group providers, from <c>builder.AddFeatureFlags(flags => …)</c>.
/// </summary>
public sealed class FeatureFlagsBuilder
{
    internal FeatureFlagsBuilder(IServiceCollection services) => Services = services;

    /// <summary>The site's services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Declares a flag that is off, or on for everyone, until an editor changes it. A flag the site's configuration
    /// also declares takes its definition from configuration, and a warning is logged at startup.
    /// </summary>
    /// <param name="name">The name code checks, e.g. <c>Checkout.NewFlow</c>.</param>
    /// <param name="enabled">Whether the flag starts on for everyone. New flags should start off.</param>
    /// <param name="description">Shown to editors.</param>
    /// <param name="group">The group editors find it in; by default the text before the first <c>.</c> in its name.</param>
    /// <param name="tags">Words editors can filter by.</param>
    public FeatureFlagsBuilder Declare(
        string name,
        bool enabled = false,
        string? description = null,
        string? group = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Declare(FlagDefinitions.Simple(name, enabled), description, group, tags);
    }

    /// <summary>
    /// Declares a flag with any definition Microsoft.FeatureManagement supports, such as a time window or variants.
    /// The backoffice shows flags it can't edit read-only.
    /// </summary>
    public FeatureFlagsBuilder Declare(
        FeatureDefinition definition,
        string? description = null,
        string? group = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        Services.AddSingleton(new CodeFlagDeclaration(definition, new FlagDetails(description, group, tags?.ToArray() ?? [])));
        return this;
    }

    /// <summary>
    /// Adds a way of putting members in groups by a rule of the site's own; editors pick its groups beside member
    /// groups.
    /// </summary>
    public FeatureFlagsBuilder AddGroupProvider<TProvider>()
        where TProvider : class, IFeatureFlagGroupProvider
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFlagGroupProvider, TProvider>());
        return this;
    }
}
