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
/// Who a flag is on for: members by key, and groups by id. Microsoft's targeting keeps every group in one list, so a
/// group id is either a member group's key or another group: a group provider's id, or a member group named in
/// appsettings. An empty audience includes nobody.
/// </summary>
internal sealed record FlagAudience(IReadOnlyList<string> Members, IReadOnlyList<string> Groups)
{
    public static FlagAudience Empty { get; } = new([], []);

    /// <summary>The groups that are member groups, by key, as the backoffice's member group picker works with them.</summary>
    public IReadOnlyList<string> MemberGroupKeys => Groups.Where(IsMemberGroupKey).ToArray();

    /// <summary>Every other group: group providers' ids, and member groups named in appsettings.</summary>
    public IReadOnlyList<string> OtherGroups => Groups.Where(id => !IsMemberGroupKey(id)).ToArray();

    /// <summary>An audience from the backoffice, which picks member groups and other groups separately.</summary>
    public static FlagAudience Of(IEnumerable<string> members, IEnumerable<string> memberGroupKeys, IEnumerable<string> otherGroups) =>
        new(members.ToArray(), memberGroupKeys.Concat(otherGroups).ToArray());

    private static bool IsMemberGroupKey(string id) => Guid.TryParse(id, out _);

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
