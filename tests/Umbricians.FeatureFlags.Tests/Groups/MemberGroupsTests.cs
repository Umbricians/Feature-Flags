using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Tests.Groups;

/// <summary>The site's member groups, as Umbraco has them.</summary>
public class MemberGroupsTests
{
    [Test]
    public async Task A_site_without_Umbraco_s_member_group_service_has_no_member_groups()
    {
        var groups = new UmbracoMemberGroups(new ServiceCollection().BuildServiceProvider());

        Assert.That(await groups.GetAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task Reads_every_member_group_with_its_key_and_name()
    {
        Guid key = Guid.NewGuid();
        IMemberGroup unnamed = Stub.Of<IMemberGroup>((method, _) => method == "get_Key" ? Guid.Empty : null);
        IMemberGroup premium = Stub.Of<IMemberGroup>((method, _) => method switch { "get_Key" => key, "get_Name" => "Premium", _ => null });
        IMemberGroupService service = Stub.Of<IMemberGroupService>((method, _) =>
            method == nameof(IMemberGroupService.GetAllAsync) ? Task.FromResult<IEnumerable<IMemberGroup>>([premium, unnamed]) : null);
        var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();

        IReadOnlyList<KnownMemberGroup> groups = await new UmbracoMemberGroups(provider).GetAllAsync(default);

        Assert.That(groups, Is.EqualTo(new[] { new KnownMemberGroup(key, "Premium"), new KnownMemberGroup(Guid.Empty, "") }));
    }
}
