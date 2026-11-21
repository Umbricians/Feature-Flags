namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// What the backoffice edits about a flag: whether it is on, and who for. A flag is on for everyone when it is
/// enabled with no audience. An audience is kept while the flag is off, so switching it back on restores it.
/// </summary>
internal sealed record FlagSettings(bool Enabled, FlagAudience? Audience)
{
    public static FlagSettings Off { get; } = new(false, null);

    public FlagState State => !Enabled ? FlagState.Off : Audience is null ? FlagState.Everyone : FlagState.Audience;

    /// <summary>Whether both say the same, ignoring the order of the audience.</summary>
    public bool SameAs(FlagSettings other) =>
        Enabled == other.Enabled
        && (Audience is null ? other.Audience is null : other.Audience is not null && Audience.SameAs(other.Audience));
}

/// <summary>
/// Who a flag is on for: members by key, and groups by id. Microsoft's targeting keeps every group in one list, so group
/// ids are member group keys, member group names or group providers' ids; <c>AudienceGroups</c> says which. An empty
/// audience includes nobody.
/// </summary>
internal sealed record FlagAudience(IReadOnlyList<string> Members, IReadOnlyList<string> Groups)
{
    public static FlagAudience Empty { get; } = new([], []);

    public bool SameAs(FlagAudience other) =>
        Members.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Members)
        && Groups.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Groups);
}

/// <summary>How a flag is evaluated, as editors see it.</summary>
internal enum FlagState
{
    Off,
    Everyone,
    Audience,
}
