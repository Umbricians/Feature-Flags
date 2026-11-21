using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Demo.DemoFlags;

/// <summary>
/// A sample group provider: members whose email is at example.com. A real site might ask its CRM or look at when a
/// member joined.
/// </summary>
public class ExampleDomainGroupProvider : IFeatureFlagGroupProvider
{
    private const string GroupId = "demo:example-com";

    public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<FeatureFlagGroup>>(
            [new FeatureFlagGroup(GroupId, "example.com members", "Members whose email address is at example.com.")]);

    public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<string>>(
            member.Email?.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase) == true ? [GroupId] : []);
}
