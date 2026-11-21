using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Tests.Targeting;

/// <summary>Who a flag is checked for, worked out from the signed-in member.</summary>
public class MemberTargetingContextAccessorTests
{
    private static readonly Guid MemberKey = Guid.NewGuid();
    private static readonly Guid PremiumKey = Guid.NewGuid();

    [Test]
    public async Task Without_a_request_the_visitor_is_anonymous()
    {
        var accessor = Accessor(new HttpContextAccessor());

        TargetingContext context = await accessor.GetContextAsync();

        Assert.That(context, Has.Property(nameof(TargetingContext.UserId)).Null.And.Property(nameof(TargetingContext.Groups)).Empty);
    }

    [Test]
    public async Task A_visitor_who_is_not_signed_in_is_anonymous()
    {
        TargetingContext context = await Accessor(Request(signedIn: false)).GetContextAsync();

        Assert.That(context, Has.Property(nameof(TargetingContext.UserId)).Null.And.Property(nameof(TargetingContext.Groups)).Empty);
    }

    [Test]
    public async Task A_site_without_member_services_has_only_anonymous_visitors()
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() } };

        TargetingContext context = await Accessor(http).GetContextAsync();

        Assert.That(context.UserId, Is.Null);
    }

    [Test]
    public async Task A_signed_in_member_is_their_key_with_the_key_and_name_of_each_of_their_groups_and_each_providers_groups()
    {
        var provider = new Provider(_ => ["crm:premium", TargetingIds.For(PremiumKey)]);

        TargetingContext context = await Accessor(Request(), provider).GetContextAsync();

        Assert.That(context.UserId, Is.EqualTo(TargetingIds.For(MemberKey)));
        Assert.That(context.Groups, Is.EquivalentTo(new[] { "Premium", TargetingIds.For(PremiumKey), "Unknown", "crm:premium" }));
        Assert.That(provider.Seen, Is.EqualTo(new FeatureFlagMember(MemberKey, "ada", "ada@example.com", ["Premium", "Unknown"])).Using<FeatureFlagMember>(
            (a, b) => a.Key == b.Key && a.UserName == b.UserName && a.Email == b.Email && a.MemberGroups.SequenceEqual(b.MemberGroups)));
    }

    [Test]
    public async Task A_member_is_worked_out_once_per_request()
    {
        var provider = new Provider(_ => []);
        IHttpContextAccessor http = Request();
        MemberTargetingContextAccessor accessor = Accessor(http, provider);

        TargetingContext first = await accessor.GetContextAsync();
        TargetingContext second = await accessor.GetContextAsync();

        Assert.That(second, Is.SameAs(first));
        Assert.That(provider.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task A_group_provider_that_fails_costs_the_member_only_its_own_groups_and_is_logged()
    {
        var logger = new CollectingLogger<MemberTargetingContextAccessor>();
        var broken = new Provider(_ => throw new InvalidOperationException("CRM is down."));
        var working = new Provider(_ => ["crm:premium"]);

        TargetingContext context = await Accessor(Request(), logger, broken, working).GetContextAsync();

        Assert.That(context.Groups, Does.Contain("crm:premium").And.Contain("Premium"));
        Assert.That(logger.Entries.Single().Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(logger.Entries.Single().Message, Does.Contain(typeof(Provider).FullName));
    }

    [Test]
    public async Task When_the_member_cannot_be_looked_up_the_visitor_is_anonymous_and_a_warning_is_logged()
    {
        var logger = new CollectingLogger<MemberTargetingContextAccessor>();
        IHttpContextAccessor http = Request(memberManagerFails: true);

        TargetingContext context = await Accessor(http, logger).GetContextAsync();
        await Accessor(http, logger).GetContextAsync();

        Assert.That(context, Has.Property(nameof(TargetingContext.UserId)).Null.And.Property(nameof(TargetingContext.Groups)).Empty);
        Assert.That(logger.Entries, Has.Count.EqualTo(1));
        Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(logger.Entries[0].Exception, Is.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void A_request_that_is_cancelled_is_not_hidden_as_an_anonymous_visitor()
    {
        IHttpContextAccessor http = Request(memberManagerFails: true, failWith: new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(async () => await Accessor(http).GetContextAsync());
    }

    private static MemberTargetingContextAccessor Accessor(IHttpContextAccessor http, params IFeatureFlagGroupProvider[] providers) =>
        Accessor(http, new CollectingLogger<MemberTargetingContextAccessor>(), providers);

    private static MemberTargetingContextAccessor Accessor(
        IHttpContextAccessor http, ILogger<MemberTargetingContextAccessor> logger, params IFeatureFlagGroupProvider[] providers) =>
        new(http, providers, logger);

    private static IHttpContextAccessor Request(bool signedIn = true, bool memberManagerFails = false, Exception? failWith = null)
    {
        var services = new ServiceCollection();
        var member = new MemberIdentityUser { Key = MemberKey, UserName = "ada", Email = "ada@example.com" };
        services.AddSingleton(Stub.Of<IMemberManager>((name, _) => name switch
        {
            "GetCurrentMemberAsync" when memberManagerFails => Task.FromException<MemberIdentityUser?>(failWith ?? new InvalidOperationException("No member store.")),
            "GetCurrentMemberAsync" => Task.FromResult<MemberIdentityUser?>(signedIn ? member : null),
            "GetRolesAsync" => Task.FromResult<IList<string>>(["Premium", "Unknown"]),
            _ => null,
        }));
        services.AddSingleton(Stub.Of<IMemberGroupService>((name, args) => name == "GetByNameAsync"
            ? Task.FromResult((string)args[0]! == "Premium" ? Stub.Of<IMemberGroup>((n, _) => n == "get_Key" ? PremiumKey : null) : null)
            : null));
        return new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() } };
    }

    private sealed class Provider(Func<FeatureFlagMember, string[]> groups) : IFeatureFlagGroupProvider
    {
        public int Calls { get; private set; }

        public FeatureFlagMember? Seen { get; private set; }

        public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<FeatureFlagGroup>>([]);

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken)
        {
            Calls++;
            Seen = member;
            return Task.FromResult<IEnumerable<string>>(groups(member));
        }
    }
}
