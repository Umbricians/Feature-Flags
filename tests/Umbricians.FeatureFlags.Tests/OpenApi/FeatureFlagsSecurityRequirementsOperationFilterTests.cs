#if !UMBRACO_18_OR_GREATER
using System.Reflection;
using Umbricians.FeatureFlags.OpenApi;

namespace Umbricians.FeatureFlags.Tests.OpenApi;

public class FeatureFlagsSecurityRequirementsOperationFilterTests
{
    [Test]
    public void Sends_the_backoffice_token_with_calls_to_the_packages_api_document()
    {
        PropertyInfo apiName = typeof(FeatureFlagsSecurityRequirementsOperationFilter)
            .GetProperty("ApiName", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.That(apiName.GetValue(new FeatureFlagsSecurityRequirementsOperationFilter()), Is.EqualTo(Constants.ApiName));
    }
}
#endif
