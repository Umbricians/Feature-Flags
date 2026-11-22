using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests.Targeting;

/// <summary>The headless endpoint against the real site, with its settings changed for the length of each test.</summary>
public class HeadlessEndpointTests : IntegrationTest
{
    private const string Url = "/umbraco/umbriciansfeatureflags/delivery/v1/flags";

    private bool _wasEnabled;
    private bool _wasPublic;
    private string? _wasKey;

    [SetUp]
    public void SwitchOn()
    {
        FeatureFlagsOptions options = Site.Services.GetRequiredService<IOptionsMonitor<FeatureFlagsOptions>>().CurrentValue;
        DeliveryApiSettings delivery = Site.Services.GetRequiredService<IOptionsMonitor<DeliveryApiSettings>>().CurrentValue;
        (_wasEnabled, _wasPublic, _wasKey) = (options.Headless.Enabled, delivery.PublicAccess, delivery.ApiKey);
        options.Headless.Enabled = true;
    }

    [TearDown]
    public void Restore()
    {
        if (IntegrationSite.Host is null)
        {
            return;
        }

        Site.Services.GetRequiredService<IOptionsMonitor<FeatureFlagsOptions>>().CurrentValue.Headless.Enabled = _wasEnabled;
        DeliveryApiSettings delivery = Site.Services.GetRequiredService<IOptionsMonitor<DeliveryApiSettings>>().CurrentValue;
        (delivery.PublicAccess, delivery.ApiKey) = (_wasPublic, _wasKey);
    }

    private async Task<(HttpResponseMessage Response, JsonObject? Body)> GetAsync(Action<HttpRequestMessage>? configure = null)
    {
        using HttpClient client = Site.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        configure?.Invoke(request);
        HttpResponseMessage response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        return (response, response.StatusCode == HttpStatusCode.OK ? JsonNode.Parse(text)!.AsObject() : null);
    }

    private void DeliveryApi(bool publicAccess, string? apiKey)
    {
        DeliveryApiSettings delivery = Site.Services.GetRequiredService<IOptionsMonitor<DeliveryApiSettings>>().CurrentValue;
        (delivery.PublicAccess, delivery.ApiKey) = (publicAccess, apiKey);
    }

    [Test]
    public async Task The_endpoint_is_a_404_while_headless_is_off()
    {
        Site.Services.GetRequiredService<IOptionsMonitor<FeatureFlagsOptions>>().CurrentValue.Headless.Enabled = false;

        Assert.That((await GetAsync()).Response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Only_public_flags_are_returned_with_whether_each_is_on_and_the_name_as_declared()
    {
        (HttpResponseMessage response, JsonObject? body) = await GetAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!["Headless.Public"]!.GetValue<bool>(), Is.True);
        Assert.That(body["Headless.PublicOff"]!.GetValue<bool>(), Is.False);
        Assert.That(body.ContainsKey("Tests.On"), Is.False);
        Assert.That(body.ContainsKey("Tests.InCode"), Is.False);
    }

    [Test]
    public async Task The_answer_is_never_cached_and_needs_no_sign_in()
    {
        (HttpResponseMessage response, _) = await GetAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl!.ToString(), Is.EqualTo("no-store, private"));
    }

    [Test]
    public async Task A_delivery_api_that_is_not_public_needs_its_api_key()
    {
        DeliveryApi(publicAccess: false, apiKey: "s3cret");

        Assert.That((await GetAsync()).Response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await GetAsync(request => request.Headers.Add("Api-Key", "wrong"))).Response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await GetAsync(request => request.Headers.Add("Api-Key", "s3cret"))).Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_delivery_api_that_is_not_public_and_has_no_key_refuses_everyone()
    {
        DeliveryApi(publicAccess: false, apiKey: null);

        Assert.That((await GetAsync(request => request.Headers.Add("Api-Key", ""))).Response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Start_Item_places_the_request_in_a_root_so_a_flag_for_that_root_is_on_only_there()
    {
        TestRoots roots = await TestRoots.MakeAsync(Site);
        await using (AsyncServiceScope scope = Site.CreateScope())
        {
            FeatureFlagResult saved = await scope.ServiceProvider.GetRequiredService<IFeatureFlagService>().SaveAsync(
                "Headless.Rooted",
                new FlagSettings(true, [new FlagCondition("Umbricians.Root", new JsonObject { ["Roots"] = new JsonArray(JsonValue.Create(roots.Uk.ToString())) })]));
            Assert.That(saved.Status, Is.EqualTo(FeatureFlagResultStatus.Saved), saved.Reason);
        }

        JsonObject inUk = (await GetAsync(request => request.Headers.Add("Start-Item", roots.Uk.ToString()))).Body!;
        JsonObject inUs = (await GetAsync(request => request.Headers.Add("Start-Item", roots.Us.ToString()))).Body!;
        JsonObject nowhere = (await GetAsync()).Body!;

        Assert.That(inUk["Headless.Rooted"]!.GetValue<bool>(), Is.True);
        Assert.That(inUs["Headless.Rooted"]!.GetValue<bool>(), Is.False);
        Assert.That(nowhere["Headless.Rooted"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task A_flag_for_an_audience_with_a_percentage_is_off_for_an_anonymous_visitor()
    {
        await using (AsyncServiceScope scope = Site.CreateScope())
        {
            FeatureFlagResult saved = await scope.ServiceProvider.GetRequiredService<IFeatureFlagService>().SaveAsync(
                "Headless.PublicOff",
                new FlagSettings(true, [new FlagCondition("Microsoft.Targeting", JsonNode.Parse("""{ "Audience": { "Users": [], "Groups": [], "DefaultRolloutPercentage": 100 } }""")!.AsObject())]));
            Assert.That(saved.Status, Is.EqualTo(FeatureFlagResultStatus.Saved), saved.Reason);
        }

        Assert.That((await GetAsync()).Body!["Headless.PublicOff"]!.GetValue<bool>(), Is.False);
    }
}
