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

        // Two filters of the same kind under one name make checking that filter's flags throw. It happens when a site adds
        // Microsoft's targeting (AddFeatureManagement().WithTargeting<T>()) after Feature Flags, which has its own.
        string[] sharedNames = services.GetServices<IFeatureFilterMetadata>()
            .GroupBy(filter => (Alias: FilterName(filter.GetType()).ToLowerInvariant(), Contextual: ContextOf(filter.GetType())))
            .Where(group => group.Select(filter => filter.GetType()).Distinct().Count() > 1)
            .Select(group => $"'{FilterName(group.First().GetType())}' ({string.Join(", ", group.Select(filter => filter.GetType().Name))})")
            .ToArray();
        if (sharedNames.Length > 0)
        {
            logger.LogError(
                "More than one feature filter has the same name, so checking a flag that uses it throws: {Filters}. Feature Flags "
                + "registers its own targeting filters in place of Microsoft's, so a site's AddFeatureManagement().WithTargeting<T>() "
                + "must come before Umbraco is added (then Feature Flags replaces Microsoft's filter), not after.",
                string.Join("; ", sharedNames));
        }

        if (services.GetService<IFeatureDefinitionProvider>() is not FeatureFlagsDefinitionProvider)
        {
            logger.LogWarning(
                "The site registered its own flag source after Feature Flags, so settings from the backoffice don't apply. "
                + "Register it before Umbraco is added, and Feature Flags wraps it.");
        }

        FlagResolution resolution = services.GetRequiredService<FlagResolution>();
        DeclaredFlags declared = await resolution.GetDeclaredAsync(cancellationToken);
        if (declared.ListingError is not null)
        {
            logger.LogWarning(declared.ListingError, "Feature Flags couldn't list the flags the site's flag source declares.");
        }

        foreach (DeclaredFlag flag in declared.Flags.Where(flag => flag.DeclaredTwice))
        {
            logger.LogWarning(
                "The flag '{FlagName}' is declared both in code and by the site's flag source. The flag source's declaration is used.",
                flag.Name);
        }
    }

    // The name a flag uses for a filter, as Microsoft works it out: its alias, or its class name without "Filter".
    private static string FilterName(Type filter)
    {
        string? alias = filter.GetCustomAttributes(typeof(FilterAliasAttribute), true).OfType<FilterAliasAttribute>().FirstOrDefault()?.Alias;
        if (alias is not null)
        {
            return alias;
        }

        return filter.Name.EndsWith("Filter", StringComparison.Ordinal) ? filter.Name[..^"Filter".Length] : filter.Name;
    }

    // Filters that take the caller's own context and filters that don't are looked up separately, so only a pair of the same kind clashes.
    private static Type? ContextOf(Type filter) =>
        filter.GetInterfaces().FirstOrDefault(face => face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IContextualFeatureFilter<>))?.GetGenericArguments()[0];
}
