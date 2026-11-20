using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Warns at startup about set-ups that quietly stop flags working as expected: a site that adds Microsoft's services
/// again after Umbraco has started composing, a flag source registered after Feature Flags, and a flag declared both
/// in code and by the site's flag source.
/// </summary>
internal sealed class StartupChecks(IServiceProvider services, ILogger<StartupChecks> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        string[] duplicateFilters = services.GetServices<IFeatureFilterMetadata>()
            .GroupBy(filter => filter.GetType())
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Name)
            .ToArray();
        if (duplicateFilters.Length > 0)
        {
            logger.LogError(
                "Microsoft.FeatureManagement is registered more than once, so these filters are too: {Filters}. Checking a flag "
                + "that uses one throws. Feature Flags adds Microsoft.FeatureManagement itself: remove the site's "
                + "AddFeatureManagement() call, or move it before Umbraco is added so Feature Flags uses it.",
                string.Join(", ", duplicateFilters));
        }

        if (services.GetService<IFeatureDefinitionProvider>() is not FeatureFlagsDefinitionProvider)
        {
            logger.LogWarning(
                "The site registered its own flag source after Feature Flags, so settings from the backoffice don't apply. "
                + "Register it before Umbraco is added, and Feature Flags wraps it.");
        }

        FlagSource source = services.GetRequiredService<FlagSource>();
        CodeFlags code = services.GetRequiredService<CodeFlags>();
        var declaredTwice = new List<string>();
        try
        {
            await foreach (FeatureDefinition definition in source.Inner.GetAllFeatureDefinitionsAsync().WithCancellation(cancellationToken))
            {
                if (code.Find(definition.Name) is not null)
                {
                    declaredTwice.Add(definition.Name);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Feature Flags couldn't list the flags the site's flag source declares.");
        }

        foreach (string name in declaredTwice)
        {
            logger.LogWarning(
                "The flag '{FlagName}' is declared both in code and by the site's flag source. The flag source's declaration is used.",
                name);
        }
    }
}
