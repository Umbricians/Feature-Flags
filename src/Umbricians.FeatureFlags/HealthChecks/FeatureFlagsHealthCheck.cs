using Umbraco.Cms.Core.HealthChecks;
using Umbricians.FeatureFlags.Diagnostics;

namespace Umbricians.FeatureFlags.HealthChecks;

/// <summary>
/// Tells an administrator, from Settings → Health Check, whether Feature Flags is set up so the backoffice takes
/// effect. It shows what the startup log reports, because both come from the same
/// <see cref="FlagDiagnostics"/>, so the log and the dashboard agree. Each result links to its explanation in the docs.
/// It has no buttons: nothing here can be fixed with a click.
/// </summary>
[HealthCheck(HealthCheckId, "Feature Flags", Description = "Checks that flags set in the backoffice take effect, and lists problems with flags and the settings saved for them.", Group = "Feature Flags")]
internal sealed class FeatureFlagsHealthCheck(FlagDiagnostics diagnostics) : HealthCheck
{
    internal const string HealthCheckId = "6c1d0f7e-3a52-4b8e-9d4f-2e7a8c5b1f03";

    /// <summary>Where each result is explained: a heading in the getting started guide.</summary>
    internal const string DocsUrl = "https://github.com/Umbricians/Feature-Flags/blob/main/docs/getting-started.md#";

    /// <inheritdoc />
    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        IReadOnlyList<DiagnosticResult> results = await diagnostics.RunAsync(CancellationToken.None);
        return results.Select(result => new HealthCheckStatus(result.Message)
        {
            ResultType = result.Severity switch
            {
                DiagnosticSeverity.Error => StatusResultType.Error,
                DiagnosticSeverity.Warning => StatusResultType.Warning,
                DiagnosticSeverity.Info => StatusResultType.Info,
                _ => StatusResultType.Success,
            },
            ReadMoreLink = DocsUrl + result.Anchor,
        }).ToArray();
    }

    /// <inheritdoc />
    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) =>
        throw new InvalidOperationException("The Feature Flags health check has no actions.");
}
