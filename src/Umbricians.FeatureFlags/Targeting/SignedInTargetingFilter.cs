using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

namespace Umbricians.FeatureFlags.Targeting;

/// <summary>
/// Microsoft's targeting filter, under the same <c>Microsoft.Targeting</c> name, except that a visitor with no user id
/// is in no audience. Microsoft hashes every visitor without an id into one bucket, so "10% of everyone else" would turn
/// a flag on for all of them or none; here they are never included, whatever the percentages. Everything else is
/// Microsoft's own evaluation. A site's own <see cref="ITargetingContextAccessor"/> that supplies ids keeps working.
/// </summary>
[FilterAlias("Microsoft.Targeting")]
internal sealed class SignedInTargetingFilter : IFeatureFilter, IFilterParametersBinder
{
    private readonly TargetingFilter _microsoft;
    private readonly ITargetingContextAccessor _accessor;

    public SignedInTargetingFilter(ITargetingContextAccessor accessor, IOptions<TargetingEvaluationOptions> options, ILoggerFactory loggerFactory)
    {
        _accessor = accessor;
        _microsoft = new TargetingFilter(accessor, options, loggerFactory);
    }

    public object BindParameters(IConfiguration parameters) => _microsoft.BindParameters(parameters);

    public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        // Microsoft's filter asks the accessor again; the member accessor caches per request, so it sees the same visitor.
        ITargetingContext targeting = await _accessor.GetContextAsync();
        return !string.IsNullOrWhiteSpace(targeting?.UserId) && await _microsoft.EvaluateAsync(context);
    }
}
