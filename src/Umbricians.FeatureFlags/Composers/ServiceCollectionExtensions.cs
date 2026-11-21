using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Controllers;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.OpenApi;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Composers;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Feature Flags and, if the site hasn't already, Microsoft.FeatureManagement. Safe to call more than once:
    /// the composer and <c>builder.AddFeatureFlags(…)</c> both call it.
    /// </summary>
    public static IServiceCollection AddFeatureFlags(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(service => service.ServiceType == typeof(FeatureFlagsMarker)))
        {
            return services;
        }

        services.AddSingleton<FeatureFlagsMarker>();

        services.AddOptions<FeatureFlagsOptions>()
            .Bind(configuration.GetSection(Constants.ConfigurationSection))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMvcCore().ConfigureApplicationPartManager(parts =>
            parts.FeatureProviders.Add(new FeatureFlagsControllerFeatureProvider()));
        services.ConfigureOptions<FeatureFlagsSwaggerGenOptions>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        services.AddFeatureManagementOnce();
        services.WrapFlagSource();

        // TryAdd, so a site's own accessor wins if it registered one first; one it registers later replaces this.
        services.TryAddSingleton<ITargetingContextAccessor, MemberTargetingContextAccessor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFilterMetadata, TargetingFilter>());

        services.TryAddSingleton<CodeFlags>();
        services.TryAddSingleton(provider => new ConfigurationFlagDetails(provider.GetRequiredService<IConfiguration>()));
        services.TryAddSingleton<OverrideCache>();
        services.TryAddSingleton<IFeatureFlagOverrideStore, DatabaseOverrideStore>();
        services.TryAddSingleton<FlagResolution>();
        services.TryAddSingleton<FlagCatalog>();
        services.TryAddSingleton<FlagList>();
        services.TryAddSingleton<IMemberGroups, UmbracoMemberGroups>();
        services.TryAddSingleton<AudienceGroups>();
        services.TryAddSingleton<OverrideService>();
        services.TryAddSingleton<FlagResponses>();
        return services;
    }

    /// <summary>
    /// Adds Microsoft.FeatureManagement unless the site already has: adding it twice registers its time window filter
    /// twice, and checking a flag that uses it then throws.
    /// </summary>
    private static void AddFeatureManagementOnce(this IServiceCollection services)
    {
        if (!services.Any(service => service.ServiceType == typeof(IFeatureManager)))
        {
            services.AddFeatureManagement();
        }
    }

    /// <summary>
    /// Puts Feature Flags in front of whichever flag source the site registered, by default Microsoft's provider for
    /// configuration.
    /// </summary>
    private static void WrapFlagSource(this IServiceCollection services)
    {
        ServiceDescriptor? existing = services.LastOrDefault(service => service.ServiceType == typeof(IFeatureDefinitionProvider));
        if (existing?.ImplementationType == typeof(FeatureFlagsDefinitionProvider))
        {
            return;
        }

        services.RemoveAll<IFeatureDefinitionProvider>();
        services.AddSingleton(provider => new FlagSource(CreateInner(provider, existing)));
        services.AddSingleton<IFeatureDefinitionProvider, FeatureFlagsDefinitionProvider>();
    }

    private static IFeatureDefinitionProvider CreateInner(IServiceProvider provider, ServiceDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return new ConfigurationFeatureDefinitionProvider(provider.GetRequiredService<IConfiguration>());
        }

        if (descriptor.ImplementationInstance is IFeatureDefinitionProvider instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IFeatureDefinitionProvider)descriptor.ImplementationFactory(provider);
        }

        return (IFeatureDefinitionProvider)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }

    /// <summary>Records that Feature Flags is registered, so registering it again does nothing.</summary>
    private sealed class FeatureFlagsMarker;
}
