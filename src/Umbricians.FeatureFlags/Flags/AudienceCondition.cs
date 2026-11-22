using System.Text.Json.Nodes;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// The package's own condition: an audience of members and groups, evaluated by Microsoft's targeting filter. It is
/// registered through the same contract as a developer's condition.
/// </summary>
internal sealed class AudienceCondition : IFeatureFlagCondition
{
    public string Name => FlagSettings.TargetingName;

    public string DisplayName => "Audience";

    // A percentage that isn't a whole number from 0 to 100, or a list of the wrong shape, is a problem.
    public IEnumerable<string> Validate(JsonObject parameters)
    {
        FlagAudience.TryRead(parameters, out string? reason);
        return reason is null ? [] : [reason];
    }

    public string Describe(JsonObject parameters)
    {
        if (FlagAudience.TryRead(parameters, out _) is not { } audience)
        {
            return "An audience that can't be read";
        }

        string[] included =
        [
            .. audience.Members.Count > 0 ? [Count(audience.Members.Count, "member")] : Array.Empty<string>(),
            .. audience.Groups.Count > 0 ? [GroupsText(audience)] : Array.Empty<string>(),
            .. audience.DefaultPercentage > 0 ? [$"{audience.DefaultPercentage}% of everyone else"] : Array.Empty<string>(),
        ];
        string[] excluded =
        [
            .. audience.ExcludedMembers.Count > 0 ? [Count(audience.ExcludedMembers.Count, "member")] : Array.Empty<string>(),
            .. audience.ExcludedGroups.Count > 0 ? [Count(audience.ExcludedGroups.Count, "group")] : Array.Empty<string>(),
        ];
        if (included.Length == 0)
        {
            return "Nobody";
        }

        string who = string.Join(" and ", included);
        return excluded.Length == 0 ? who : $"{who}, except {string.Join(" and ", excluded)}";
    }

    // "2 groups", or "2 groups (1 in part)" when some are only a percentage of the group.
    private static string GroupsText(FlagAudience audience)
    {
        int partial = audience.Groups.Count(group => audience.PercentageOf(group) < 100);
        string text = Count(audience.Groups.Count, "group");
        return partial == 0 ? text : $"{text} ({partial} in part)";
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
