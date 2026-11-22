using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Web.Common.Routing;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Security;
using Umbricians.FeatureFlags.Targeting;

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
    private readonly IFeatureFlagService _flags;
    private readonly FlagResponses _responses;
    private readonly AudienceGroups _audienceGroups;
    private readonly ConditionRegistry _conditions;
    private readonly IHostEnvironment _environment;
    private readonly ISiteRoots _roots;

    public FlagsController(
        FlagList list,
        IFeatureFlagService flags,
        FlagResponses responses,
        AudienceGroups audienceGroups,
        ConditionRegistry conditions,
        IHostEnvironment environment,
        ISiteRoots roots)
    {
        _list = list;
        _flags = flags;
        _responses = responses;
        _audienceGroups = audienceGroups;
        _conditions = conditions;
        _environment = environment;
        _roots = roots;
    }

    /// <summary>A page of flags, searched, filtered and sorted across every flag.</summary>
    /// <param name="skip">How many matching flags to skip.</param>
    /// <param name="take">How many to return, at most 100.</param>
    /// <param name="filter">Text the name or description contains.</param>
    /// <param name="state"><c>off</c>, <c>everyone</c>, <c>audience</c>, <c>conditions</c> or <c>custom</c>.</param>
    /// <param name="show"><c>declared</c> (the default), or <c>undeclared</c> for settings left from flags nobody declares any more.</param>
    /// <param name="marks">Only flags with every one of these, comma-separated: <c>changed</c>, <c>differs</c> and <c>twice</c>.</param>
    /// <param name="section">A section's name, or an empty value for flags in no section.</param>
    /// <param name="tag">A tag.</param>
    /// <param name="orderBy"><c>name</c> (the default), <c>section</c> or <c>updated</c>, most recent first.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    [HttpGet("flags")]
    [ProducesResponseType<PagedViewModel<FlagResponseModel>>(StatusCodes.Status200OK)]
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
            : Ok(new PagedViewModel<FlagResponseModel> { Total = result.Total, Items = await _responses.ListAsync(result.Items, cancellationToken) });
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

    /// <summary>One flag.</summary>
    [HttpGet("flag")]
    [ProducesResponseType<FlagResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Flag([FromQuery] string name, CancellationToken cancellationToken)
    {
        FeatureFlag? flag = await _flags.GetAsync(name, cancellationToken);
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

    /// <summary>The conditions registered on this site, so the backoffice can offer them and find their editors.</summary>
    [HttpGet("conditions")]
    [ProducesResponseType<IEnumerable<ConditionResponseModel>>(StatusCodes.Status200OK)]
    public IEnumerable<ConditionResponseModel> Conditions() =>
        _conditions.All.Select(condition => new ConditionResponseModel { Name = condition.Name, DisplayName = condition.DisplayName });

    /// <summary>
    /// The sites (top-level content nodes) the Root condition can name, each with its domains. Without <paramref name="keys"/>
    /// every root; with them, only those keys, and a key with no root any more comes back with <c>deleted</c> true.
    /// </summary>
    /// <param name="keys">Root keys, comma-separated.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    [HttpGet("roots")]
    [ProducesResponseType<IEnumerable<RootResponseModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Roots(string? keys = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keys))
        {
            var all = new List<RootResponseModel>();
            foreach (SiteRoot root in await _roots.GetAllAsync(cancellationToken))
            {
                all.Add(await ModelAsync(root.Key, root, cancellationToken));
            }

            return Ok(all);
        }

        var asked = new List<Guid>();
        foreach (string text in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(text, out Guid key))
            {
                return BadRequest(new ProblemDetails { Title = "Invalid keys", Detail = $"'{text}' isn't a key." });
            }

            asked.Add(key);
        }

        var found = new List<RootResponseModel>();
        foreach (Guid key in asked.Distinct())
        {
            found.Add(await ModelAsync(key, _roots.Find(key), cancellationToken));
        }

        return Ok(found);
    }

    private async Task<RootResponseModel> ModelAsync(Guid key, SiteRoot? root, CancellationToken cancellationToken) => new()
    {
        Key = key,
        Name = root?.Name,
        Domains = root is null ? [] : await _roots.GetDomainsAsync(key, cancellationToken),
        Deleted = root is null,
    };
}
