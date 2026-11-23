using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Migrations;

/// <summary>
/// Umbraco runs each step once per site, on startup, and records that it has, so later changes made by
/// administrators are never overwritten.
/// </summary>
public class FeatureFlagsMigrationPlan : PackageMigrationPlan
{
    /// <summary>Creates the plan under the package's name, which Umbraco records its completed steps against.</summary>
    public FeatureFlagsMigrationPlan()
        : base(Constants.PackageId)
    {
    }

    /// <summary>Lists the plan's steps in the order they run.</summary>
    protected override void DefinePlan() =>
        To<AddFeatureFlagsOverrideTable>("3b1f7c52-9e4d-4a26-8f1b-6d2c0e9a4b71")
            .To<GrantFeatureFlagsPermissionsToAdministrators>("c84e2a19-5f3b-4d7e-a6c0-1b9d8e2f7a35");
}

/// <summary>Adds the table that keeps editors' settings for flags.</summary>
public class AddFeatureFlagsOverrideTable : AsyncMigrationBase
{
    /// <summary>Created by Umbraco when it runs the plan.</summary>
    public AddFeatureFlagsOverrideTable(IMigrationContext context)
        : base(context)
    {
    }

    /// <summary>Creates the table unless it already exists.</summary>
    protected override Task MigrateAsync()
    {
        if (!TableExists(FeatureFlagOverrideDto.TableName))
        {
            Create.Table<FeatureFlagOverrideDto>().Do();
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives the Administrators group both Feature Flags permissions, so the package works straight after installing.
/// Other groups are left for administrators to grant.
/// </summary>
public class GrantFeatureFlagsPermissionsToAdministrators : AsyncMigrationBase
{
    private readonly IUserGroupService _userGroupService;

    /// <summary>Created by Umbraco when it runs the plan.</summary>
    public GrantFeatureFlagsPermissionsToAdministrators(IMigrationContext context, IUserGroupService userGroupService)
        : base(context) => _userGroupService = userGroupService;

    /// <summary>Adds each Feature Flags permission the Administrators group doesn't have yet.</summary>
    protected override async Task MigrateAsync()
    {
        IUserGroup? administrators = await _userGroupService.GetAsync(Umbraco.Cms.Core.Constants.Security.AdminGroupKey);
        if (administrators is null)
        {
            return;
        }

        bool changed = false;
        foreach (string permission in Constants.Permissions.All)
        {
            changed |= administrators.Permissions.Add(permission);
        }

        if (changed)
        {
            await _userGroupService.UpdateAsync(administrators, Umbraco.Cms.Core.Constants.Security.SuperUserKey);
        }
    }
}
