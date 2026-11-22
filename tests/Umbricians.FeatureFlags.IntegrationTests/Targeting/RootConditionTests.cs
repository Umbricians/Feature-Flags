using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests.Targeting;

/// <summary>
/// The Root condition against the real site: two published roots, each with a domain, and a flag on for one of them.
/// A request is placed in a root by its host or by a Start-Item header.
/// </summary>
public class RootConditionTests : IntegrationTest
{
    private Task<TestRoots> MakeRootsAsync() => TestRoots.MakeAsync(Site);

    private async Task SaveRootFlagAsync(string name, params Guid[] roots)
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        FeatureFlagResult result = await scope.ServiceProvider.GetRequiredService<IFeatureFlagService>().SaveAsync(
            name,
            new FlagSettings(true, [new FlagCondition("Umbricians.Root", new JsonObject { ["Roots"] = new JsonArray(roots.Select(key => (JsonNode?)key.ToString()).ToArray()) })]));
        Assert.That(result.Status, Is.EqualTo(FeatureFlagResultStatus.Saved), result.Reason);
    }

    private async Task<string> CheckAsync(string flag, string? host = null, string? startItem = null)
    {
        using HttpClient client = Site.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test-flags/" + flag);
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (startItem is not null)
        {
            request.Headers.Add("Start-Item", startItem);
        }

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsStringAsync();
    }

    [Test]
    public async Task A_flag_for_one_root_is_on_by_its_domain_and_off_by_the_others_and_off_with_no_root()
    {
        TestRoots roots = await MakeRootsAsync();
        await SaveRootFlagAsync("Tests.Off", roots.Uk);

        Assert.That(await CheckAsync("Tests.Off", roots.UkHost), Is.EqualTo("on"));
        Assert.That(await CheckAsync("Tests.Off", roots.UsHost), Is.EqualTo("off"));
        Assert.That(await CheckAsync("Tests.Off", "unknown.test"), Is.EqualTo("off"));
    }

    [Test]
    public async Task The_Start_Item_header_places_a_request_by_a_roots_key_and_beats_the_domain()
    {
        TestRoots roots = await MakeRootsAsync();
        await SaveRootFlagAsync("Tests.Off", roots.Uk);

        Assert.That(await CheckAsync("Tests.Off", roots.UsHost, roots.Uk.ToString()), Is.EqualTo("on"));
        Assert.That(await CheckAsync("Tests.Off", roots.UkHost, roots.Us.ToString()), Is.EqualTo("off"));
        Assert.That(await CheckAsync("Tests.Off", null, Guid.NewGuid().ToString()), Is.EqualTo("off"));
    }

    [Test]
    public async Task The_Start_Item_header_places_a_request_by_a_roots_url_segment()
    {
        TestRoots roots = await MakeRootsAsync();
        await using (AsyncServiceScope scope = Site.CreateScope())
        {
            string name = scope.ServiceProvider.GetRequiredService<IContentService>().GetById(roots.Uk)!.Name!;
            await SaveRootFlagAsync("Tests.Off", roots.Uk);
            string segment = name.ToLowerInvariant().Replace(' ', '-');

            Assert.That(await CheckAsync("Tests.Off", null, "/" + segment + "/"), Is.EqualTo("on"));
        }
    }

    [Test]
    public async Task A_deleted_root_never_matches_and_its_flag_is_listed()
    {
        TestRoots roots = await MakeRootsAsync();
        await SaveRootFlagAsync("Tests.Off", roots.Uk);
        await using AsyncServiceScope scope = Site.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IContentService>();
        content.Delete(content.GetById(roots.Uk)!);

        Assert.That(await CheckAsync("Tests.Off", roots.UkHost), Is.EqualTo("off"));
        Assert.That(await scope.ServiceProvider.GetRequiredService<DeletedRootFlags>().FindAsync(CancellationToken.None), Does.Contain("Tests.Off"));
    }
}
