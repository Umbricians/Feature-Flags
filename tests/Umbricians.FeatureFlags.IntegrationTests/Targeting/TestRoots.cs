using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbricians.FeatureFlags.IntegrationTests.Site;

namespace Umbricians.FeatureFlags.IntegrationTests.Targeting;

/// <summary>Two published roots of the test site, each with a domain, made for one test.</summary>
internal sealed record TestRoots(Guid Uk, Guid Us, string UkHost, string UsHost, string UkSegment)
{
    public static async Task<TestRoots> MakeAsync(TestSiteHost site)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        await using AsyncServiceScope scope = site.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        Guid admin = Umbraco.Cms.Core.Constants.Security.SuperUserKey;

        var type = new ContentType(services.GetRequiredService<IShortStringHelper>(), -1)
        {
            Alias = "rootTestPage" + suffix,
            Name = "Root test page " + suffix,
            AllowedAsRoot = true,
        };
        var created = await services.GetRequiredService<IContentTypeService>().CreateAsync(type, admin);
        Assert.That(created.Success, Is.True, created.Result.ToString());

        var content = services.GetRequiredService<IContentService>();
        var domains = services.GetRequiredService<IDomainService>();
        var roots = new TestRoots(Guid.Empty, Guid.Empty, $"uk-{suffix}.test", $"us-{suffix}.test", $"uk-shop-{suffix}");
        Guid uk = await MakeRootAsync(content, domains, type.Alias, "UK shop " + suffix, roots.UkHost);
        Guid us = await MakeRootAsync(content, domains, type.Alias, "US shop " + suffix, roots.UsHost);
        return roots with { Uk = uk, Us = us };
    }

    private static async Task<Guid> MakeRootAsync(IContentService content, IDomainService domains, string alias, string name, string host)
    {
        IContent root = content.Create(name, -1, alias);
        Assert.That(content.Save(root).Success, Is.True);
        Assert.That(content.Publish(root, ["*"]).Success, Is.True);
        var updated = await domains.UpdateDomainsAsync(root.Key, new DomainsUpdateModel
        {
            DefaultIsoCode = null,
            Domains = [new DomainModel { DomainName = host, IsoCode = "en-US" }],
        });
        Assert.That(updated.Success, Is.True, updated.Status.ToString());
        return root.Key;
    }
}
