using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Umbricians.FeatureFlags.IntegrationTests.Api;

/// <summary>
/// A script or CI pipeline signed in as an Umbraco API user with client credentials, calling the Management API the way
/// docs/api.md says to: the package's permission check must work for it exactly as for an editor.
/// </summary>
public class ApiUserTests : IntegrationTest
{
    private const string Base = "/umbraco/umbriciansfeatureflags/api/v1/";

    [Test]
    public async Task An_api_user_in_a_group_with_both_permissions_can_read_and_change_flags()
    {
        using HttpClient client = await SignInAsApiUserAsync(Constants.Permissions.View, Constants.Permissions.Manage);

        HttpResponseMessage list = await client.GetAsync(Base + "flags?filter=Tests.");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK), await list.Content.ReadAsStringAsync());
        JsonElement page = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()), Does.Contain("Tests.Off"));

        HttpResponseMessage saved = await client.PutAsJsonAsync(Base + "flag?name=Tests.Off", new { enabled = true, conditions = Array.Empty<object>(), match = "any" });
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
        JsonElement flag = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(flag.GetProperty("state").GetString(), Is.EqualTo("everyone"));

        // The API user is credited with the change, as an editor would be.
        Guid userKey = flag.GetProperty("override").GetProperty("updatedBy").GetGuid();
        Assert.That(flag.GetProperty("override").GetProperty("updatedByName").GetString(), Is.EqualTo("Feature Flags API user"));
        Assert.That(userKey, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task An_api_user_can_save_the_audience_from_the_docs_then_get_switch_and_reset_the_flag()
    {
        using HttpClient client = await SignInAsApiUserAsync(Constants.Permissions.View, Constants.Permissions.Manage);

        HttpResponseMessage saved = await client.PutAsync(Base + "flag?name=Tests.On", new StringContent("""
            { "enabled": true, "match": "any",
              "conditions": [ { "name": "Microsoft.Targeting",
                "parameters": { "Audience": { "Users": [], "Groups": [ { "Name": "Beta testers", "RolloutPercentage": 100 } ], "DefaultRolloutPercentage": 0 } } } ] }
            """, System.Text.Encoding.UTF8, "application/json"));
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
        Assert.That((await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString(), Is.EqualTo("audience"));

        HttpResponseMessage read = await client.GetAsync(Base + "flag?name=Tests.On");
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString(), Is.EqualTo("audience"));

        HttpResponseMessage off = await client.PutAsJsonAsync(Base + "flag/enabled?name=Tests.On", new { enabled = false });
        Assert.That(off.StatusCode, Is.EqualTo(HttpStatusCode.OK), await off.Content.ReadAsStringAsync());
        Assert.That((await off.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enabled").GetBoolean(), Is.False);

        HttpResponseMessage reset = await client.DeleteAsync(Base + "flag?name=Tests.On");
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), await reset.Content.ReadAsStringAsync());
        Assert.That((await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString(), Is.EqualTo("everyone"));
    }

    [Test]
    public async Task A_null_condition_is_a_bad_request_not_a_server_error()
    {
        using HttpClient client = await SignInAsApiUserAsync(Constants.Permissions.View, Constants.Permissions.Manage);

        HttpResponseMessage response = await client.PutAsync(Base + "flag?name=Tests.Off", new StringContent(
            """{"enabled":true,"conditions":[null]}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Deeply_nested_parameters_are_refused_by_the_rules_not_by_a_server_error()
    {
        using HttpClient client = await SignInAsApiUserAsync(Constants.Permissions.View, Constants.Permissions.Manage);
        string nested = string.Concat(Enumerable.Repeat("{\"a\":", 60)) + "1" + new string('}', 60);

        HttpResponseMessage response = await client.PutAsync(Base + "flag?name=Tests.Off", new StringContent(
            $$"""{"enabled":true,"conditions":[{"name":"Contoso.Unregistered","parameters":{{nested}}}]}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task An_api_user_in_a_group_with_only_view_can_read_but_not_change_flags()
    {
        using HttpClient client = await SignInAsApiUserAsync(Constants.Permissions.View);

        Assert.That((await client.GetAsync(Base + "flags")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        HttpResponseMessage refused = await client.PutAsJsonAsync(Base + "flag?name=Tests.Off", new { enabled = true, conditions = Array.Empty<object>(), match = "any" });
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task An_api_user_in_a_group_with_no_feature_flags_permission_can_not_read_flags()
    {
        using HttpClient client = await SignInAsApiUserAsync();

        Assert.That((await client.GetAsync(Base + "flags")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Without_a_token_the_api_is_unauthorized()
    {
        using HttpClient client = Site.CreateClient();

        Assert.That((await client.GetAsync(Base + "flags")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task The_swagger_document_describes_a_condition_s_parameters_as_free_form_json()
    {
        using HttpClient client = Site.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/umbraco/openapi/" + Constants.ApiName + ".json");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        JsonElement document = await response.Content.ReadFromJsonAsync<JsonElement>();

        JsonElement schemas = document.GetProperty("components").GetProperty("schemas");
        JsonElement condition = schemas.GetProperty("FlagConditionModel");
        JsonElement parameters = condition.GetProperty("properties").GetProperty("parameters");
        if (parameters.TryGetProperty("$ref", out JsonElement reference))
        {
            parameters = schemas.GetProperty(reference.GetString()!.Split('/')[^1]);
        }

        Assert.That(parameters.GetRawText(), Does.Contain("\"object\"").And.Not.Contain("options"));
        Assert.That(schemas.TryGetProperty("SaveFlagRequestModel", out _), Is.True);
        Assert.That(schemas.TryGetProperty("SaveAudienceRequestModel", out _), Is.False);
    }

    /// <summary>Makes an API user in a new group with these permissions, and returns a client that has its bearer token.</summary>
    private async Task<HttpClient> SignInAsApiUserAsync(params string[] permissions)
    {
        string suffix = Guid.NewGuid().ToString("N");
        string clientId = "umbraco-back-office-flags-" + suffix;
        string clientSecret = "S3cret-" + Guid.NewGuid().ToString("N");

        await using AsyncServiceScope scope = Site.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        Guid admin = Umbraco.Cms.Core.Constants.Security.SuperUserKey;

        var group = new UserGroup(services.GetRequiredService<IShortStringHelper>(), 0, "flagsTest" + suffix, "Flags test " + suffix, "icon-users")
        {
            Permissions = new HashSet<string>(permissions),
        };
        var created = await services.GetRequiredService<IUserGroupService>().CreateAsync(group, admin, []);
        Assert.That(created.Success, Is.True, created.Status.ToString());

        var user = await services.GetRequiredService<IUserService>().CreateAsync(admin, new UserCreateModel
        {
            Email = $"flags-{suffix}@example.com",
            UserName = $"flags-{suffix}@example.com",
            Name = "Feature Flags API user",
            Kind = UserKind.Api,
            UserGroupKeys = new HashSet<Guid> { group.Key },
        }, approveUser: true);
        Assert.That(user.Success, Is.True, user.Status.ToString());

        await services.GetRequiredService<IBackOfficeUserClientCredentialsManager>().SaveAsync(user.Result.CreatedUser!.Key, clientId, clientSecret);

        HttpClient client = Site.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        HttpResponseMessage token = await client.PostAsync("/umbraco/management/api/v1/security/back-office/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }));
        Assert.That(token.StatusCode, Is.EqualTo(HttpStatusCode.OK), await token.Content.ReadAsStringAsync());
        string accessToken = (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
