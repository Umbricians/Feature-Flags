namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Searches, filters, sorts and pages the flag list, across every flag. Undeclared flags are only listed when asked
/// for, since they have no effect.
/// </summary>
internal sealed record FlagQuery(
    string? Filter = null,
    FlagDisplayState? State = null,
    FlagQueryShow Show = FlagQueryShow.Declared,
    string? Group = null,
    bool UngroupedOnly = false,
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
            FlagQueryOrder.Group => matching
                .OrderBy(flag => flag.Details.Group is null ? 1 : 0)
                .ThenBy(flag => flag.Details.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
            _ => matching.OrderBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase),
        };

        CatalogFlag[] all = matching.ToArray();
        return (all.Skip(Math.Max(0, skip)).Take(Math.Max(0, take)).ToArray(), all.Length);
    }

    private bool Matches(CatalogFlag flag)
    {
        bool shown = Show switch
        {
            FlagQueryShow.Undeclared => !flag.IsDeclared,
            FlagQueryShow.Changed => flag.IsDeclared && flag.Override is not null,
            FlagQueryShow.DiffersFromDefault => flag.IsDeclared && flag.DiffersFromDefault,
            FlagQueryShow.DeclaredTwice => flag.IsDeclared && flag.DeclaredTwice,
            _ => flag.IsDeclared,
        };

        if (!shown || (State is not null && flag.State != State))
        {
            return false;
        }

        if (UngroupedOnly ? flag.Details.Group is not null
            : Group is not null && !string.Equals(flag.Details.Group, Group, StringComparison.OrdinalIgnoreCase))
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

internal enum FlagQueryShow
{
    Declared,
    Changed,
    DiffersFromDefault,
    DeclaredTwice,
    Undeclared,
}

internal enum FlagQueryOrder
{
    Name,
    Group,
    Updated,
}
