using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbricians.FeatureFlags.Migrations;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Tests.Overrides;

namespace Umbricians.FeatureFlags.Tests.Migrations;

/// <summary>What the package's migrations do to a site's database and Administrators group.</summary>
public class FeatureFlagsMigrationPlanTests
{
    [Test]
    public void The_plan_is_recorded_under_the_packages_name_and_runs_both_steps_in_order()
    {
        var plan = new FeatureFlagsMigrationPlan();

        Assert.That(plan.Name, Is.EqualTo(Constants.PackageId));
        Assert.That(plan.Transitions.Keys, Does.Contain("3b1f7c52-9e4d-4a26-8f1b-6d2c0e9a4b71").And.Contain("c84e2a19-5f3b-4d7e-a6c0-1b9d8e2f7a35"));
        Assert.That(plan.FinalState, Is.EqualTo("c84e2a19-5f3b-4d7e-a6c0-1b9d8e2f7a35"));
    }

    [Test]
    public async Task The_first_step_adds_the_overrides_table_and_running_it_again_is_harmless()
    {
        using var database = new TestDatabase();

        await database.CreateOverridesTableAsync();
        await database.CreateOverridesTableAsync();

        Assert.That(await new DatabaseOverrideStore(database.Scopes).GetAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task The_second_step_gives_the_administrators_both_permissions_and_keeps_the_ones_they_have()
    {
        using var database = new TestDatabase();
        var administrators = Group(["Umb.Document.Read", Constants.Permissions.View]);
        var groups = new Groups(administrators);

        await database.RunAsync(context => new GrantFeatureFlagsPermissionsToAdministrators(context, groups.Service));

        Assert.That(administrators.Permissions, Is.EquivalentTo(new[] { "Umb.Document.Read", Constants.Permissions.View, Constants.Permissions.Manage }));
        Assert.That(groups.Updated, Is.EqualTo(1));
    }

    [Test]
    public async Task The_second_step_does_not_save_administrators_who_already_have_both_permissions()
    {
        using var database = new TestDatabase();
        var groups = new Groups(Group([.. Constants.Permissions.All]));

        await database.RunAsync(context => new GrantFeatureFlagsPermissionsToAdministrators(context, groups.Service));

        Assert.That(groups.Updated, Is.Zero);
    }

    [Test]
    public async Task The_second_step_does_nothing_on_a_site_without_an_administrators_group()
    {
        using var database = new TestDatabase();
        var groups = new Groups(null);

        await database.RunAsync(context => new GrantFeatureFlagsPermissionsToAdministrators(context, groups.Service));

        Assert.That(groups.Updated, Is.Zero);
    }

    private static IUserGroup Group(string[] permissions)
    {
        var set = new HashSet<string>(permissions);
        return Stub.Of<IUserGroup>((name, _) => name == "get_Permissions" ? set : null);
    }

    private sealed class Groups
    {
        public Groups(IUserGroup? administrators) =>
            Service = Stub.Of<IUserGroupService>((name, args) => name switch
            {
                "GetAsync" when args[0] is Guid key && key == Umbraco.Cms.Core.Constants.Security.AdminGroupKey => Task.FromResult(administrators),
                "UpdateAsync" => Update(administrators!),
                _ => null,
            });

        public IUserGroupService Service { get; }

        public int Updated { get; private set; }

        private Task<Attempt<IUserGroup, UserGroupOperationStatus>> Update(IUserGroup group)
        {
            Updated++;
            return Task.FromResult(Attempt.SucceedWithStatus(UserGroupOperationStatus.Success, group));
        }
    }
}
