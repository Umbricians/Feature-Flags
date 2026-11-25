using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Diagnostics;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Logs at startup what the health check would show as an error or a warning, from the same <see cref="FlagDiagnostics"/>:
/// set-ups that quietly stop flags working as expected, such as a site that adds Microsoft's services again after Umbraco
/// has started composing, a flag source registered after Feature Flags, and a flag declared both in code and by the
/// site's flag source. Info and success results aren't problems, so they aren't logged.
/// </summary>
/// <remarks>
/// Umbraco resolves notification handlers from a scope that ends when the notification has been handled, but the checks
/// carry on in the background after that. So they take services from a scope of their own, which lasts as long as they do,
/// never from the provider the handler was resolved from.
/// </remarks>
internal sealed class StartupChecks(IServiceScopeFactory scopes, ILogger<StartupChecks> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    /// <summary>
    /// Starts the checks in the background and returns, so they never slow the site's start or fail it. Nothing runs while
    /// Umbraco is installing or upgrading, since the package's tables may not exist yet.
    /// </summary>
    public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        CancellationToken stopping;
        using (IServiceScope scope = scopes.CreateScope())
        {
            if (scope.ServiceProvider.GetService<IRuntimeState>() is { } runtime && runtime.Level != RuntimeLevel.Run)
            {
                return Task.CompletedTask;
            }

            stopping = scope.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;
        }

        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => RunAsync(stopping), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <summary>Runs the checks and logs what they found. Never throws: a check that fails is logged as a warning.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            foreach (DiagnosticResult result in await scope.ServiceProvider.GetRequiredService<FlagDiagnostics>().RunAsync(cancellationToken))
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
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Feature Flags couldn't run its startup checks. The health check in Settings can run them again.");
        }
    }
}
