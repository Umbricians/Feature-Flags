using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.IntegrationTests.Overrides;

/// <summary>What happens to an audience when Umbraco really deletes a member or a member group.</summary>
public class AudienceCleanupTests : IntegrationTest
{
    [Test]
    public async Task Deleting_a_member_takes_them_out_of_the_audience_and_leaves_the_others()
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        IMemberService members = scope.ServiceProvider.GetRequiredService<IMemberService>();
        IMember leaving = NewMember(members, "leaving");
        IMember staying = NewMember(members, "staying");
        await SaveAudienceAsync("Tests.Off", new FlagAudience([TargetingIds.For(leaving.Key), TargetingIds.For(staying.Key)], []));

        members.Delete(leaving);

        Assert.That((await SavedAudienceAsync("Tests.Off")).Members, Is.EqualTo(new[] { TargetingIds.For(staying.Key) }));
        members.Delete(staying);
    }

    [Test]
    public async Task Deleting_the_only_member_in_an_audience_leaves_it_empty_not_open_to_everyone()
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        IMemberService members = scope.ServiceProvider.GetRequiredService<IMemberService>();
        IMember member = NewMember(members, "only");
        await SaveAudienceAsync("Tests.Off", new FlagAudience([TargetingIds.For(member.Key)], []));

        members.Delete(member);

        FlagAudience audience = await SavedAudienceAsync("Tests.Off");
        Assert.That(audience.Members, Is.Empty);
        Assert.That(audience.Groups, Is.Empty);
    }

    [Test]
    public async Task Deleting_a_member_group_takes_it_out_of_the_audience_and_leaves_the_others()
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        IMemberGroupService groups = scope.ServiceProvider.GetRequiredService<IMemberGroupService>();
        IMemberGroup leaving = await NewGroupAsync(groups, "Leaving");
        IMemberGroup staying = await NewGroupAsync(groups, "Staying");
        await SaveAudienceAsync("Tests.Off", new FlagAudience([], [TargetingIds.For(leaving.Key), TargetingIds.For(staying.Key)]));

        await groups.DeleteAsync(leaving.Key);

        Assert.That((await SavedAudienceAsync("Tests.Off")).Groups, Is.EqualTo(new[] { TargetingIds.For(staying.Key) }));
        await groups.DeleteAsync(staying.Key);
    }

    private static IMember NewMember(IMemberService members, string name)
    {
        string unique = $"{name}-{Guid.NewGuid():N}";
        IMember member = members.CreateMemberWithIdentity($"{unique}@example.com", $"{unique}@example.com", unique, "Member");
        members.Save(member);
        return member;
    }

    private static async Task<IMemberGroup> NewGroupAsync(IMemberGroupService groups, string name)
    {
        var group = new MemberGroup { Name = $"{name} {Guid.NewGuid():N}" };
        var result = await groups.CreateAsync(group);
        Assert.That(result.Success, Is.True, result.Status.ToString());
        return result.Result!;
    }

    private async Task SaveAudienceAsync(string flagName, FlagAudience audience)
    {
        var result = await Site.Services.GetRequiredService<OverrideService>().SaveAsync(flagName, FlagSettings.ForAudience(true, audience), null, default);
        Assert.That(result.Outcome, Is.EqualTo(OverrideOutcome.Saved));
    }

    private async Task<FlagAudience> SavedAudienceAsync(string flagName)
    {
        FeatureFlagOverride saved = (await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default))
            .Single(row => row.FlagName == flagName);
        return FlagDefinitions.ReadOverride(saved.Definition, out _)!.Audience!;
    }
}
