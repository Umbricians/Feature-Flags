using System.Text.Json.Nodes;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// A flag's settings: whether it is on, and the conditions that say who or when it is on for. A flag is on for everyone
/// when it is enabled with no conditions. Conditions are kept while the flag is off, so switching it back on restores them.
/// </summary>
/// <param name="Enabled">Whether the flag is on at all; when off, its conditions are ignored.</param>
/// <param name="Conditions">What must hold for the flag to be on, as Microsoft's filters name them. None means everyone.</param>
/// <param name="Match">Whether any one condition is enough, or all of them are needed.</param>
public sealed record FlagSettings(bool Enabled, IReadOnlyList<FlagCondition> Conditions, ConditionMatch Match = ConditionMatch.Any)
{
    internal const string TargetingName = "Microsoft.Targeting";
    private const string TargetingShortName = "Targeting";

    /// <summary>Off, with no conditions.</summary>
    public static FlagSettings Off { get; } = new(false, []);

    /// <summary>Settings for an audience: on for the members and groups in it, or for everyone when there is none.</summary>
    /// <param name="enabled">Whether the flag is on.</param>
    /// <param name="audience">Who it is on for; null for everyone.</param>
    public static FlagSettings ForAudience(bool enabled, FlagAudience? audience) =>
        new(enabled, audience is null ? [] : [new FlagCondition(TargetingName, audience.ToParameters())]);

    /// <summary>
    /// Who the flag is on for, when its only condition is a targeting condition the backoffice can read: members by key
    /// and groups by id, with no percentages or exclusions. Otherwise null.
    /// </summary>
    public FlagAudience? Audience =>
        Conditions.Count == 1 && IsTargeting(Conditions[0].Name) ? FlagAudience.TryRead(Conditions[0].Parameters, out _) : null;

    /// <summary>A copy that shares nothing with this one, so a caller can change its condition parameters freely.</summary>
    internal FlagSettings Copy() =>
        new(Enabled, Conditions.Select(condition => new FlagCondition(condition.Name, (JsonObject)condition.Parameters.DeepClone())).ToArray(), Match);

    internal FlagState State =>
        !Enabled ? FlagState.Off
        : Conditions.Count == 0 ? FlagState.Everyone
        : Audience is not null ? FlagState.Audience
        : FlagState.Conditions;

    // Microsoft matches "Targeting" and "Microsoft.Targeting" (and the time window's two names) as the same filter.
    private static bool SameFilter(string a, string b) =>
        string.Equals(ShortName(a), ShortName(b), StringComparison.OrdinalIgnoreCase);

    private static string ShortName(string name) =>
        name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ? name["Microsoft.".Length..] : name;

    internal static bool IsTargeting(string? name) =>
        string.Equals(name, TargetingName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, TargetingShortName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether both say the same: conditions are compared as JSON, in order, except that two audiences are the same
    /// whatever their order.
    /// </summary>
    internal bool SameAs(FlagSettings other)
    {
        if (Enabled != other.Enabled || Conditions.Count != other.Conditions.Count)
        {
            return false;
        }

        if (Conditions.Count > 1 && Match != other.Match)
        {
            return false;
        }

        for (int i = 0; i < Conditions.Count; i++)
        {
            FlagCondition mine = Conditions[i];
            FlagCondition theirs = other.Conditions[i];
            bool same = IsTargeting(mine.Name) && IsTargeting(theirs.Name)
                && FlagAudience.TryRead(mine.Parameters, out _) is { } a && FlagAudience.TryRead(theirs.Parameters, out _) is { } b
                    ? a.SameAs(b)
                    : SameFilter(mine.Name, theirs.Name)
                        && JsonNode.DeepEquals(mine.Parameters, theirs.Parameters);
            if (!same)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>One of Microsoft's filters on a flag, kept exactly as stored.</summary>
/// <param name="Name">The filter's name, such as <c>Microsoft.Targeting</c> or <c>Microsoft.TimeWindow</c>.</param>
/// <param name="Parameters">The filter's parameters as JSON; empty when it has none.</param>
public sealed record FlagCondition(string Name, JsonObject Parameters);

/// <summary>Whether a flag with several conditions needs any one of them to hold, or all of them.</summary>
public enum ConditionMatch
{
    /// <summary>Any one condition holding is enough.</summary>
    Any,

    /// <summary>Every condition must hold.</summary>
    All,
}

/// <summary>
/// Who a flag is on for: members by key, and groups by id. Microsoft's targeting keeps every group in one list, so group
/// ids are member group keys, member group names or group providers' ids; <c>AudienceGroups</c> says which. An empty
/// audience includes nobody.
/// </summary>
/// <param name="Members">The keys of the members it includes.</param>
/// <param name="Groups">The ids of the groups it includes.</param>
public sealed record FlagAudience(IReadOnlyList<string> Members, IReadOnlyList<string> Groups)
{
    /// <summary>An audience that includes nobody.</summary>
    public static FlagAudience Empty { get; } = new([], []);

    internal bool SameAs(FlagAudience other) =>
        Members.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Members)
        && Groups.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Groups);

    /// <summary>Microsoft's targeting parameters for this audience: everyone in it, and nobody else.</summary>
    internal JsonObject ToParameters()
    {
        var groups = new JsonArray();
        foreach (string group in Groups)
        {
            groups.Add(new JsonObject { ["Name"] = group, ["RolloutPercentage"] = 100 });
        }

        return new JsonObject
        {
            ["Audience"] = new JsonObject
            {
                ["Users"] = new JsonArray(Members.Select(m => (JsonNode?)m).ToArray()),
                ["Groups"] = groups,
                ["DefaultRolloutPercentage"] = 0,
            },
        };
    }

    /// <summary>
    /// The audience targeting parameters amount to, or null with the reason when they say more than members and groups:
    /// a percentage of everyone or of a group, or exclusions.
    /// </summary>
    internal static FlagAudience? TryRead(JsonObject parameters, out string? reason)
    {
        reason = null;
        JsonObject? audience = Child(parameters, "Audience") as JsonObject;

        if (Percentage(Child(audience, "DefaultRolloutPercentage")) is not (null or 0))
        {
            reason = "It is on for a percentage of everyone, which the backoffice can't edit yet.";
            return null;
        }

        if (Child(audience, "Exclusion") is JsonObject exclusion
            && (Child(exclusion, "Users") is JsonArray { Count: > 0 } || Child(exclusion, "Groups") is JsonArray { Count: > 0 }))
        {
            reason = "It excludes members or groups, which the backoffice can't edit yet.";
            return null;
        }

        var groups = new List<string>();
        if (Child(audience, "Groups") is JsonArray groupNodes)
        {
            foreach (JsonNode? node in groupNodes)
            {
                if (Percentage(Child(node as JsonObject, "RolloutPercentage")) is not (null or 100))
                {
                    reason = "It is on for a percentage of a group, which the backoffice can't edit yet.";
                    return null;
                }

                if (Text(Child(node as JsonObject, "Name")) is { } name)
                {
                    groups.Add(name);
                }
            }
        }

        var members = new List<string>();
        if (Child(audience, "Users") is JsonArray users)
        {
            members.AddRange(users.Select(Text).OfType<string>());
        }

        return new FlagAudience(members, groups);
    }

    // Microsoft reads configuration keys without regard to case, so these do too.
    private static JsonNode? Child(JsonObject? parent, string name) =>
        parent?.FirstOrDefault(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    // Hand-written JSON may hold an id as a number or a boolean, such as "Users": [1001]; its JSON text is the id.
    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        string? text = value.TryGetValue(out string? s) ? s : value.GetValueKind() is System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? value.ToJsonString() : null;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    // A number, or text that reads as one, whichever way the JSON value holds it.
    private static double? Percentage(JsonNode? node) => node is JsonValue value
        && (value.GetValueKind() == System.Text.Json.JsonValueKind.Number || value.TryGetValue(out string? _))
        && double.TryParse(
            value.GetValueKind() == System.Text.Json.JsonValueKind.Number ? value.ToJsonString() : value.GetValue<string>(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double number)
            ? number
            : null;
}

/// <summary>How a flag is evaluated, as editors see it.</summary>
internal enum FlagState
{
    Off,
    Everyone,
    Audience,
    Conditions,
}
