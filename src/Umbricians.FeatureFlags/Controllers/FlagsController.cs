using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Web.Common.Routing;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Security;

namespace Umbricians.FeatureFlags.Controllers;

/// <summary>
/// Reads flags for the backoffice. Flag names go in the query string, never the path, since they may hold dots,
/// slashes and spaces.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Feature Flags")]
[BackOfficeRoute("umbriciansfeatureflags/api/v{version:apiVersion}")]
[FeatureFlagsPermission(Constants.Permissions.View)]
internal sealed class FlagsController : FeatureFlagsControllerBase
{
    private const int MaxPageSize = 100;

    private readonly FlagCatalog _catalog;
    private readonly FlagResponses _responses;
    private readonly IEnumerable<IFeatureFlagGroupProvider> _groupProviders;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;

    public FlagsController(
        FlagCatalog catalog,
        FlagResponses responses,
        IEnumerable<IFeatureFlagGroupProvider> groupProviders,
        IOptionsMonitor<FeatureFlagsOptions> options)
    {
        _catalog = catalog;
        _responses = responses;
        _groupProviders = groupProviders;
        _options = options;
    }

    /// <summary>A page of flags, searched, filtered and sorted across every flag.</summary>
    /// <param name="skip">How many matching flags to skip.</param>
    /// <param name="take">How many to return, at most 100.</param>
    /// <param name="filter">Text the name or description contains.</param>
    /// <param name="state"><c>off</c>, <c>everyone</c>, <c>audience</c> or <c>custom</c>.</param>
    /// <param name="show"><c>declared</c> (the default), <c>changed</c>, <c>differs</c>, <c>twice</c> or <c>undeclared</c>.</param>
    /// <param name="group">A group's name, or an empty value for flags in no group.</param>
    /// <param name="tag">A tag.</param>
    /// <param name="orderBy"><c>name</c> (the default), <c>group</c> or <c>updated</c>, most recent first.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    [HttpGet("flags")]
    [ProducesResponseType<PagedViewModel<FlagListItemResponseModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Flags(
        int skip = 0,
        int take = 25,
        string? filter = null,
        string? state = null,
        string? show = null,
        string? group = null,
        string? tag = null,
        string? orderBy = null,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take < 1 || take > MaxPageSize)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid page", Detail = $"skip must be 0 or more, and take between 1 and {MaxPageSize}." });
        }

        FlagDisplayState? stateFilter = state?.ToLowerInvariant() switch
        {
            null or "" => null,
            "off" => FlagDisplayState.Off,
            "everyone" => FlagDisplayState.Everyone,
            "audience" => FlagDisplayState.Audience,
            "custom" => FlagDisplayState.Custom,
            _ => (FlagDisplayState)(-1),
        };
        FlagQueryShow? showFilter = show?.ToLowerInvariant() switch
        {
            null or "" or "declared" => FlagQueryShow.Declared,
            "changed" => FlagQueryShow.Changed,
            "differs" => FlagQueryShow.DiffersFromDefault,
            "twice" => FlagQueryShow.DeclaredTwice,
            "undeclared" => FlagQueryShow.Undeclared,
            _ => null,
        };
        FlagQueryOrder? order = orderBy?.ToLowerInvariant() switch
        {
            null or "" or "name" => FlagQueryOrder.Name,
            "group" => FlagQueryOrder.Group,
            "updated" => FlagQueryOrder.Updated,
            _ => null,
        };
        if (stateFilter == (FlagDisplayState)(-1) || showFilter is null || order is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid filter", Detail = "state, show or orderBy has a value the API doesn't know." });
        }

        var query = new FlagQuery(
            filter,
            stateFilter,
            showFilter.Value,
            string.IsNullOrEmpty(group) ? null : group,
            UngroupedOnly: group is not null && group.Length == 0,
            string.IsNullOrEmpty(tag) ? null : tag,
            order.Value);

        (IReadOnlyList<CatalogFlag> items, int total) = query.Run(await _catalog.GetAllAsync(cancellationToken), skip, take);
        return Ok(new PagedViewModel<FlagListItemResponseModel> { Total = total, Items = await _responses.ListItemsAsync(items) });
    }

    /// <summary>The groups and tags flags are in, for the list's filters and the tree.</summary>
    [HttpGet("flags/summary")]
    [ProducesResponseType<FlagSummaryResponseModel>(StatusCodes.Status200OK)]
    public async Task<FlagSummaryResponseModel> Summary(CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogFlag> flags = await _catalog.GetAllAsync(cancellationToken);
        CatalogFlag[] declared = flags.Where(flag => flag.IsDeclared).ToArray();
        return new FlagSummaryResponseModel
        {
            Groups = declared
                .GroupBy(flag => flag.Details.Group, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key is null ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FlagGroupCountModel { Name = g.Key, Count = g.Count() })
                .ToArray(),
            Tags = declared.SelectMany(flag => flag.Details.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Total = declared.Length,
            Undeclared = flags.Count - declared.Length,
            ReadOnly = _options.CurrentValue.Overrides != FeatureFlagsOverrides.Enabled,
        };
    }

    /// <summary>One flag, with what its editor shows.</summary>
    [HttpGet("flag")]
    [ProducesResponseType<FlagResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Flag([FromQuery] string name, CancellationToken cancellationToken)
    {
        CatalogFlag? flag = await _catalog.FindAsync(name, cancellationToken);
        return flag is null
            ? NotFound(new ProblemDetails { Title = "Flag not found", Detail = $"No flag source declares '{name}', and it has no saved setting." })
            : Ok(await _responses.FlagAsync(flag));
    }

    /// <summary>The groups the site's group providers offer, for an audience.</summary>
    [HttpGet("provider-groups")]
    [ProducesResponseType<IEnumerable<ProviderGroupResponseModel>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<ProviderGroupResponseModel>> ProviderGroups(CancellationToken cancellationToken)
    {
        var groups = new List<ProviderGroupResponseModel>();
        foreach (IFeatureFlagGroupProvider provider in _groupProviders)
        {
            foreach (FeatureFlagGroup group in await provider.GetGroupsAsync(cancellationToken))
            {
                groups.Add(new ProviderGroupResponseModel { Id = group.Id, Name = group.Name, Description = group.Description });
            }
        }

        return groups;
    }
}
