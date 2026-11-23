using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Targeting;

/// <summary>
/// Tells Microsoft.FeatureManagement's targeting who the signed-in member is: their key as the user id, and as groups
/// the key and name of each of their member groups plus the groups every <see cref="IFeatureFlagGroupProvider"/> puts
/// them in. The member is the one signed in with the member cookie or with a Delivery API member token. A token is
/// only recognised on this package's headless endpoint and other /umbraco/ paths, where Umbraco's token handling runs;
/// on a site page the member cookie is what counts. A visitor who isn't signed in has no user id and no groups. Worked out once per request.
/// </summary>
/// <remarks>
/// A site that identifies people another way registers its own <see cref="ITargetingContextAccessor"/>, which wins.
/// </remarks>
internal sealed class MemberTargetingContextAccessor : ITargetingContextAccessor
{
    private static readonly object ItemKey = new();

    /// <summary>The scheme the Delivery API reads member tokens with, which is OpenIddict's validation.</summary>
    private const string MemberTokenScheme = "OpenIddict.Validation.AspNetCore";

    /// <summary>The claim the Delivery API puts a member's key in when it makes a token.</summary>
    private const string MemberKeyClaim = "umbraco-member-key";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IEnumerable<IFeatureFlagGroupProvider> _groupProviders;
    private readonly ILogger<MemberTargetingContextAccessor> _logger;

    public MemberTargetingContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        IEnumerable<IFeatureFlagGroupProvider> groupProviders,
        ILogger<MemberTargetingContextAccessor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _groupProviders = groupProviders;
        _logger = logger;
    }

    public async ValueTask<TargetingContext> GetContextAsync()
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Anonymous();
        }

        if (httpContext.Items.TryGetValue(ItemKey, out object? cached) && cached is TargetingContext known)
        {
            return known;
        }

        TargetingContext context;
        try
        {
            context = await ForCurrentMemberAsync(httpContext.RequestServices, httpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception,
                "Feature Flags couldn't work out who the signed-in member is, so flags are checked as for a visitor who isn't signed in.");
            context = Anonymous();
        }

        httpContext.Items[ItemKey] = context;
        return context;
    }

    private async Task<TargetingContext> ForCurrentMemberAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IMemberManager? memberManager = services.GetService<IMemberManager>();
        if (memberManager is null)
        {
            return Anonymous();
        }

        MemberIdentityUser? member = await memberManager.GetCurrentMemberAsync() ?? await MemberFromTokenAsync(memberManager, services);
        if (member is null)
        {
            return Anonymous();
        }

        IList<string> memberGroupNames = await memberManager.GetRolesAsync(member);
        var groups = new List<string>();
        IMemberGroupService? memberGroupService = services.GetService<IMemberGroupService>();
        foreach (string name in memberGroupNames)
        {
            groups.Add(name);
            IMemberGroup? memberGroup = memberGroupService is null ? null : await memberGroupService.GetByNameAsync(name);
            if (memberGroup is not null)
            {
                groups.Add(TargetingIds.For(memberGroup.Key));
            }
        }

        var featureFlagMember = new FeatureFlagMember(member.Key, member.UserName, member.Email, memberGroupNames.ToArray());
        foreach (IFeatureFlagGroupProvider provider in _groupProviders)
        {
            try
            {
                groups.AddRange(await provider.GetGroupIdsForMemberAsync(featureFlagMember, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception,
                    "The Feature Flags group provider {Provider} failed, so the member is in none of its groups for this request.",
                    provider.GetType().FullName);
            }
        }

        return new TargetingContext { UserId = TargetingIds.For(member.Key), Groups = groups.Distinct().ToArray() };
    }

    /// <summary>
    /// The member a Delivery API member token names. Umbraco's member manager reads only the member cookie, so when
    /// the Delivery API's member authorization is on this authenticates the token's scheme itself, and takes only a
    /// principal with the member key claim: backoffice tokens pass the same scheme and must not count as members.
    /// </summary>
    private async Task<MemberIdentityUser?> MemberFromTokenAsync(IMemberManager memberManager, IServiceProvider services)
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null
            || !httpContext.Request.Headers.ContainsKey(HeaderNames.Authorization)
            || services.GetService<IOptionsMonitor<DeliveryApiSettings>>()?.CurrentValue.MemberAuthorizationIsEnabled() != true)
        {
            return null;
        }

        AuthenticateResult result = await httpContext.AuthenticateAsync(MemberTokenScheme);
        return result is { Succeeded: true, Principal: { } principal } && principal.HasClaim(claim => claim.Type == MemberKeyClaim)
            ? await memberManager.GetUserAsync(principal)
            : null;
    }

    private static TargetingContext Anonymous() => new() { UserId = null, Groups = [] };
}

/// <summary>How keys are written wherever targeting compares them, so stored audiences and members always match.</summary>
internal static class TargetingIds
{
    public static string For(Guid key) => key.ToString("D");
}
