using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Keeps overrides in the Umbraco database, in the table the package migration adds. Flag names are matched ignoring
/// case in code rather than in SQL, whose case rules differ between databases; a site has at most a few hundred flags.
/// </summary>
internal sealed class DatabaseOverrideStore : IFeatureFlagOverrideStore
{
    // Enough for several editors saving the same new flag at the same moment; one of them inserts it and the rest update it.
    private const int MaxSaveAttempts = 3;

    private readonly IScopeProvider _scopes;

    public DatabaseOverrideStore(IScopeProvider scopes) => _scopes = scopes;

    public async Task<IReadOnlyCollection<FeatureFlagOverride>> GetAllAsync(CancellationToken cancellationToken)
    {
        using IScope scope = _scopes.CreateScope(autoComplete: true);
        List<FeatureFlagOverrideDto> rows = await FetchAllAsync(scope);
        return rows.Select(row => new FeatureFlagOverride(row.FlagName, row.Definition, row.UpdatedBy, DateTime.SpecifyKind(row.UpdatedUtc, DateTimeKind.Utc)))
            .ToArray();
    }

    public async Task SaveAsync(FeatureFlagOverride featureFlagOverride, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await SaveOnceAsync(featureFlagOverride);
                return;
            }
            catch (Exception) when (attempt < MaxSaveAttempts)
            {
                // Two saves of a flag with no override can both find none and both insert it; the table's unique index
                // refuses the second. That save was rolled back, so it reads again, finds the row and updates it.
            }
        }
    }

    private async Task SaveOnceAsync(FeatureFlagOverride featureFlagOverride)
    {
        using IScope scope = _scopes.CreateScope();
        FeatureFlagOverrideDto? row = Find(await FetchAllAsync(scope), featureFlagOverride.FlagName);
        if (row is null)
        {
            await scope.Database.InsertAsync(new FeatureFlagOverrideDto
            {
                FlagName = featureFlagOverride.FlagName,
                Definition = featureFlagOverride.Definition,
                UpdatedBy = featureFlagOverride.UpdatedBy,
                UpdatedUtc = featureFlagOverride.UpdatedUtc,
            });
        }
        else
        {
            row.FlagName = featureFlagOverride.FlagName;
            row.Definition = featureFlagOverride.Definition;
            row.UpdatedBy = featureFlagOverride.UpdatedBy;
            row.UpdatedUtc = featureFlagOverride.UpdatedUtc;
            await scope.Database.UpdateAsync(row);
        }

        scope.Complete();
    }

    public async Task DeleteAsync(string flagName, CancellationToken cancellationToken)
    {
        using IScope scope = _scopes.CreateScope();
        FeatureFlagOverrideDto? row = Find(await FetchAllAsync(scope), flagName);
        if (row is not null)
        {
            await scope.Database.DeleteAsync(row);
        }

        scope.Complete();
    }

    private static Task<List<FeatureFlagOverrideDto>> FetchAllAsync(IScope scope) =>
        scope.Database.FetchAsync<FeatureFlagOverrideDto>(scope.SqlContext.Sql().Select<FeatureFlagOverrideDto>().From<FeatureFlagOverrideDto>());

    private static FeatureFlagOverrideDto? Find(IEnumerable<FeatureFlagOverrideDto> rows, string flagName) =>
        rows.FirstOrDefault(row => string.Equals(row.FlagName, flagName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A row of the overrides table.</summary>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class FeatureFlagOverrideDto
{
    public const string TableName = "umbriciansFeatureFlagsOverride";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("flagName")]
    [Length(255)]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_umbriciansFeatureFlagsOverride_flagName")]
    public string FlagName { get; set; } = string.Empty;

    [Column("definition")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string Definition { get; set; } = string.Empty;

    [Column("updatedBy")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? UpdatedBy { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }
}
