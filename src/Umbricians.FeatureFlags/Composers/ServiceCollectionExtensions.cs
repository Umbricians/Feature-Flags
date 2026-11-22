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
        services.AddSignedInTargeting();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFlagCondition, AudienceCondition>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFlagCondition, TimeWindowCondition>());
        services.TryAddSingleton<ISiteRoots, UmbracoSiteRoots>();
        services.TryAddSingleton<IRootLocator, UmbracoRootLocator>();
        services.TryAddSingleton<RequestRoot>();
        services.TryAddSingleton<DeletedRootFlags>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFilterMetadata, RootFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFlagCondition, RootCondition>());
        services.TryAddSingleton<ConditionRegistry>();
        services.TryAddSingleton<CodeFlags>();
        services.TryAddSingleton<PublicFlags>();
        services.TryAddSingleton(provider => new ConfigurationFlagDetails(provider.GetRequiredService<IConfiguration>()));
        services.TryAddSingleton<OverrideCache>();
        services.TryAddSingleton<IFeatureFlagOverrideStore, DatabaseOverrideStore>();
        services.TryAddSingleton<FlagResolution>();
        services.TryAddSingleton<FlagCatalog>();
        services.TryAddSingleton<FlagList>();
        services.TryAddSingleton<IMemberGroups, UmbracoMemberGroups>();
        services.TryAddSingleton<AudienceGroups>();
        services.TryAddSingleton<FeatureFlagService>();
        services.TryAddSingleton<IFeatureFlagService>(provider => provider.GetRequiredService<FeatureFlagService>());
        services.TryAddSingleton<FlagResponses>();
        return services;
    }

    /// <summary>
    /// Puts the package's targeting filters in place of Microsoft's, which share their name and would otherwise make
    /// checking an audience flag throw. They live as long as the feature manager does, so a site that scopes its feature
    /// management and its targeting accessor gets a filter that sees the request's accessor. A site that adds Microsoft's
    /// filters after this is caught at startup.
    /// </summary>
    private static void AddSignedInTargeting(this IServiceCollection services)
    {
        ServiceLifetime lifetime = services.LastOrDefault(service => service.ServiceType == typeof(IFeatureManager))?.Lifetime ?? ServiceLifetime.Singleton;
        services.RemoveAll<IFeatureFilterMetadata>(service =>
            service.ImplementationType == typeof(TargetingFilter) || service.ImplementationType == typeof(ContextualTargetingFilter));
        services.TryAddEnumerable(ServiceDescriptor.Describe(typeof(IFeatureFilterMetadata), typeof(SignedInTargetingFilter), lifetime));
        services.TryAddEnumerable(ServiceDescriptor.Describe(typeof(IFeatureFilterMetadata), typeof(SignedInContextualTargetingFilter), lifetime));
    }

    private static void RemoveAll<T>(this IServiceCollection services, Func<ServiceDescriptor, bool> matching)
    {
        foreach (ServiceDescriptor service in services.Where(service => service.ServiceType == typeof(T) && matching(service)).ToArray())
        {
            services.Remove(service);
        }
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
