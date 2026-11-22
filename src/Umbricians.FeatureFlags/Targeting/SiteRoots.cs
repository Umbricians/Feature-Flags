using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Umbricians.FeatureFlags.Targeting;

/// <summary>A site's root: a top-level content node, which is what editors call a site.</summary>
internal sealed record SiteRoot(Guid Key, string Name);

/// <summary>The site's roots, as the backoffice and the Root condition's summary need them. A seam so tests don't need Umbraco.</summary>
internal interface ISiteRoots
{
    /// <summary>The root with this key, or null when there is none any more: deleted, in the recycle bin, or no longer top-level.</summary>
    SiteRoot? Find(Guid key);

    /// <summary>Every root, in tree order.</summary>
    Task<IReadOnlyList<SiteRoot>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The domains assigned to the root itself, such as <c>example.com</c> or <c>example.com/uk</c>.</summary>
    Task<IReadOnlyList<string>> GetDomainsAsync(Guid rootKey, CancellationToken cancellationToken);
}

/// <summary>Umbraco's content tree and domains; none when the services aren't registered.</summary>
internal sealed class UmbracoSiteRoots(IServiceProvider services) : ISiteRoots
{
    // Roots come from the navigation tree, in memory, and the name from the content cache, so asking for many flags' roots
    // never reads the database. Only a root that isn't published has no cached name, and the content service gives it.
    public SiteRoot? Find(Guid key)
    {
        var navigation = services.GetService<IDocumentNavigationQueryService>();
        if (navigation is null || !navigation.TryGetRootKeys(out IEnumerable<Guid> roots) || !roots.Contains(key))
        {
            return null;
        }

        string? name = services.GetService<IPublishedContentCache>()?.GetById(true, key)?.Name
            ?? services.GetService<IContentService>()?.GetById(key)?.Name;
        return new SiteRoot(key, name ?? string.Empty);
    }

    public Task<IReadOnlyList<SiteRoot>> GetAllAsync(CancellationToken cancellationToken)
    {
        var navigation = services.GetService<IDocumentNavigationQueryService>();
        var contentService = services.GetService<IContentService>();
        if (navigation is null || contentService is null || !navigation.TryGetRootKeys(out IEnumerable<Guid> keys))
        {
            return Task.FromResult<IReadOnlyList<SiteRoot>>([]);
        }

        Guid[] ordered = keys.ToArray();
        IContent[] roots = contentService.GetByIds(ordered).ToArray();
        return Task.FromResult<IReadOnlyList<SiteRoot>>(
            ordered.Select(key => roots.FirstOrDefault(root => root.Key == key))
                .OfType<IContent>()
                .Where(root => !root.Trashed)
                .Select(root => new SiteRoot(root.Key, root.Name ?? string.Empty))
                .ToArray());
    }

    public async Task<IReadOnlyList<string>> GetDomainsAsync(Guid rootKey, CancellationToken cancellationToken)
    {
        var domains = services.GetService<IDomainService>();
        if (domains is null)
        {
            return [];
        }

        return (await domains.GetAssignedDomainsAsync(rootKey, false)).Select(domain => domain.DomainName).ToArray();
    }
}
