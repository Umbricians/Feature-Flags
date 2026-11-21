using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Web.Common.Routing;
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
    private readonly FlagList _list;
    private readonly FlagCatalog _catalog;
    private readonly FlagResponses _responses;
    private readonly AudienceGroups _audienceGroups;
    private readonly IHostEnvironment _environment;

    public FlagsController(
        FlagList list,
        FlagCatalog catalog,
        FlagResponses responses,
        AudienceGroups audienceGroups,
        IHostEnvironment environment)
    {
        _list = list;
        _catalog = catalog;
        _responses = responses;
        _audienceGroups = audienceGroups;
        _environment = environment;
    }

    /// <summary>A page of flags, searched, filtered and sorted across every flag.</summary>
    /// <param name="skip">How many matching flags to skip.</param>
    /// <param name="take">How many to return, at most 100.</param>
    /// <param name="filter">Text the name or description contains.</param>
    /// <param name="state"><c>off</c>, <c>everyone</c>, <c>audience</c> or <c>custom</c>.</param>
    /// <param name="show"><c>declared</c> (the default), or <c>undeclared</c> for settings left from flags nobody declares any more.</param>
    /// <param name="marks">Only flags with every one of these, comma-separated: <c>changed</c>, <c>differs</c> and <c>twice</c>.</param>
    /// <param name="section">A section's name, or an empty value for flags in no section.</param>
    /// <param name="tag">A tag.</param>
    /// <param name="orderBy"><c>name</c> (the default), <c>section</c> or <c>updated</c>, most recent first.</param>
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
        string? marks = null,
        string? section = null,
        string? tag = null,
        string? orderBy = null,
        CancellationToken cancellationToken = default)
    {
        FlagListResult result = await _list.PageAsync(new FlagListRequest(skip, take, filter, state, show, marks, section, tag, orderBy), cancellationToken);
        return result.Problem is { } problem
            ? BadRequest(new ProblemDetails { Title = problem.Title, Detail = problem.Detail })
            : Ok(new PagedViewModel<FlagListItemResponseModel> { Total = result.Total, Items = await _responses.ListItemsAsync(result.Items, cancellationToken) });
    }

    /// <summary>The sections and tags flags are in, for the list's filters, and what the list says about the site.</summary>
    [HttpGet("flags/summary")]
    [ProducesResponseType<FlagSummaryResponseModel>(StatusCodes.Status200OK)]
    public async Task<FlagSummaryResponseModel> Summary(CancellationToken cancellationToken)
    {
        FlagListSummary summary = await _list.SummaryAsync(cancellationToken);
        return new FlagSummaryResponseModel
        {
            Sections = summary.Sections.Select(section => new FlagSectionCountModel { Name = section.Name, Count = section.Count }).ToArray(),
            Tags = summary.Tags,
            Total = summary.Total,
            Undeclared = summary.Undeclared,
            ReadOnly = summary.ReadOnly,
            Environment = _environment.EnvironmentName,
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
            : Ok(await _responses.FlagAsync(flag, cancellationToken));
    }

    /// <summary>The groups the site's group providers offer, for an audience.</summary>
    [HttpGet("provider-groups")]
    [ProducesResponseType<IEnumerable<ProviderGroupResponseModel>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<ProviderGroupResponseModel>> ProviderGroups(CancellationToken cancellationToken)
    {
        GroupDirectory directory = await _audienceGroups.ReadAsync(cancellationToken);
        return directory.ProviderGroups.Select(group => new ProviderGroupResponseModel { Id = group.Id, Name = group.Name, Description = group.Description });
    }
}
