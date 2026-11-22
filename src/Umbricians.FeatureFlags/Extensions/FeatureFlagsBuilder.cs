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
    private readonly IServiceCollection _services;

    internal FeatureFlagsBuilder(IServiceCollection services) => _services = services;

    /// <summary>
    /// Declares a flag that is off, or on for everyone, until an editor changes it. A flag the site's configuration
    /// also declares takes its definition from configuration, and a warning is logged at startup.
    /// </summary>
    /// <param name="name">The name code checks, e.g. <c>Checkout.NewFlow</c>.</param>
    /// <param name="enabled">Whether the flag starts on for everyone. New flags should start off.</param>
    /// <param name="description">Shown to editors.</param>
    /// <param name="section">The section of the flag list editors find it in; by default the text before the first <c>.</c> in
    /// its name.</param>
    /// <param name="tags">Words editors can filter by.</param>
    /// <param name="isPublic">Whether the flag may be shown to visitors' browsers; flags are private unless declared public.</param>
    public FeatureFlagsBuilder Declare(
        string name,
        bool enabled = false,
        string? description = null,
        string? section = null,
        IEnumerable<string>? tags = null,
        bool isPublic = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Declare(FlagDefinitions.Simple(name, enabled), description, section, tags, isPublic);
    }

    /// <summary>
    /// Declares a flag with any definition Microsoft.FeatureManagement supports, such as a time window or variants.
    /// The backoffice shows flags it can't edit read-only.
    /// </summary>
    public FeatureFlagsBuilder Declare(
        FeatureDefinition definition,
        string? description = null,
        string? section = null,
        IEnumerable<string>? tags = null,
        bool isPublic = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        _services.AddSingleton(new CodeFlagDeclaration(definition, new FlagDetails(description, section, tags?.ToArray() ?? [], isPublic)));
        return this;
    }

    /// <summary>
    /// Marks flags as public, whichever flag source declares them: code, configuration or the site's own. It wins over a
    /// flag that says <c>"public": false</c> in configuration or <c>isPublic: false</c> in code.
    /// </summary>
    /// <param name="names">The flags' names, which are compared without regard to case.</param>
    public FeatureFlagsBuilder MakePublic(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        _services.AddSingleton(new PublicFlagNames(names));
        return this;
    }

    /// <summary>
    /// Adds a way of putting members in groups by a rule of the site's own; editors pick its groups beside member
    /// groups.
    /// </summary>
    public FeatureFlagsBuilder AddGroupProvider<TProvider>()
        where TProvider : class, IFeatureFlagGroupProvider
    {
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFlagGroupProvider, TProvider>());
        return this;
    }
}
