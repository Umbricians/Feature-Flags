using Microsoft.Extensions.DependencyInjection;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Tests.Groups;

/// <summary>What each group id in an audience is, as the editor needs to know.</summary>
public class AudienceGroupsTests
{
    private static readonly Guid PremiumKey = Guid.Parse("7f3a9c21-4e8b-4d6f-a1c5-0b2e9d4f8a67");

    private sealed class FixedGroupProvider(params FeatureFlagGroup[] groups) : IFeatureFlagGroupProvider
    {
        public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<FeatureFlagGroup>>(groups);

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<string>>([]);
    }

    private sealed class FailingGroupProvider : IFeatureFlagGroupProvider
    {
        public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("The CRM is down.");

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<string>>([]);
    }

    private static async Task<GroupDirectory> ReadAsync(TestSite site) =>
        await site.Provider.GetRequiredService<AudienceGroups>().ReadAsync(default);

    [Test]
    public async Task A_provider_group_with_a_guid_id_is_a_provider_group()
    {
        var site = new TestSite(before: s => s.AddSingleton<IFeatureFlagGroupProvider>(new FixedGroupProvider(new FeatureFlagGroup(PremiumKey.ToString(), "Premium by rule"))));
        site.MemberGroups.Add(PremiumKey, "Premium members");

        GroupDirectory directory = await ReadAsync(site);

        Assert.That(directory.Classify(PremiumKey.ToString()), Is.EqualTo(new AudienceGroup(PremiumKey.ToString(), AudienceGroupKind.ProviderGroup, "Premium by rule")));
        Assert.That(directory.ProviderGroups.Select(group => group.Id), Is.EqualTo(new[] { PremiumKey.ToString() }));
    }

    [Test]
    public async Task A_member_group_key_is_a_member_group_with_its_name()
    {
        var site = new TestSite();
        site.MemberGroups.Add(PremiumKey, "Premium members");

        GroupDirectory directory = await ReadAsync(site);

        Assert.That(directory.Classify(PremiumKey.ToString().ToUpperInvariant()),
            Is.EqualTo(new AudienceGroup(PremiumKey.ToString().ToUpperInvariant(), AudienceGroupKind.MemberGroup, "Premium members")));
    }

    [Test]
    public async Task A_member_group_named_by_name_is_a_named_member_group()
    {
        var site = new TestSite();
        site.MemberGroups.Add(PremiumKey, "Premium members");

        GroupDirectory directory = await ReadAsync(site);

        Assert.That(directory.Classify("Premium members"), Is.EqualTo(new AudienceGroup("Premium members", AudienceGroupKind.NamedMemberGroup, "Premium members", PremiumKey)));
        Assert.That(directory.Classify("premium members").Kind, Is.EqualTo(AudienceGroupKind.Unknown));
    }

    [Test]
    public async Task An_id_nobody_knows_is_unknown()
    {
        var site = new TestSite(before: s => s.AddSingleton<IFeatureFlagGroupProvider>(new FixedGroupProvider(new FeatureFlagGroup("crm:premium", "Premium in the CRM"))));

        GroupDirectory directory = await ReadAsync(site);

        Assert.That(directory.Classify(PremiumKey.ToString()), Is.EqualTo(new AudienceGroup(PremiumKey.ToString(), AudienceGroupKind.Unknown, null)));
        Assert.That(directory.Classify("crm:gone"), Is.EqualTo(new AudienceGroup("crm:gone", AudienceGroupKind.Unknown, null)));
    }

    [Test]
    public async Task A_provider_that_fails_offers_no_groups()
    {
        var logger = new CollectingLogger<AudienceGroups>();
        var groups = new AudienceGroups(
            new InMemoryMemberGroups(),
            [new FailingGroupProvider(), new FixedGroupProvider(new FeatureFlagGroup("crm:premium", "Premium"))],
            logger);

        GroupDirectory directory = await groups.ReadAsync(default);

        Assert.That(directory.ProviderGroups.Select(group => group.Id), Is.EqualTo(new[] { "crm:premium" }));
        Assert.That(logger.Entries.Single().Level, Is.EqualTo(Microsoft.Extensions.Logging.LogLevel.Warning));
        Assert.That(logger.Entries.Single().Message, Does.Contain(typeof(FailingGroupProvider).FullName));
    }

    [Test]
    public async Task Normalizing_rewrites_only_member_group_keys_and_member_keys_and_trims_the_rest()
    {
        var site = new TestSite(before: s => s.AddSingleton<IFeatureFlagGroupProvider>(new FixedGroupProvider(new FeatureFlagGroup("A1B2C3D4-0000-4000-8000-000000000000", "Odd id"))));
        site.MemberGroups.Add(PremiumKey, "Premium members");
        GroupDirectory directory = await ReadAsync(site);
        string member = "8C2F4A10-3B6E-4D1A-9F5C-2E7B1D0A6C34";

        FlagAudience normalized = directory.Normalize(new FlagAudience(
            [member, member.ToLowerInvariant(), " ", "someone"],
            [PremiumKey.ToString().ToUpperInvariant(), " Premium members ", "A1B2C3D4-0000-4000-8000-000000000000", "B1B2C3D4-0000-4000-8000-000000000000", "", "Premium members"]));

        Assert.That(normalized.Members, Is.EqualTo(new[] { member.ToLowerInvariant(), "someone" }));
        Assert.That(normalized.Groups, Is.EqualTo(new[] { PremiumKey.ToString(), "Premium members", "A1B2C3D4-0000-4000-8000-000000000000", "B1B2C3D4-0000-4000-8000-000000000000" }));
    }

    [Test]
    public async Task Groups_that_normalize_to_one_id_keep_the_first_ones_percentage_even_when_a_later_one_is_in_full()
    {
        var site = new TestSite();
        site.MemberGroups.Add(PremiumKey, "Premium members");
        GroupDirectory directory = await ReadAsync(site);
        string upper = PremiumKey.ToString().ToUpperInvariant();

        FlagAudience normalized = directory.Normalize(new FlagAudience([], [PremiumKey.ToString(), upper])
        {
            GroupPercentages = new Dictionary<string, int> { [PremiumKey.ToString()] = 40 },
        });

        Assert.That(normalized.Groups, Is.EqualTo(new[] { PremiumKey.ToString() }));
        Assert.That(normalized.PercentageOf(PremiumKey.ToString()), Is.EqualTo(40));
    }

    [Test]
    public async Task A_site_without_member_groups_knows_only_its_providers()
    {
        var site = new TestSite();

        GroupDirectory directory = await ReadAsync(site);

        Assert.That(directory.ProviderGroups, Is.Empty);
        Assert.That(directory.Classify("anything").Kind, Is.EqualTo(AudienceGroupKind.Unknown));
    }
}
