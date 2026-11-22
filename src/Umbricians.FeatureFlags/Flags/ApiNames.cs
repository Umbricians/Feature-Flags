using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>The words the API uses for a flag's state and for the list's filters, each written once, both ways.</summary>
internal static class ApiNames
{
    private static readonly Dictionary<FeatureFlagState, string> States = new()
    {
        [FeatureFlagState.Off] = "off",
        [FeatureFlagState.Everyone] = "everyone",
        [FeatureFlagState.Audience] = "audience",
        [FeatureFlagState.Conditions] = "conditions",
        [FeatureFlagState.Custom] = "custom",
    };

    private static readonly Dictionary<string, FlagMarks> Marks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["changed"] = FlagMarks.Changed,
        ["differs"] = FlagMarks.DiffersFromDefault,
        ["twice"] = FlagMarks.DeclaredTwice,
    };

    private static readonly Dictionary<string, FlagQueryOrder> Orders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = FlagQueryOrder.Name,
        ["section"] = FlagQueryOrder.Section,
        ["updated"] = FlagQueryOrder.Updated,
    };

    public static string State(FeatureFlagState state) => States[state];

    /// <summary>How a flag with several conditions matches them, as the API says it.</summary>
    public static string Match(ConditionMatch match) => match == ConditionMatch.All ? "all" : "any";

    /// <summary>A match from its name; false for a name the API doesn't know.</summary>
    public static bool TryParseMatch(string? name, out ConditionMatch match)
    {
        match = ConditionMatch.Any;
        if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
        {
            match = ConditionMatch.All;
            return true;
        }

        return string.Equals(name, "any", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where a flag is declared, as the API says it.</summary>
    public static string DeclaredIn(FeatureFlagDeclaredIn declaredIn) => declaredIn switch
    {
        FeatureFlagDeclaredIn.Configuration => "configuration",
        FeatureFlagDeclaredIn.FlagSource => "flagSource",
        _ => "code",
    };

    /// <summary>What an audience group is, as the API says it.</summary>
    public static string GroupKind(AudienceGroupKind kind) => kind switch
    {
        AudienceGroupKind.MemberGroup => "memberGroup",
        AudienceGroupKind.NamedMemberGroup => "namedMemberGroup",
        AudienceGroupKind.ProviderGroup => "providerGroup",
        _ => "unknown",
    };

    /// <summary>A state from its name; true with null for none given.</summary>
    public static bool TryParseState(string? name, out FeatureFlagState? state)
    {
        state = null;
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        foreach ((FeatureFlagState value, string stateName) in States)
        {
            if (string.Equals(stateName, name, StringComparison.OrdinalIgnoreCase))
            {
                state = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Marks from a comma-separated list, such as <c>changed,twice</c>.</summary>
    public static bool TryParseMarks(string? names, out FlagMarks marks)
    {
        marks = FlagMarks.None;
        foreach (string name in (names ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Marks.TryGetValue(name, out FlagMarks mark))
            {
                return false;
            }

            marks |= mark;
        }

        return true;
    }

    /// <summary>Which list to show: <c>declared</c> (the default) or <c>undeclared</c>.</summary>
    public static bool TryParseShow(string? name, out bool undeclared)
    {
        undeclared = string.Equals(name, "undeclared", StringComparison.OrdinalIgnoreCase);
        return undeclared || string.IsNullOrEmpty(name) || string.Equals(name, "declared", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An order from its name; <see cref="FlagQueryOrder.Name"/> for none given.</summary>
    public static bool TryParseOrder(string? name, out FlagQueryOrder order)
    {
        order = FlagQueryOrder.Name;
        return string.IsNullOrEmpty(name) || Orders.TryGetValue(name, out order);
    }
}
