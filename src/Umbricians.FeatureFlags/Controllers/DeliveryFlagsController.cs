using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Core.Configuration.Models;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Controllers;

/// <summary>
/// Tells a headless front end which public flags are on for its request. It needs no backoffice user: a visitor, a
/// member (by cookie or Delivery API member token) or a server calling for one all reach it, subject to the Delivery
/// API's own access settings. It is off until <c>Umbricians:FeatureFlags:Headless:Enabled</c> is true.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Feature Flags")]
[MapToApi(Constants.DeliveryApiName)]
[Route("umbraco/umbriciansfeatureflags/delivery/v{version:apiVersion}")]
internal sealed class DeliveryFlagsController(
    FlagCatalog catalog,
    IFeatureManager features,
    IOptionsMonitor<FeatureFlagsOptions> options,
    IOptionsMonitor<DeliveryApiSettings> deliveryApi,
    ILogger<DeliveryFlagsController> logger) : ControllerBase
{
    internal const string ApiKeyHeader = "Api-Key";

    /// <summary>
    /// Every public flag and whether it is on for this request, such as <c>{ "Checkout.ExpressPay": true }</c>. Audiences
    /// and time windows apply, and the <c>Start-Item</c> header places the request in a root. The answer is never cached.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    [HttpGet("flags")]
    [ProducesResponseType<IDictionary<string, bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Flags(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Headless.Enabled)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        if (!HasAccess())
        {
            return Unauthorized();
        }

        var answer = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (FeatureFlag flag in await catalog.GetAllAsync(cancellationToken))
        {
            if (flag is { IsDeclared: true, IsPublic: true })
            {
                answer[flag.Name] = await IsOnAsync(flag.Name, cancellationToken);
            }
        }

        return Ok(answer);
    }

    // The Delivery API's rule: open when PublicAccess is on, otherwise only with the configured ApiKey in the Api-Key header.
    // Not Umbraco's IApiAccessService, which exists only when the Delivery API is added and this works without it.
    private bool HasAccess()
    {
        DeliveryApiSettings settings = deliveryApi.CurrentValue;
        if (settings.PublicAccess)
        {
            return true;
        }

        string given = Request.Headers[ApiKeyHeader].ToString();
        return !string.IsNullOrWhiteSpace(settings.ApiKey)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(settings.ApiKey));
    }

    private async Task<bool> IsOnAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await features.IsEnabledAsync(name, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Feature Flags couldn't check the public flag {Flag}, so the headless endpoint reports it off.", name);
            return false;
        }
    }
}
