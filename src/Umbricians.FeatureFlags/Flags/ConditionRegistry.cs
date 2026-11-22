using System.Text.Json.Nodes;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// The conditions registered for the backoffice, found by the filter name a flag uses. Decides what the backoffice
/// can edit: a flag is editable when every one of its conditions is registered.
/// </summary>
internal sealed class ConditionRegistry
{
    private readonly IFeatureFlagCondition[] _conditions;

    public ConditionRegistry(IEnumerable<IFeatureFlagCondition> conditions) => _conditions = conditions.ToArray();

    /// <summary>The registered conditions, in the order they were registered.</summary>
    public IReadOnlyList<IFeatureFlagCondition> All => _conditions;

    /// <summary>
    /// The condition for a filter name as a flag writes it, or null when nobody registered one. Names match the way
    /// Microsoft.FeatureManagement matches filter names to filters: ignoring case, and a name without a namespace
    /// matches a registered name that ends with it after a dot (<c>Targeting</c> matches <c>Microsoft.Targeting</c>).
    /// (Microsoft strips a trailing "Filter" from a filter's class name when it has no alias, but not from the name a flag
    /// uses, so <c>TargetingFilter</c> in a flag matches nothing; neither does it here.)
    /// An exact match wins over a shorter one.
    /// </summary>
    public IFeatureFlagCondition? Find(string? filterName)
    {
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return null;
        }

        return _conditions.FirstOrDefault(condition => string.Equals(condition.Name, filterName, StringComparison.OrdinalIgnoreCase))
            ?? _conditions.FirstOrDefault(condition => Matches(condition.Name, filterName));
    }

    /// <summary>Why the backoffice can't edit these settings's conditions, or null when it can.</summary>
    public string? NotEditableReason(FlagSettings settings)
    {
        foreach (FlagCondition condition in settings.Conditions)
        {
            if (Find(condition.Name) is null)
            {
                return $"It uses the '{condition.Name}' filter, which no condition is registered for, so the backoffice can't edit it.";
            }

            // The audience editor can't yet edit percentages or exclusions.
            if (FlagSettings.IsTargeting(condition.Name))
            {
                FlagAudience.TryRead(condition.Parameters, out string? reason);
                if (reason is not null)
                {
                    return reason;
                }
            }
        }

        return null;
    }

    /// <summary>Whether every condition of the settings is registered.</summary>
    public bool AllRegistered(FlagSettings settings) => settings.Conditions.All(condition => Find(condition.Name) is not null);

    /// <summary>
    /// The problems with every condition's parameters, each named for the condition it is about. A condition nobody
    /// registered is a problem too, since the backoffice and code can't vouch for what it saves.
    /// </summary>
    public IReadOnlyList<string> Validate(FlagSettings settings)
    {
        var problems = new List<string>();
        foreach (FlagCondition condition in settings.Conditions)
        {
            IFeatureFlagCondition? registered = Find(condition.Name);
            if (registered is null)
            {
                problems.Add($"The '{condition.Name}' filter has no registered condition, so it can't be saved here.");
                continue;
            }

            string[] found;
            try
            {
                found = registered.Validate((JsonObject)condition.Parameters.DeepClone()).Where(problem => !string.IsNullOrWhiteSpace(problem)).ToArray();
            }
            catch (Exception exception)
            {
                found = [$"It couldn't be checked: {exception.Message}"];
            }

            problems.AddRange(found.Select(problem => $"{registered.DisplayName}: {problem}"));
        }

        return problems;
    }

    /// <summary>
    /// A one-line summary of the settings' conditions for the list: each registered condition's own description, joined
    /// with "; ". Null when there are none to describe.
    /// </summary>
    public string? Summarize(FlagSettings settings)
    {
        var parts = new List<string>();
        foreach (FlagCondition condition in settings.Conditions)
        {
            if (Find(condition.Name) is not { } registered)
            {
                continue;
            }

            try
            {
                string text = registered.Describe((JsonObject)condition.Parameters.DeepClone());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text.Trim());
                }
            }
            catch (Exception)
            {
                // A summary is a nicety; a condition that can't describe itself is left out rather than breaking the list.
            }
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    // Microsoft's IsMatchingName: a name with a dot must match exactly (Find has already tried that), and one without
    // matches the last segment of the registered name, so "Culture" finds "Contoso.Culture" but "Product.Culture" does
    // not find "Contoso.Product.Culture".
    private static bool Matches(string registered, string filterName) =>
        !filterName.Contains('.') && registered.EndsWith("." + filterName, StringComparison.OrdinalIgnoreCase);
}
