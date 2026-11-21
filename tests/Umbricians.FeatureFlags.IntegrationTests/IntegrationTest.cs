using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Umbricians.FeatureFlags.IntegrationTests.Site;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests;

/// <summary>
/// The start of every integration test: ignored when there is no SQL Server, and otherwise starting with no saved
/// overrides in the site's database.
/// </summary>
public abstract class IntegrationTest
{
    internal TestSiteHost Site => IntegrationSite.Host!;

    [SetUp]
    public async Task StartWithNoOverrides()
    {
        if (IntegrationSite.Host is null)
        {
            Assert.Ignore(SqlServerEnvironment.HowToStart);
        }

        await using SqlConnection connection = await Site.OpenConnectionAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {FeatureFlagOverrideDto.TableName}";
        await command.ExecuteNonQueryAsync();
        await Site.Services.GetRequiredService<OverrideCache>().ReloadAsync();
    }
}
