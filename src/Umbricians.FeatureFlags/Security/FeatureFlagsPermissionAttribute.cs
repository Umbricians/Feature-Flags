using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;

namespace Umbricians.FeatureFlags.Security;

/// <summary>
/// Requires the current backoffice user to have a Feature Flags permission through at least one of their user groups.
/// Apply alongside <c>AuthorizationPolicies.BackOfficeAccess</c>, which signs the user in; no section access is needed.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
internal sealed class FeatureFlagsPermissionAttribute : TypeFilterAttribute
{
    /// <param name="permission">One of <see cref="Constants.Permissions"/>.</param>
    public FeatureFlagsPermissionAttribute(string permission)
        : base(typeof(FeatureFlagsPermissionFilter))
    {
        Permission = permission;
        Arguments = [permission];
    }

    public string Permission { get; }

    private sealed class FeatureFlagsPermissionFilter(string permission, IBackOfficeSecurityAccessor security) : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            IUser? user = security.BackOfficeSecurity?.CurrentUser;
            if (!FeatureFlagsPermissions.Has(user, permission))
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Missing Feature Flags permission",
                    Detail = $"Your user group needs the '{permission}' permission.",
                })
                { StatusCode = StatusCodes.Status403Forbidden };
            }
        }
    }
}

internal static class FeatureFlagsPermissions
{
    /// <summary>Whether any of the user's groups grants the permission.</summary>
    public static bool Has(IUser? user, string permission) =>
        user?.Groups.Any(group => group.Permissions.Contains(permission)) == true;
}
