using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Umbricians.FeatureFlags.Targeting;

/// <summary>The three ways a request's root is found, each answering with the root's key or null. A seam so tests don't need Umbraco.</summary>
internal interface IRootLocator
{
    /// <summary>The root of the content the request found.</summary>
    Guid? OfFoundContent();

    /// <summary>The root a <c>Start-Item</c> header names: a root's key, or a root's URL segment, as the Delivery API reads it.</summary>
    Guid? FromStartItem(string header);

    /// <summary>The root of the domain that matches the request's address.</summary>
    Guid? FromDomain(Uri address);

    /// <summary>
    /// Whether the request's answer can no longer change: Umbraco has routed it, or there is no Umbraco request to wait for.
    /// A flag checked in middleware, before routing, can't yet know the content's root.
    /// </summary>
    bool IsRouted();
}

/// <summary>
/// Which <see cref="Flags.RootCondition">root</see> the current request is in: the root of the content the request found;
/// else the one the <c>Start-Item</c> header names; else the one whose domain matches the request's host and path; else
/// none. Worked out once for each request, once Umbraco has routed it.
/// </summary>
internal sealed class RequestRoot(IHttpContextAccessor http, IRootLocator locator)
{
    internal const string StartItemHeader = "Start-Item";

    private static readonly object ItemsKey = new();

    private sealed record Found(Guid? Key);

    /// <summary>The key of the request's root, or null when there is no request or it is in no root.</summary>
    public Guid? Current()
    {
        HttpContext? context = http.HttpContext;
        if (context is null)
        {
            return null;
        }

        if (context.Items[ItemsKey] is Found cached)
        {
            return cached.Key;
        }

        Guid? key = Find(context);

        // An answer worked out before Umbraco routed the request may change once the content is found, so it isn't kept.
        if (locator.IsRouted())
        {
            context.Items[ItemsKey] = new Found(key);
        }

        return key;
    }

    private Guid? Find(HttpContext context)
    {
        if (locator.OfFoundContent() is { } ofContent)
        {
            return ofContent;
        }

        string header = context.Request.Headers[StartItemHeader].ToString().Trim().Trim('/');
        if (header.Length > 0 && locator.FromStartItem(header) is { } fromHeader)
        {
            return fromHeader;
        }

        return Uri.TryCreate(UriHelper(context.Request), UriKind.Absolute, out Uri? address) ? locator.FromDomain(address) : null;
    }

    private static string UriHelper(HttpRequest request) =>
        $"{request.Scheme}://{request.Host.Value}{request.PathBase}{request.Path}";
}

/// <summary>
/// Finds roots through Umbraco's published content, navigation and domains. It reads keys and the navigation tree
/// rather than properties of cached content, which can belong to a scope that has ended.
/// </summary>
internal sealed class UmbracoRootLocator(IServiceProvider services) : IRootLocator
{
    public Guid? OfFoundContent()
    {
        var accessor = services.GetService<IUmbracoContextAccessor>();
        if (accessor is null || !accessor.TryGetUmbracoContext(out IUmbracoContext? context))
        {
            return null;
        }

        IPublishedContent? content = context.PublishedRequest?.PublishedContent;
        return content is null ? null : RootOf(content.Key);
    }

    // As the Delivery API finds its start item: only a published root can be one, and a key must be the root's own.
    public Guid? FromStartItem(string header)
    {
        var navigation = services.GetService<IDocumentNavigationQueryService>();
        var cache = services.GetService<IPublishedContentCache>();
        if (navigation is null || cache is null || !navigation.TryGetRootKeys(out IEnumerable<Guid> rootKeys))
        {
            return null;
        }

        Guid[] roots = rootKeys.Where(key => cache.GetById(false, key) is not null).ToArray();
        if (Guid.TryParse(header, out Guid key))
        {
            return roots.Contains(key) ? key : null;
        }

        var urls = services.GetService<IDocumentUrlService>();
        string? culture = services.GetService<IVariationContextAccessor>()?.VariationContext?.Culture;
        culture = string.IsNullOrEmpty(culture) ? services.GetService<IDomainCache>()?.DefaultCulture : culture;
        return urls is null || culture is null
            ? null
            : roots.Where(root => urls.GetUrlSegment(root, culture, false).InvariantEquals(header)).Select(root => (Guid?)root).FirstOrDefault();
    }

    public bool IsRouted() =>
        services.GetService<IUmbracoContextAccessor>() is not { } accessor
        || !accessor.TryGetUmbracoContext(out IUmbracoContext? context)
        || context.PublishedRequest is not null;

    public Guid? FromDomain(Uri address)
    {
        // Umbraco's own cleaned address, so case and path are normalised the way its domain matching expects.
        if (services.GetService<IUmbracoContextAccessor>() is { } accessor && accessor.TryGetUmbracoContext(out IUmbracoContext? umbraco))
        {
            address = umbraco.CleanedUmbracoUrl;
        }

        var domains = services.GetService<IDomainCache>();
        var ids = services.GetService<IIdKeyMap>();
        if (domains is null || ids is null)
        {
            return null;
        }

        Domain? domain = DomainUtilities.SelectDomain(domains.GetAll(false), address);
        if (domain is null)
        {
            return null;
        }

        Attempt<Guid> key = ids.GetKeyForId(domain.ContentId, UmbracoObjectTypes.Document);
        return key.Success ? RootOf(key.Result) : null;
    }

    // The top of the content's branch: the last of its ancestors, or the content itself when it is top-level.
    private Guid? RootOf(Guid key)
    {
        var navigation = services.GetService<IDocumentNavigationQueryService>();
        if (navigation is null || !navigation.TryGetAncestorsOrSelfKeys(key, out IEnumerable<Guid> branch))
        {
            return null;
        }

        Guid[] keys = branch.ToArray();
        return keys.Length == 0 ? null : keys[^1];
    }
}
