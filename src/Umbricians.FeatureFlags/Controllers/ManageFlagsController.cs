using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Routing;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Security;

namespace Umbricians.FeatureFlags.Controllers;

/// <summary>
/// Changes flags from the backoffice. In its own controller because authorization is per controller: these need the
/// Manage permission as well as View.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Feature Flags")]
[BackOfficeRoute("umbriciansfeatureflags/api/v{version:apiVersion}")]
[FeatureFlagsPermission(Constants.Permissions.View)]
[FeatureFlagsPermission(Constants.Permissions.Manage)]
internal sealed class ManageFlagsController : FeatureFlagsControllerBase
{
    private readonly FeatureFlagService _overrides;
    private readonly IBackOfficeSecurityAccessor _security;

    public ManageFlagsController(FeatureFlagService overrides, IBackOfficeSecurityAccessor security)
    {
        _overrides = overrides;
        _security = security;
    }

    /// <summary>Saves an editor's settings for a flag, which replace its default on this site.</summary>
    [HttpPut("flag")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromQuery] string name, [FromBody] SaveFlagRequestModel request, CancellationToken cancellationToken)
    {
        FlagAudience? audience = request.Audience is null
            ? null
            : new FlagAudience(request.Audience.Members, request.Audience.Groups);
        return Respond(name, await _overrides.SaveAsync(name, FlagSettings.ForAudience(request.Enabled, audience), FeatureFlagChangeCause.Editor, CurrentUser(), cancellationToken));
    }

    /// <summary>Switches a flag on or off, keeping who it is for.</summary>
    [HttpPut("flag/enabled")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetEnabled([FromQuery] string name, [FromBody] SetFlagEnabledRequestModel request, CancellationToken cancellationToken) =>
        Respond(name, await _overrides.SwitchAsync(name, request.Enabled!.Value, FeatureFlagChangeCause.Editor, CurrentUser(), cancellationToken));

    /// <summary>Resets a flag to its default, or removes the setting of a flag nobody declares any more.</summary>
    [HttpDelete("flag")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reset([FromQuery] string name, CancellationToken cancellationToken) =>
        Respond(name, await _overrides.ResetAsync(name, FeatureFlagChangeCause.Editor, CurrentUser(), cancellationToken));

    private Guid? CurrentUser() => _security.BackOfficeSecurity?.CurrentUser?.Key;

    private IActionResult Respond(string name, FeatureFlagResult result) => result.Status switch
    {
        FeatureFlagResultStatus.Saved => Ok(),
        FeatureFlagResultStatus.NotFound => NotFound(new ProblemDetails { Title = "Flag not found", Detail = $"No flag source declares '{name}'." }),
        FeatureFlagResultStatus.Cancelled => Conflict(Cancelled(result.Messages)),
        _ => Conflict(new ProblemDetails { Title = "Flag can't be changed here", Detail = result.Reason }),
    };

    /// <summary>A handler cancelled the change: its messages are the detail, and listed in <c>messages</c> as well.</summary>
    private static ProblemDetails Cancelled(IReadOnlyList<string> messages)
    {
        var problem = new ProblemDetails
        {
            Title = "Flag can't be changed here",
            Detail = messages.Count > 0 ? string.Join(" ", messages) : "A handler of the Feature Flags notifications cancelled the change.",
        };
        problem.Extensions["messages"] = messages;
        return problem;
    }
}
