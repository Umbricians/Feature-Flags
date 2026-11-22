using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

namespace Umbricians.FeatureFlags.Targeting;

/// <summary>
/// Microsoft's targeting evaluation with one change: a visitor with no user id is never in a percentage. Microsoft hashes
/// every visitor without an id into one bucket, so "10% of everyone else" would turn a flag on for all of them or none.
/// Here the percentage of everyone else, and a group at less than 100%, include nobody without an id. Listed members,
/// groups at 100% (a site's own accessor may supply groups but no id) and exclusions all work as in Microsoft's.
/// Everything else is Microsoft's own evaluation.
/// </summary>
internal static class SignedInTargeting
{
    internal const string Alias = "Microsoft.Targeting";

    /// <summary>Evaluates for a targeting context, with the percentages suppressed when it has no user id.</summary>
    public static Task<bool> EvaluateAsync(ContextualTargetingFilter microsoft, FeatureFilterEvaluationContext context, ITargetingContext targeting)
    {
        if (!string.IsNullOrWhiteSpace(targeting.UserId))
        {
            return microsoft.EvaluateAsync(context, targeting);
        }

        var settings = context.Settings as TargetingFilterSettings ?? (TargetingFilterSettings)microsoft.BindParameters(context.Parameters);
        var withoutPercentages = new FeatureFilterEvaluationContext
        {
            FeatureName = context.FeatureName,
            Parameters = context.Parameters,
            Settings = new TargetingFilterSettings
            {
                Audience = settings.Audience is null
                    ? null!
                    : new Audience
                    {
                        Users = settings.Audience.Users,
                        Groups = settings.Audience.Groups?.Where(group => group.RolloutPercentage >= 100).ToList(),
                        DefaultRolloutPercentage = 0,
                        Exclusion = settings.Audience.Exclusion,
                    },
            },
        };
        return microsoft.EvaluateAsync(withoutPercentages, targeting);
    }
}

/// <summary>
/// Takes the place of Microsoft's <c>TargetingFilter</c> under the same <c>Microsoft.Targeting</c> name; see
/// <see cref="SignedInTargeting"/>. A site's own <see cref="ITargetingContextAccessor"/> keeps working.
/// </summary>
[FilterAlias(SignedInTargeting.Alias)]
internal sealed class SignedInTargetingFilter : IFeatureFilter, IFilterParametersBinder
{
    private readonly ContextualTargetingFilter _microsoft;
    private readonly ITargetingContextAccessor _accessor;
    private readonly ILogger _logger;

    public SignedInTargetingFilter(ITargetingContextAccessor accessor, IOptions<TargetingEvaluationOptions> options, ILoggerFactory loggerFactory)
    {
        _accessor = accessor;
        _microsoft = new ContextualTargetingFilter(options, loggerFactory);
        _logger = loggerFactory.CreateLogger<SignedInTargetingFilter>();
    }

    public object BindParameters(IConfiguration parameters) => _microsoft.BindParameters(parameters);

    public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ITargetingContext? targeting = await _accessor.GetContextAsync();
        if (targeting is null)
        {
            _logger.LogWarning("No targeting context available for targeting evaluation.");
            return false;
        }

        return await SignedInTargeting.EvaluateAsync(_microsoft, context, targeting);
    }
}

/// <summary>
/// Takes the place of Microsoft's <c>ContextualTargetingFilter</c>, which code uses when it passes its own targeting context
/// to <c>IsEnabledAsync(name, context)</c>, with the same rule about visitors with no user id.
/// </summary>
[FilterAlias(SignedInTargeting.Alias)]
internal sealed class SignedInContextualTargetingFilter : IContextualFeatureFilter<ITargetingContext>, IFilterParametersBinder
{
    private readonly ContextualTargetingFilter _microsoft;

    public SignedInContextualTargetingFilter(IOptions<TargetingEvaluationOptions> options, ILoggerFactory loggerFactory) =>
        _microsoft = new ContextualTargetingFilter(options, loggerFactory);

    public object BindParameters(IConfiguration parameters) => _microsoft.BindParameters(parameters);

    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context, ITargetingContext targetingContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetingContext);
        return SignedInTargeting.EvaluateAsync(_microsoft, context, targetingContext);
    }
}
