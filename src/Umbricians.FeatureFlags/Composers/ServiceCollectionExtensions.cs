using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Controllers;
using Umbricians.FeatureFlags.OpenApi;

namespace Umbricians.FeatureFlags.Composers;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureFlags(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FeatureFlagsOptions>()
            .Bind(configuration.GetSection(Constants.ConfigurationSection))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMvcCore().ConfigureApplicationPartManager(parts =>
            parts.FeatureProviders.Add(new FeatureFlagsControllerFeatureProvider()));
        services.ConfigureOptions<FeatureFlagsSwaggerGenOptions>();

        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
