using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Umbricians.FeatureFlags.Groups;

/// <summary>A member group on the site.</summary>
internal sealed record KnownMemberGroup(Guid Key, string Name);

/// <summary>The site's member groups. A seam so tests don't need Umbraco.</summary>
internal interface IMemberGroups
{
    Task<IReadOnlyList<KnownMemberGroup>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>Umbraco's member groups, through <see cref="IMemberGroupService"/>; none when that service isn't registered.</summary>
internal sealed class UmbracoMemberGroups(IServiceProvider services) : IMemberGroups
{
    public async Task<IReadOnlyList<KnownMemberGroup>> GetAllAsync(CancellationToken cancellationToken)
    {
        IMemberGroupService? service = services.GetService<IMemberGroupService>();
        if (service is null)
        {
            return [];
        }

        IEnumerable<IMemberGroup> groups = await service.GetAllAsync();
        return groups.Select(group => new KnownMemberGroup(group.Key, group.Name ?? string.Empty)).ToArray();
    }
}
