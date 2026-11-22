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

    /// <summary>Settings for an audience: on for those in it, or for everyone when there is none.</summary>
    /// <param name="enabled">Whether the flag is on.</param>
    /// <param name="audience">Who it is on for; null for everyone.</param>
    public static FlagSettings ForAudience(bool enabled, FlagAudience? audience) =>
        new(enabled, audience is null ? [] : [new FlagCondition(TargetingName, audience.ToParameters())]);

    /// <summary>
    /// Who the flag is on for, when its only condition is a targeting condition the backoffice can read: members, groups,
    /// percentages and exclusions. Otherwise null.
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
/// Who a flag is on for: members by key, and groups by id, with a percentage of everyone else and of each group, and
/// members and groups left out. Microsoft's targeting keeps every group in one list, so group ids are member group keys,
/// member group names or group providers' ids; <c>AudienceGroups</c> says which. An empty audience includes nobody.
/// Percentages are of members only: Microsoft hashes each member's id, so the same member is always in or out.
/// </summary>
/// <param name="Members">The keys of the members it includes.</param>
/// <param name="Groups">The ids of the groups it includes.</param>
public sealed record FlagAudience(IReadOnlyList<string> Members, IReadOnlyList<string> Groups)
{
    /// <summary>An audience that includes nobody.</summary>
    public static FlagAudience Empty { get; } = new([], []);

    /// <summary>The percentage, 0 to 100, of every member not otherwise included. 0 by default.</summary>
    public int DefaultPercentage { get; init; }

    /// <summary>The percentage of a group that is included, by group id. A group not listed is included in full.</summary>
    public IReadOnlyDictionary<string, int> GroupPercentages { get; init; } = new Dictionary<string, int>();

    /// <summary>The keys of members left out, even when they are in a group or the percentage of everyone.</summary>
    public IReadOnlyList<string> ExcludedMembers { get; init; } = [];

    /// <summary>The ids of groups left out, even when a member is also in an included group.</summary>
    public IReadOnlyList<string> ExcludedGroups { get; init; } = [];

    /// <summary>The percentage of a group that is included: 100 unless <see cref="GroupPercentages"/> says otherwise.</summary>
    public int PercentageOf(string group) =>
        GroupPercentages.FirstOrDefault(pair => string.Equals(pair.Key, group, StringComparison.OrdinalIgnoreCase)) is { Key: not null } found
            ? found.Value
            : 100;

    internal bool SameAs(FlagAudience other) =>
        Members.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Members)
        && Groups.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Groups)
        && DefaultPercentage == other.DefaultPercentage
        && Groups.All(group => PercentageOf(group) == other.PercentageOf(group))
        && ExcludedMembers.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.ExcludedMembers)
        && ExcludedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.ExcludedGroups);

    /// <summary>Microsoft's targeting parameters for this audience.</summary>
    internal JsonObject ToParameters()
    {
        var groups = new JsonArray();
        foreach (string group in Groups)
        {
            groups.Add(new JsonObject { ["Name"] = group, ["RolloutPercentage"] = PercentageOf(group) });
        }

        var audience = new JsonObject
        {
            ["Users"] = new JsonArray(Members.Select(m => (JsonNode?)m).ToArray()),
            ["Groups"] = groups,
            ["DefaultRolloutPercentage"] = DefaultPercentage,
        };

        if (ExcludedMembers.Count > 0 || ExcludedGroups.Count > 0)
        {
            audience["Exclusion"] = new JsonObject
            {
                ["Users"] = new JsonArray(ExcludedMembers.Select(m => (JsonNode?)m).ToArray()),
                ["Groups"] = new JsonArray(ExcludedGroups.Select(g => (JsonNode?)g).ToArray()),
            };
        }

        return new JsonObject { ["Audience"] = audience };
    }

    /// <summary>
    /// The audience targeting parameters amount to, or null with the reason when they can't be read as one: a percentage
    /// that isn't a whole number from 0 to 100, or a list of the wrong shape. Numbers may be written as text, as
    /// configuration gives them, and keys may be in any case, as Microsoft reads them.
    /// </summary>
    internal static FlagAudience? TryRead(JsonObject parameters, out string? reason)
    {
        reason = null;
        JsonObject? audience = Child(parameters, "Audience") as JsonObject;

        if (!TryPercentage(Child(audience, "DefaultRolloutPercentage"), 0, "The percentage of everyone else", out int defaultPercentage, out reason))
        {
            return null;
        }

        var groups = new List<string>();
        var percentages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (Child(audience, "Groups") is JsonArray groupNodes)
        {
            foreach (JsonNode? node in groupNodes)
            {
                // Microsoft binds a missing percentage as 0, so a group without one includes nobody.
                if (Text(Child(node as JsonObject, "Name")) is not { } name)
                {
                    continue;
                }

                if (!TryPercentage(Child(node as JsonObject, "RolloutPercentage"), 0, $"The percentage of the group '{name}'", out int percentage, out reason))
                {
                    return null;
                }

                groups.Add(name);
                percentages.TryAdd(name, percentage);
            }
        }

        var members = new List<string>();
        if (Child(audience, "Users") is JsonArray users)
        {
            members.AddRange(users.Select(Text).OfType<string>());
        }

        JsonObject? exclusion = Child(audience, "Exclusion") as JsonObject;
        return new FlagAudience(members, groups)
        {
            DefaultPercentage = defaultPercentage,
            GroupPercentages = percentages.Where(pair => pair.Value != 100).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            ExcludedMembers = Child(exclusion, "Users") is JsonArray excludedUsers ? excludedUsers.Select(Text).OfType<string>().ToArray() : [],
            ExcludedGroups = Child(exclusion, "Groups") is JsonArray excludedGroups ? excludedGroups.Select(Text).OfType<string>().ToArray() : [],
        };
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

    // A percentage written as a number or as text that reads as one; missing means the fallback. Anything else, or a
    // number that isn't a whole number from 0 to 100, is why the audience can't be read.
    private static bool TryPercentage(JsonNode? node, int fallback, string what, out int percentage, out string? reason)
    {
        percentage = fallback;
        reason = null;
        if (node is null)
        {
            return true;
        }

        string? text = node is JsonValue value
            ? value.GetValueKind() == System.Text.Json.JsonValueKind.Number ? value.ToJsonString() : value.TryGetValue(out string? s) ? s : null
            : null;
        if (text is null || !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number))
        {
            reason = $"{what} isn't a number.";
            return false;
        }

        if (number is < 0 or > 100 || number != Math.Floor(number))
        {
            reason = $"{what} is {text}, and must be a whole number from 0 to 100.";
            return false;
        }

        percentage = (int)number;
        return true;
    }
}

/// <summary>How a flag is evaluated, as editors see it.</summary>
internal enum FlagState
{
    Off,
    Everyone,
    Audience,
    Conditions,
}
