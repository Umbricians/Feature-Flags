using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.IntegrationTests.Site;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests.Overrides;

/// <summary>The site as a visitor and as it starts.</summary>
public class StartupTests : IntegrationTest
{
    [Test]
    public async Task The_site_responds_to_a_request()
    {
        using HttpClient client = Site.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.That((int)response.StatusCode, Is.LessThan(500));
    }

    [Test]
    public async Task A_site_that_starts_with_a_saved_override_in_its_database_uses_it()
    {
        string definition = FlagDefinitions.ToJson("Tests.Off", new FlagSettings(true, null));
        await using (SqlConnection connection = await Site.OpenConnectionAsync())
        await using (SqlCommand command = connection.CreateCommand())
        {
            command.CommandText = $"INSERT INTO {FeatureFlagOverrideDto.TableName} (flagName, definition, updatedUtc) VALUES (@name, @definition, SYSUTCDATETIME())";
            command.Parameters.AddWithValue("@name", "Tests.Off");
            command.Parameters.AddWithValue("@definition", definition);
            await command.ExecuteNonQueryAsync();
        }

        // A second copy of the site, as another server in a load-balanced setup would start, against the same database.
        using TestSiteHost second = await TestSiteHost.StartAsync(Site.ConnectionString);
        await using AsyncServiceScope scope = second.CreateScope();

        Assert.That(await scope.ServiceProvider.GetRequiredService<IFeatureManager>().IsEnabledAsync("Tests.Off"), Is.True);
    }
}
