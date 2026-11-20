using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Targeting;

/// <summary>
/// Tells Microsoft.FeatureManagement's targeting who the signed-in member is: their key as the user id, and as groups
/// the key and name of each of their member groups plus the groups every <see cref="IFeatureFlagGroupProvider"/> puts
/// them in. A visitor who isn't signed in has no user id and no groups. Worked out once per request.
/// </summary>
/// <remarks>
/// A site that identifies people another way registers its own <see cref="ITargetingContextAccessor"/>, which wins.
/// </remarks>
internal sealed class MemberTargetingContextAccessor : ITargetingContextAccessor
{
    private static readonly object ItemKey = new();

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
        MemberIdentityUser? member = memberManager is null ? null : await memberManager.GetCurrentMemberAsync();
        if (memberManager is null || member is null)
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

    private static TargetingContext Anonymous() => new() { UserId = null, Groups = [] };
}

/// <summary>How keys are written wherever targeting compares them, so stored audiences and members always match.</summary>
internal static class TargetingIds
{
    public static string For(Guid key) => key.ToString("D");
}
