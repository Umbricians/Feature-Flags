using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NPoco;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Persistence.Sqlite.Services;
using Umbricians.FeatureFlags.Migrations;

namespace Umbricians.FeatureFlags.Tests.Overrides;

/// <summary>
/// An empty in-memory SQLite database behind Umbraco's own <see cref="UmbracoDatabase"/>, SQL syntax and NPoco, so the
/// package's queries and migrations run for real. Only the scope around the database is a stand-in: it hands out the one
/// database and counts how often it is completed.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public TestDatabase()
    {
        string connectionString = $"Data Source=flags{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var syntax = new SqliteSyntaxProvider(Options.Create(new GlobalSettings()), NullLogger<SqliteSyntaxProvider>.Instance);
        var mappers = new NPoco.MapperCollection();
        SqlContext = new SqlContext(
            syntax,
            DatabaseType.SQLite,
            new FluentPocoDataFactory((type, _) => new PocoDataBuilder(type, mappers).Init(), mappers),
            new Umbraco.Cms.Infrastructure.Persistence.Mappers.MapperCollection(() => []));
        Database = new UmbracoDatabase(connectionString, SqlContext, SqliteFactory.Instance, NullLogger<UmbracoDatabase>.Instance, null, null!, null);

        IScope scope = Stub.Of<IScope>((name, _) => name switch
        {
            "get_Database" => Database,
            "get_SqlContext" => SqlContext,
            "Complete" => Completed(),
            _ => null,
        });
        Scopes = Stub.Of<IScopeProvider>((name, _) => name == "CreateScope" ? scope : null);
    }

    public UmbracoDatabase Database { get; }

    public SqlContext SqlContext { get; }

    public IScopeProvider Scopes { get; }

    public int Completions { get; private set; }

    /// <summary>Runs a package migration step, as Umbraco does when it runs the package's plan.</summary>
    public Task RunAsync(Func<IMigrationContext, AsyncMigrationBase> migration) =>
        migration(NewMigrationContext()).RunAsync();

    /// <summary>Creates the overrides table the way the package's migration does.</summary>
    public Task CreateOverridesTableAsync() => RunAsync(context => new AddFeatureFlagsOverrideTable(context));

    // Umbraco keeps its migration context internal, so build it the way Umbraco does when it runs a plan.
    private IMigrationContext NewMigrationContext()
    {
        Type type = typeof(AsyncMigrationBase).Assembly.GetType("Umbraco.Cms.Infrastructure.Migrations.MigrationContext")!;
        object logger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))!;
        return (IMigrationContext)Activator.CreateInstance(
            type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [new FeatureFlagsMigrationPlan(), Database, logger, new Action(() => { })], null)!;
    }

    public void Dispose()
    {
        Database.Dispose();
        _keepAlive.Dispose();
    }

    private bool Completed()
    {
        Completions++;
        return true;
    }
}
