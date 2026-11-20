using Umbraco.Cms.Api.Management.OpenApi;

namespace Umbricians.FeatureFlags.OpenApi;

/// <summary>
/// Makes the Swagger UI send the backoffice user's token with the package's API calls.
/// </summary>
internal sealed class FeatureFlagsSecurityRequirementsOperationFilter : BackOfficeSecurityRequirementsOperationFilterBase
{
    protected override string ApiName => Constants.ApiName;
}
