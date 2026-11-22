using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Diagnostics;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Logs at startup what the health check would show as an error or a warning, from the same <see cref="FlagDiagnostics"/>:
/// set-ups that quietly stop flags working as expected, such as a site that adds Microsoft's services again after Umbraco
/// has started composing, a flag source registered after Feature Flags, and a flag declared both in code and by the
/// site's flag source. Info and success results aren't problems, so they aren't logged.
/// </summary>
internal sealed class StartupChecks(IServiceProvider services, ILogger<StartupChecks> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        foreach (DiagnosticResult result in await services.GetRequiredService<FlagDiagnostics>().RunAsync(cancellationToken))
        {
            switch (result.Severity)
            {
                case DiagnosticSeverity.Error:
                    logger.LogError(result.Exception, "{Message}", result.Message);
                    break;
                case DiagnosticSeverity.Warning:
                    logger.LogWarning(result.Exception, "{Message}", result.Message);
                    break;
            }
        }
    }
}
