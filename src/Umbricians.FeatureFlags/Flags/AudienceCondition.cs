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

    // Percentages and exclusions can't be edited yet, so a save that has them is a problem.
    public IEnumerable<string> Validate(JsonObject parameters)
    {
        FlagAudience.TryRead(parameters, out string? reason);
        return reason is null ? [] : [reason];
    }

    public string Describe(JsonObject parameters)
    {
        if (FlagAudience.TryRead(parameters, out _) is not { } audience)
        {
            return "An audience with percentages or exclusions";
        }

        string[] parts =
        [
            .. audience.Members.Count > 0 ? [Count(audience.Members.Count, "member")] : Array.Empty<string>(),
            .. audience.Groups.Count > 0 ? [Count(audience.Groups.Count, "group")] : Array.Empty<string>(),
        ];
        return parts.Length == 0 ? "Nobody" : string.Join(" and ", parts);
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
