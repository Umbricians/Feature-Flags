using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Web.Common.Authorization;
using Umbricians.FeatureFlags.Controllers;

namespace Umbricians.FeatureFlags.Tests.Controllers;

public class FeatureFlagsControllerBaseTests
{
    [Test]
    public void Requires_a_signed_in_backoffice_user_on_every_controller()
    {
        var authorize = typeof(FeatureFlagsControllerBase).GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(authorize?.Policy, Is.EqualTo(AuthorizationPolicies.BackOfficeAccess));
        Assert.That(typeof(AuthorizeAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.Inherited, Is.True);
    }

    [Test]
    public void Puts_every_controller_in_the_package_api_document()
    {
        var mapToApi = typeof(FeatureFlagsControllerBase).GetCustomAttribute<MapToApiAttribute>();

        Assert.That(mapToApi?.ApiName, Is.EqualTo(Constants.ApiName));
    }
}
