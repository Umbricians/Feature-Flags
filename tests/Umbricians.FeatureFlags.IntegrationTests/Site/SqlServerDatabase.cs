using Microsoft.Data.SqlClient;

namespace Umbricians.FeatureFlags.IntegrationTests.Site;

/// <summary>A database made for one test run on the server, and dropped when the run is over.</summary>
internal sealed class SqlServerDatabase
{
    private readonly string _serverConnectionString;

    private SqlServerDatabase(string serverConnectionString, string name)
    {
        _serverConnectionString = serverConnectionString;
        Name = name;
        ConnectionString = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = name }.ConnectionString;
    }

    public string Name { get; }

    /// <summary>Connects to this database. Holds the server's password, so it is never shown.</summary>
    public string ConnectionString { get; }

    public static async Task<SqlServerDatabase> CreateAsync(string serverConnectionString)
    {
        var database = new SqlServerDatabase(serverConnectionString, $"FeatureFlagsTests_{Guid.NewGuid():N}");
        await database.RunOnServerAsync($"CREATE DATABASE [{database.Name}]");
        return database;
    }

    /// <summary>Drops the database, throwing off anything still connected to it.</summary>
    public async Task DropAsync()
    {
        // Pooled connections would otherwise keep the database busy for a while after the site has stopped.
        SqlConnection.ClearAllPools();
        await RunOnServerAsync(
            $"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END");
    }

    private async Task RunOnServerAsync(string sql)
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
