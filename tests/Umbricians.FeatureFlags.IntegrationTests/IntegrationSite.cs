using Umbricians.FeatureFlags.IntegrationTests.Site;

namespace Umbricians.FeatureFlags.IntegrationTests;

/// <summary>
/// Makes a database on the SQL Server and boots the test site against it once for every test, then drops the database.
/// Without a SQL Server it does nothing, and each test reports itself ignored.
/// </summary>
[SetUpFixture]
public class IntegrationSite
{
    private static SqlServerDatabase? _database;

    internal static TestSiteHost? Host { get; private set; }

    internal static string? Database => _database?.ConnectionString;

    [OneTimeSetUp]
    public async Task StartSite()
    {
        string? server = SqlServerEnvironment.FindConnectionString();
        if (server is null)
        {
            return;
        }

        _database = await SqlServerDatabase.CreateAsync(server);
        Host = await TestSiteHost.StartAsync(_database.ConnectionString);
    }

    [OneTimeTearDown]
    public async Task StopSite()
    {
        Host?.Dispose();
        Host = null;
        TestSiteHost.ClearSettings();
        if (_database is not null)
        {
            await _database.DropAsync();
            _database = null;
        }
    }
}
