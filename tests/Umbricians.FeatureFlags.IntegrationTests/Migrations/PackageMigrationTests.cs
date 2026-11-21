using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests.Migrations;

/// <summary>What the package's migration left in a SQL Server database Umbraco installed itself into.</summary>
public class PackageMigrationTests : IntegrationTest
{
    [Test]
    public async Task The_overrides_table_has_the_columns_the_design_describes()
    {
        var columns = new List<string>();
        await using SqlConnection connection = await Site.OpenConnectionAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, ISNULL(CHARACTER_MAXIMUM_LENGTH, 0), IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """;
        command.Parameters.AddWithValue("@table", FeatureFlagOverrideDto.TableName);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add($"{reader.GetString(0)} {reader.GetString(1)} {reader.GetInt32(2)} {reader.GetString(3)}");
        }

        Assert.That(columns, Is.EqualTo(new[]
        {
            "id int 0 NO",
            "flagName nvarchar 255 NO",
            "definition nvarchar -1 NO",
            "updatedBy uniqueidentifier 0 YES",
            "updatedUtc datetime 0 NO",
        }));
    }

    [Test]
    public async Task The_id_is_the_primary_key_and_counts_up_by_itself()
    {
        await using SqlConnection connection = await Site.OpenConnectionAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
              (SELECT COUNT(*) FROM sys.indexes i
                 JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                 JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                 WHERE i.object_id = OBJECT_ID('{FeatureFlagOverrideDto.TableName}') AND i.is_primary_key = 1 AND c.name = 'id'),
              COLUMNPROPERTY(OBJECT_ID('{FeatureFlagOverrideDto.TableName}'), 'id', 'IsIdentity')
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.That(reader.GetInt32(0), Is.EqualTo(1), "primary key on id");
        Assert.That(reader.GetInt32(1), Is.EqualTo(1), "id is an identity column");
    }

    [Test]
    public async Task A_flag_name_can_only_be_saved_once()
    {
        await using SqlConnection connection = await Site.OpenConnectionAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*) FROM sys.indexes i
              JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE i.object_id = OBJECT_ID('{FeatureFlagOverrideDto.TableName}') AND i.is_unique = 1 AND i.is_primary_key = 0 AND c.name = 'flagName'
            """;

        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Umbraco_recorded_that_both_migration_steps_ran()
    {
        await using SqlConnection connection = await Site.OpenConnectionAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT [value] FROM umbracoKeyValue WHERE [key] LIKE '%' + @package";
        command.Parameters.AddWithValue("@package", Constants.PackageId);

        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("c84e2a19-5f3b-4d7e-a6c0-1b9d8e2f7a35"));
    }

    [Test]
    public async Task The_Administrators_group_was_given_both_permissions()
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        IUserGroup? administrators = await scope.ServiceProvider.GetRequiredService<IUserGroupService>()
            .GetAsync(Umbraco.Cms.Core.Constants.Security.AdminGroupKey);

        Assert.That(administrators!.Permissions, Is.SupersetOf(Constants.Permissions.All));
    }
}
