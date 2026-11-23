namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Searches, filters, sorts and pages the flag list, across every flag. Undeclared flags are a list of their own, only
/// shown when asked for, since they have no effect. Every filter given must match.
/// </summary>
internal sealed record FlagQuery(
    string? Filter = null,
    FeatureFlagState? State = null,
    bool Undeclared = false,
    FlagMarks Marks = FlagMarks.None,
    string? Section = null,
    bool NoSectionOnly = false,
    string? Tag = null,
    FlagQueryOrder OrderBy = FlagQueryOrder.Name)
{
    /// <summary>Sections in the order the list shows them: no section last, then by name, ignoring case.</summary>
    public static IComparer<string?> SectionOrder { get; } = Comparer<string?>.Create((a, b) =>
        a is null || b is null ? (a is null).CompareTo(b is null) : StringComparer.OrdinalIgnoreCase.Compare(a, b));

    public (IReadOnlyList<FeatureFlag> Items, int Total) Run(IEnumerable<FeatureFlag> flags, int skip, int take)
    {
        IEnumerable<FeatureFlag> matching = flags.Where(Matches);
        matching = OrderBy switch
        {
            FlagQueryOrder.Updated => matching
                .OrderByDescending(flag => flag.Override?.UpdatedUtc ?? DateTime.MinValue)
                .ThenBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
            FlagQueryOrder.Section => matching
                .OrderBy(flag => flag.Section, SectionOrder)
                .ThenBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
            _ => matching.OrderBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
        };

        FeatureFlag[] all = matching.ToArray();
        return (all.Skip(Math.Max(0, skip)).Take(Math.Max(0, take)).ToArray(), all.Length);
    }

    private bool HasMarks(FeatureFlag flag) =>
        (!Marks.HasFlag(FlagMarks.Changed) || flag.Override is not null)
        && (!Marks.HasFlag(FlagMarks.DiffersFromDefault) || flag.DiffersFromDefault)
        && (!Marks.HasFlag(FlagMarks.DeclaredTwice) || flag.DeclaredTwice);

    private bool Matches(FeatureFlag flag)
    {
        if (flag.IsDeclared == Undeclared || !HasMarks(flag) || (State is not null && flag.State != State))
        {
            return false;
        }

        if (NoSectionOnly ? flag.Section is not null
            : Section is not null && !string.Equals(flag.Section, Section, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Tag is not null && !flag.Tags.Contains(Tag, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        string text = Filter.Trim();
        return flag.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || flag.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    }
}

/// <summary>Notes on a flag the list can be narrowed to; a flag must have every one asked for.</summary>
[Flags]
internal enum FlagMarks
{
    None = 0,

    /// <summary>An editor has changed it on this site.</summary>
    Changed = 1,

    /// <summary>Its setting here differs from its default.</summary>
    DiffersFromDefault = 2,

    /// <summary>Code and configuration both declare it.</summary>
    DeclaredTwice = 4,
}

internal enum FlagQueryOrder
{
    Name,
    Section,
    Updated,
}
