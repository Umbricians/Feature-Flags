using Microsoft.Extensions.Options;
using Umbricians.FeatureFlags.Configuration;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// The flag list as the backoffice asks for it: raw query values in, a page or a problem out; and its summary. A
/// <see cref="FlagListRequest.Section"/> of <c>null</c> means every section, an empty one means flags in no section, and
/// any other is that section, whatever its case.
/// </summary>
internal sealed class FlagList
{
    public const int MaxPageSize = 100;

    private readonly FlagCatalog _catalog;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;

    public FlagList(FlagCatalog catalog, IOptionsMonitor<FeatureFlagsOptions> options)
    {
        _catalog = catalog;
        _options = options;
    }

    /// <summary>A page of flags, searched, filtered and sorted across every flag, or why the request can't be answered.</summary>
    public async Task<FlagListResult> PageAsync(FlagListRequest request, CancellationToken cancellationToken)
    {
        if (request.Skip < 0 || request.Take < 1 || request.Take > MaxPageSize)
        {
            return Refused("Invalid page", $"skip must be 0 or more, and take between 1 and {MaxPageSize}.");
        }

        if (!ApiNames.TryParseState(request.State, out FeatureFlagState? state)
            || !ApiNames.TryParseShow(request.Show, out bool undeclared)
            || !ApiNames.TryParseMarks(request.Marks, out FlagMarks marks)
            || !ApiNames.TryParseOrder(request.OrderBy, out FlagQueryOrder order))
        {
            return Refused("Invalid filter", "state, show, marks or orderBy has a value the API doesn't know.");
        }

        var query = new FlagQuery(
            request.Filter,
            state,
            undeclared,
            marks,
            string.IsNullOrEmpty(request.Section) ? null : request.Section,
            NoSectionOnly: request.Section is not null && request.Section.Length == 0,
            string.IsNullOrEmpty(request.Tag) ? null : request.Tag,
            order);

        (IReadOnlyList<FeatureFlag> items, int total) = query.Run(await _catalog.GetAllAsync(cancellationToken), request.Skip, request.Take);
        return new FlagListResult(items, total, null);
    }

    /// <summary>The sections and tags declared flags are in, for the list's filters, and what the list says about the site.</summary>
    public async Task<FlagListSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<FeatureFlag> flags = await _catalog.GetAllAsync(cancellationToken);
        FeatureFlag[] declared = flags.Where(flag => flag.IsDeclared).ToArray();
        return new FlagListSummary(
            declared
                .GroupBy(flag => flag.Section, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, FlagQuery.SectionOrder)
                .Select(group => new FlagSectionCount(group.Key, group.Count()))
                .ToArray(),
            declared.SelectMany(flag => flag.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            declared.Length,
            flags.Count - declared.Length,
            _options.CurrentValue.OverridesAreReadOnly());
    }

    private static FlagListResult Refused(string title, string detail) => new([], 0, new FlagListProblem(title, detail));
}

/// <summary>What the list was asked for, as the query string gave it.</summary>
internal sealed record FlagListRequest(
    int Skip = 0,
    int Take = 25,
    string? Filter = null,
    string? State = null,
    string? Show = null,
    string? Marks = null,
    string? Section = null,
    string? Tag = null,
    string? OrderBy = null);

internal sealed record FlagListProblem(string Title, string Detail);

internal sealed record FlagListResult(IReadOnlyList<FeatureFlag> Items, int Total, FlagListProblem? Problem);

internal sealed record FlagSectionCount(string? Name, int Count);

internal sealed record FlagListSummary(
    IReadOnlyList<FlagSectionCount> Sections,
    IReadOnlyList<string> Tags,
    int Total,
    int Undeclared,
    bool ReadOnly);
