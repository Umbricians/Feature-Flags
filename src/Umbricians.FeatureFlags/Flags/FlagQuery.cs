namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Searches, filters, sorts and pages the flag list, across every flag. Undeclared flags are a list of their own, only
/// shown when asked for, since they have no effect. Every filter given must match.
/// </summary>
internal sealed record FlagQuery(
    string? Filter = null,
    FlagDisplayState? State = null,
    bool Undeclared = false,
    FlagMarks Marks = FlagMarks.None,
    string? Section = null,
    bool NoSectionOnly = false,
    string? Tag = null,
    FlagQueryOrder OrderBy = FlagQueryOrder.Name)
{
    public (IReadOnlyList<CatalogFlag> Items, int Total) Run(IEnumerable<CatalogFlag> flags, int skip, int take)
    {
        IEnumerable<CatalogFlag> matching = flags.Where(Matches);
        matching = OrderBy switch
        {
            FlagQueryOrder.Updated => matching
                .OrderByDescending(flag => flag.Override?.UpdatedUtc ?? DateTime.MinValue)
                .ThenBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
            FlagQueryOrder.Section => matching
                .OrderBy(flag => flag.Details.Section is null ? 1 : 0)
                .ThenBy(flag => flag.Details.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
            _ => matching.OrderBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
        };

        CatalogFlag[] all = matching.ToArray();
        return (all.Skip(Math.Max(0, skip)).Take(Math.Max(0, take)).ToArray(), all.Length);
    }

    private bool HasMarks(CatalogFlag flag) =>
        (!Marks.HasFlag(FlagMarks.Changed) || flag.Override is not null)
        && (!Marks.HasFlag(FlagMarks.DiffersFromDefault) || flag.DiffersFromDefault)
        && (!Marks.HasFlag(FlagMarks.DeclaredTwice) || flag.DeclaredTwice);

    private bool Matches(CatalogFlag flag)
    {
        if (flag.IsDeclared == Undeclared || !HasMarks(flag) || (State is not null && flag.State != State))
        {
            return false;
        }

        if (NoSectionOnly ? flag.Details.Section is not null
            : Section is not null && !string.Equals(flag.Details.Section, Section, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Tag is not null && !flag.Details.Tags.Contains(Tag, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        string text = Filter.Trim();
        return flag.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || flag.Details.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
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
