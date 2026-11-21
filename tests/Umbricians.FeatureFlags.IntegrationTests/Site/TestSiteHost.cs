using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Umbricians.FeatureFlags.IntegrationTests.Site;

/// <summary>
/// The test site, booted inside the test process against one database. Umbraco installs itself into an empty database
/// the first time, with an administrator made up for the run; a database that is already installed is just started.
/// </summary>
internal sealed class TestSiteHost : IDisposable
{
    // Umbraco reads its settings while the site is being built, before a test can add to them, so they go in the
    // environment for the length of the run.
    private static readonly string[] Settings =
    [
        "ConnectionStrings__umbracoDbDSN",
        "ConnectionStrings__umbracoDbDSN_ProviderName",
        "Umbraco__CMS__Unattended__InstallUnattended",
        "Umbraco__CMS__Unattended__UnattendedUserName",
        "Umbraco__CMS__Unattended__UnattendedUserEmail",
        "Umbraco__CMS__Unattended__UnattendedUserPassword",
    ];

    private readonly WebApplicationFactory<Program> _factory;

    private TestSiteHost(string connectionString, WebApplicationFactory<Program> factory)
    {
        ConnectionString = connectionString;
        _factory = factory;
    }

    /// <summary>Connects to the site's database. Holds the server's password, so it is never shown.</summary>
    public string ConnectionString { get; }

    public IServiceProvider Services => _factory.Services;

    /// <summary>
    /// Boots the site and waits until Umbraco is running, which includes installing it if the database is empty. Umbraco
    /// installs first and runs the package migrations in the background just after the site has started.
    /// </summary>
    public static async Task<TestSiteHost> StartAsync(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__umbracoDbDSN", connectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__umbracoDbDSN_ProviderName", "Microsoft.Data.SqlClient");
        Environment.SetEnvironmentVariable("Umbraco__CMS__Unattended__InstallUnattended", "true");
        Environment.SetEnvironmentVariable("Umbraco__CMS__Unattended__UnattendedUserName", "Feature Flags Tests");
        Environment.SetEnvironmentVariable("Umbraco__CMS__Unattended__UnattendedUserEmail", "tests@example.com");
        Environment.SetEnvironmentVariable("Umbraco__CMS__Unattended__UnattendedUserPassword", NewPassword());

        var factory = new WebApplicationFactory<Program>();
        var host = new TestSiteHost(connectionString, factory);
        await host.WaitUntilRunningAsync(TimeSpan.FromMinutes(2));
        return host;
    }

    private async Task WaitUntilRunningAsync(TimeSpan timeout)
    {
        IRuntimeState runtime = Services.GetRequiredService<IRuntimeState>();
        DateTime giveUp = DateTime.UtcNow + timeout;
        while (runtime.Level != RuntimeLevel.Run)
        {
            if (DateTime.UtcNow > giveUp)
            {
                throw new TimeoutException($"Umbraco did not reach the Run level within {timeout.TotalSeconds:0} seconds. It was at {runtime.Level}: {runtime.Reason}.");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>Makes a request to the site, without following redirects.</summary>
    public HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Starts a scope of the site's services, as a request would.</summary>
    public AsyncServiceScope CreateScope() => _factory.Services.CreateAsyncScope();

    /// <summary>Opens a connection to the site's database, for the tests that look at it directly.</summary>
    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>Forgets the settings made for the run.</summary>
    public static void ClearSettings()
    {
        foreach (string setting in Settings)
        {
            Environment.SetEnvironmentVariable(setting, null);
        }
    }

    // Made up for each run, long enough and mixed enough for Umbraco's password rules, and never written anywhere.
    private static string NewPassword() => "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}
