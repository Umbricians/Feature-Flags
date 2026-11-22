using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Tests.Targeting;

/// <summary>Which root a request is in, the Root condition that checks it, and flags that name a deleted root.</summary>
public class RootTests
{
    internal static readonly Guid Uk = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid Us = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly Guid Gone = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class FakeLocator : IRootLocator
    {
        public Guid? Content { get; set; }

        public Guid? StartItem { get; set; }

        public Guid? Domain { get; set; }

        public List<string> Asked { get; } = [];

        public Guid? OfFoundContent()
        {
            Asked.Add("content");
            return Content;
        }

        public Guid? FromStartItem(string header)
        {
            Asked.Add("start-item:" + header);
            return StartItem;
        }

        public Guid? FromDomain(Uri address)
        {
            Asked.Add("domain:" + address);
            return Domain;
        }
    }

    internal sealed class FakeRoots : ISiteRoots
    {
        public Dictionary<Guid, string> Names { get; } = new() { [Uk] = "UK shop", [Us] = "US shop" };

        public Dictionary<Guid, string[]> Domains { get; } = new() { [Uk] = ["uk.example.com", "example.com/uk"] };

        public SiteRoot? Find(Guid key) => Names.TryGetValue(key, out string? name) ? new SiteRoot(key, name) : null;

        public Task<IReadOnlyList<SiteRoot>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SiteRoot>>(Names.Select(pair => new SiteRoot(pair.Key, pair.Value)).ToArray());

        public Task<IReadOnlyList<string>> GetDomainsAsync(Guid rootKey, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Domains.GetValueOrDefault(rootKey) ?? []);
    }

    private static (RequestRoot Root, FakeLocator Locator, DefaultHttpContext Context) Request(string? startItem = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("uk.example.com");
        context.Request.Path = "/shop";
        if (startItem is not null)
        {
            context.Request.Headers["Start-Item"] = startItem;
        }

        var locator = new FakeLocator();
        return (new RequestRoot(new HttpContextAccessor { HttpContext = context }, locator), locator, context);
    }

    [Test]
    public void The_root_of_the_content_the_request_found_comes_first()
    {
        (RequestRoot root, FakeLocator locator, _) = Request("us");
        locator.Content = Uk;
        locator.StartItem = Us;
        locator.Domain = Us;

        Assert.That(root.Current(), Is.EqualTo(Uk));
        Assert.That(locator.Asked, Is.EqualTo(["content"]));
    }

    [Test]
    public void With_no_content_the_Start_Item_header_is_next_trimmed_of_slashes()
    {
        (RequestRoot root, FakeLocator locator, _) = Request(" /us/ ");
        locator.StartItem = Us;
        locator.Domain = Uk;

        Assert.That(root.Current(), Is.EqualTo(Us));
        Assert.That(locator.Asked, Is.EqualTo(["content", "start-item:us"]));
    }

    [Test]
    public void With_no_content_and_no_header_the_domain_of_the_host_and_path_is_next()
    {
        (RequestRoot root, FakeLocator locator, _) = Request();
        locator.Domain = Us;

        Assert.That(root.Current(), Is.EqualTo(Us));
        Assert.That(locator.Asked, Is.EqualTo(["content", "domain:https://uk.example.com/shop"]));
    }

    [Test]
    public void A_header_that_names_no_root_falls_through_to_the_domain()
    {
        (RequestRoot root, FakeLocator locator, _) = Request("nowhere");
        locator.Domain = Uk;

        Assert.That(root.Current(), Is.EqualTo(Uk));
    }

    [Test]
    public void With_nothing_the_request_is_in_no_root()
    {
        (RequestRoot root, _, _) = Request("nowhere");

        Assert.That(root.Current(), Is.Null);
    }

    [Test]
    public void Without_a_request_there_is_no_root_and_nothing_is_asked()
    {
        var locator = new FakeLocator { Content = Uk };
        var root = new RequestRoot(new HttpContextAccessor(), locator);

        Assert.That(root.Current(), Is.Null);
        Assert.That(locator.Asked, Is.Empty);
    }

    [Test]
    public void The_answer_is_worked_out_once_for_the_request_even_when_it_is_none()
    {
        (RequestRoot root, FakeLocator locator, _) = Request();
        root.Current();
        root.Current();
        Assert.That(locator.Asked, Has.Count.EqualTo(2), "content and domain, once");

        locator.Domain = Uk;
        Assert.That(root.Current(), Is.Null);
    }

    [Test]
    public void A_new_request_asks_again()
    {
        (RequestRoot first, FakeLocator locator, _) = Request();
        locator.Content = Uk;
        Assert.That(first.Current(), Is.EqualTo(Uk));

        (RequestRoot second, FakeLocator other, _) = Request();
        other.Content = Us;
        Assert.That(second.Current(), Is.EqualTo(Us));
    }

    private static TestSite SiteIn(FakeLocator locator, DefaultHttpContext context, FakeRoots? roots = null, params Guid[] named)
    {
        string keys = string.Join(",", named.Select(key => $"\"{key}\""));
        string flag = $$"""
            { "feature_management": { "feature_flags": [ { "id": "Shop", "enabled": true,
              "conditions": { "client_filters": [ { "name": "Umbricians.Root", "parameters": { "Roots": [ {{keys}} ] } } ] } } ] } }
            """;
        return new TestSite(flag, before: services =>
        {
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
            services.AddSingleton<IRootLocator>(locator);
            services.AddSingleton<ISiteRoots>(roots ?? new FakeRoots());
        });
    }

    [Test]
    public async Task The_flag_is_on_in_a_named_root_and_off_in_another()
    {
        (_, FakeLocator locator, DefaultHttpContext context) = Request();
        locator.Content = Us;
        TestSite site = SiteIn(locator, context, null, Uk, Us);
        Assert.That(await site.IsOnAsync("Shop"), Is.True);

        (_, FakeLocator other, DefaultHttpContext otherContext) = Request();
        other.Content = Us;
        Assert.That(await SiteIn(other, otherContext, null, Uk).IsOnAsync("Shop"), Is.False);
    }

    [Test]
    public async Task The_flag_is_off_for_a_request_in_no_root()
    {
        (_, FakeLocator locator, DefaultHttpContext context) = Request();

        Assert.That(await SiteIn(locator, context, null, Uk).IsOnAsync("Shop"), Is.False);
    }

    [Test]
    public async Task A_deleted_root_never_matches()
    {
        var roots = new FakeRoots();
        (_, FakeLocator locator, DefaultHttpContext context) = Request();
        locator.Content = Uk;
        roots.Names.Remove(Uk);
        TestSite site = SiteIn(locator, context, roots, Gone);

        Assert.That(await site.IsOnAsync("Shop"), Is.False);
        Assert.That(new RootCondition(roots).Describe(new JsonObject { ["Roots"] = new JsonArray(Gone.ToString()) }), Is.EqualTo("Only on a deleted site"));
    }

    [Test]
    public void The_root_condition_is_registered_with_its_filter_once()
    {
        (_, FakeLocator locator, DefaultHttpContext context) = Request();
        TestSite site = SiteIn(locator, context, null, Uk);

        Assert.That(site.Provider.GetServices<IFeatureFlagCondition>().OfType<RootCondition>().Count(), Is.EqualTo(1));
        Assert.That(site.Provider.GetServices<Microsoft.FeatureManagement.IFeatureFilterMetadata>().OfType<RootFilter>().Count(), Is.EqualTo(1));
    }

    private static JsonObject Roots(params object[] items) => new() { ["Roots"] = new JsonArray(items.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()) };

    [Test]
    public void Validation_wants_a_non_empty_list_of_keys()
    {
        var condition = new RootCondition(new FakeRoots());

        Assert.That(condition.Validate(Roots(Uk.ToString())), Is.Empty);
        Assert.That(condition.Validate(Roots(Gone.ToString())), Is.Empty, "a deleted root can still be saved");
        Assert.That(condition.Validate(Roots()), Is.Not.Empty);
        Assert.That(condition.Validate(Roots("uk")), Is.Not.Empty);
        Assert.That(condition.Validate(Roots(1)), Is.Not.Empty);
        Assert.That(condition.Validate(new JsonObject()), Is.Not.Empty);
        Assert.That(condition.Validate(new JsonObject { ["Roots"] = Uk.ToString() }), Is.Not.Empty);
    }

    [Test]
    public void The_description_names_the_roots_and_a_deleted_one_is_a_deleted_site()
    {
        var condition = new RootCondition(new FakeRoots());

        Assert.That(condition.Describe(Roots(Uk.ToString(), Us.ToString())), Is.EqualTo("Only on UK shop, US shop"));
        Assert.That(condition.Describe(Roots(Uk.ToString(), Gone.ToString())), Is.EqualTo("Only on UK shop, a deleted site"));
        Assert.That(condition.Describe(new JsonObject()), Is.EqualTo("Sites that can't be read"));
        Assert.That(condition.Name, Is.EqualTo("Umbricians.Root"));
        Assert.That(condition.DisplayName, Is.EqualTo("Only on these sites"));
    }

    [Test]
    public async Task Flags_that_name_a_deleted_root_are_listed()
    {
        var roots = new FakeRoots();
        (_, FakeLocator locator, DefaultHttpContext context) = Request();
        TestSite site = SiteIn(locator, context, roots, Uk, Gone);
        site.Store.With("Other", FlagSettings.Off);

        var deleted = await site.Provider.GetRequiredService<DeletedRootFlags>().FindAsync(CancellationToken.None);
        Assert.That(deleted, Is.EqualTo(["Shop"]));

        roots.Names.Remove(Uk);
        Assert.That(await SiteIn(locator, context, new FakeRoots(), Uk, Us).Provider.GetRequiredService<DeletedRootFlags>().FindAsync(CancellationToken.None), Is.Empty);
    }
}
