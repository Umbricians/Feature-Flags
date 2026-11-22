using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;
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
    private readonly FlagResponses _responses;
    private readonly IBackOfficeSecurityAccessor _security;

    public ManageFlagsController(FeatureFlagService overrides, FlagResponses responses, IBackOfficeSecurityAccessor security)
    {
        _overrides = overrides;
        _responses = responses;
        _security = security;
    }

    /// <summary>Saves settings for a flag, which replace its default on this site, and returns the flag as it is now.</summary>
    [HttpPut("flag")]
    [ProducesResponseType<FlagResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromQuery] string name, [FromBody] SaveFlagRequestModel request, CancellationToken cancellationToken)
    {
        if (!ApiNames.TryParseMatch(request.Match, out ConditionMatch match))
        {
            return BadRequest(new ProblemDetails { Type = ProblemTypes.Error, Title = "Invalid match", Detail = "match must be 'any' or 'all'." });
        }

        if (request.Conditions.Any(condition => condition is null))
        {
            return BadRequest(new ProblemDetails { Type = ProblemTypes.Error, Title = "Invalid conditions", Detail = "Every condition must be an object with a name and parameters, not null." });
        }

        var settings = new FlagSettings(
            request.Enabled,
            request.Conditions.Select(condition => new FlagCondition(condition.Name, (JsonObject)(condition.Parameters ?? new JsonObject()).DeepClone())).ToArray(),
            match);
        return await RespondAsync(name, await _overrides.SaveAsync(name, settings, FeatureFlagChangeCause.Editor, CurrentUser(), cancellationToken), cancellationToken);
    }

    /// <summary>Switches a flag on or off, keeping who it is for.</summary>
    [HttpPut("flag/enabled")]
    [ProducesResponseType<FlagResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetEnabled([FromQuery] string name, [FromBody] SetFlagEnabledRequestModel request, CancellationToken cancellationToken) =>
        await RespondAsync(name, await _overrides.SwitchAsync(name, request.Enabled!.Value, FeatureFlagChangeCause.Editor, CurrentUser(), cancellationToken), cancellationToken);

    /// <summary>Resets a flag to its default, or removes the setting of a flag nobody declares any more.</summary>
    [HttpDelete("flag")]
    [ProducesResponseType<FlagResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reset([FromQuery] string name, CancellationToken cancellationToken) =>
        await RespondAsync(name, await _overrides.ResetAsync(name, FeatureFlagChangeCause.Editor, CurrentUser(), cancellationToken), cancellationToken);

    private Guid? CurrentUser() => _security.BackOfficeSecurity?.CurrentUser?.Key;

    /// <summary>
    /// Saved is 200 with the flag as it is now (a flag nobody declares any more has gone, so its removal is a 200 with
    /// no body), NotFound is 404, Cancelled is 409 with the handlers' messages, and NotAllowed is 409 with the reason.
    /// </summary>
    internal async Task<IActionResult> RespondAsync(string name, FeatureFlagResult result, CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case FeatureFlagResultStatus.Saved:
                return await _overrides.GetAsync(name, cancellationToken) is { } flag
                    ? Ok(await _responses.FlagAsync(flag, cancellationToken))
                    : Ok();
            case FeatureFlagResultStatus.NotFound:
                return NotFound(new ProblemDetails { Type = ProblemTypes.Error, Title = "Flag not found", Detail = $"No flag source declares '{name}'." });
            case FeatureFlagResultStatus.Cancelled:
                return Conflict(Cancelled(result.Messages));
            default:
                return Conflict(NotAllowed(result));
        }
    }

    /// <summary>The rules refused the change: the reason is the detail, and any problems with conditions are listed in <c>problems</c> as well.</summary>
    private static ProblemDetails NotAllowed(FeatureFlagResult result)
    {
        var problem = new ProblemDetails { Type = ProblemTypes.Error, Title = "Flag can't be changed here", Detail = result.Reason };
        if (result.Problems.Count > 0)
        {
            problem.Extensions["problems"] = result.Problems;
        }

        return problem;
    }

    /// <summary>A handler cancelled the change: its messages are the detail, and listed in <c>messages</c> as well.</summary>
    private static ProblemDetails Cancelled(IReadOnlyList<string> messages)
    {
        var problem = new ProblemDetails
        {
            Type = ProblemTypes.Error,
            Title = "Flag can't be changed here",
            Detail = messages.Count > 0 ? string.Join(" ", messages) : "A handler of the Feature Flags notifications cancelled the change.",
        };
        problem.Extensions["messages"] = messages;
        return problem;
    }
}
