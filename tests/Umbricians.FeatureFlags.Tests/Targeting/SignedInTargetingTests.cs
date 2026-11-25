using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Tests.Targeting;

/// <summary>A visitor who isn't signed in is in no audience, whatever the percentages, as Microsoft's real manager sees it.</summary>
public class SignedInTargetingTests
{
    private const string Declared = """{ "feature_management": { "feature_flags": [ { "id": "F", "enabled": true } ] } }""";
    private const string Member = "8c2f4a10-3b6e-4d1a-9f5c-2e7b1d0a6c34";

    private static async Task<bool> OnAsync(FlagAudience audience, Action<FixedTargetingContextAccessor> visitor)
    {
        var site = new TestSite(Declared);
        site.Store.With("F", FlagSettings.ForAudience(true, audience));
        visitor(site.Visitor);
        return await site.IsOnAsync("F");
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public async Task A_visitor_who_is_not_signed_in_is_off_at_any_percentage_of_everyone_else(int percentage)
    {
        bool on = await OnAsync(new FlagAudience([], []) { DefaultPercentage = percentage }, visitor => visitor.SignOut());

        Assert.That(on, Is.False);
    }

    [Test]
    public async Task A_visitor_with_a_group_but_no_user_id_is_in_the_group_only_at_100_percent()
    {
        FlagAudience Beta(int percentage) => new([], ["Beta"]) { GroupPercentages = new Dictionary<string, int> { ["Beta"] = percentage } };

        Assert.That(await OnAsync(Beta(100), visitor => visitor.Context = new TargetingContext { Groups = ["Beta"] }), Is.True);
        Assert.That(await OnAsync(Beta(99), visitor => visitor.Context = new TargetingContext { Groups = ["Beta"] }), Is.False);
        Assert.That(await OnAsync(Beta(50), visitor => visitor.Context = new TargetingContext { Groups = ["Beta"] }), Is.False);
    }

    [Test]
    public async Task A_group_with_no_user_id_still_loses_to_an_exclusion()
    {
        var audience = new FlagAudience([], ["Beta"]) { ExcludedGroups = ["Banned"] };

        Assert.That(await OnAsync(audience, visitor => visitor.Context = new TargetingContext { Groups = ["Beta", "Banned"] }), Is.False);
    }

    [Test]
    public async Task The_percentage_of_everyone_else_includes_nobody_without_an_id_even_in_a_group_the_audience_does_not_name()
    {
        var audience = new FlagAudience([], ["Beta"]) { DefaultPercentage = 100 };

        Assert.That(await OnAsync(audience, visitor => visitor.Context = new TargetingContext { Groups = ["Other"] }), Is.False);
    }

    [Test]
    public async Task A_member_is_unaffected()
    {
        Assert.That(await OnAsync(new FlagAudience([], []) { DefaultPercentage = 100 }, visitor => visitor.SignIn(Member)), Is.True);
        Assert.That(await OnAsync(new FlagAudience([Member], []), visitor => visitor.SignIn(Member)), Is.True);
        Assert.That(await OnAsync(new FlagAudience([], ["Beta"]), visitor => visitor.SignIn(Member, "Beta")), Is.True);
        Assert.That(await OnAsync(new FlagAudience([], []) { DefaultPercentage = 0 }, visitor => visitor.SignIn(Member)), Is.False);
    }

    [Test]
    public async Task A_site_s_own_accessor_that_supplies_ids_keeps_working()
    {
        var site = new TestSite(Declared);
        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience([], []) { DefaultPercentage = 100 }));

        site.Visitor.SignIn("visitor-cookie-id");

        Assert.That(await site.IsOnAsync("F"), Is.True);
    }

    [Test]
    public void There_is_exactly_one_filter_under_the_targeting_name()
    {
        var site = new TestSite(Declared);

        var filters = site.Provider.GetServices<Microsoft.FeatureManagement.IFeatureFilterMetadata>().Select(f => f.GetType().Name).ToArray();

        Assert.That(filters, Does.Contain("SignedInTargetingFilter"));
        Assert.That(filters, Does.Not.Contain("TargetingFilter"), string.Join(",", filters));
        Assert.That(filters.Count(name => name == "SignedInContextualTargetingFilter"), Is.EqualTo(1));
        Assert.That(filters, Does.Not.Contain("ContextualTargetingFilter"), string.Join(",", filters));
    }

    private sealed class SitesOwnAccessor : ITargetingContextAccessor
    {
        public ValueTask<TargetingContext> GetContextAsync() => new(new TargetingContext { UserId = Member, Groups = [] });
    }

    private static readonly FlagSettings MemberOnly = FlagSettings.ForAudience(true, new FlagAudience([Member], []));

    [Test]
    public async Task A_site_that_added_Microsoft_targeting_before_Feature_Flags_gets_one_filter_and_audiences_evaluate()
    {
        var site = new TestSite(Declared, before: services => services.AddFeatureManagement().WithTargeting<SitesOwnAccessor>());
        site.Store.With("F", MemberOnly);
        site.Visitor.SignIn(Member);

        Assert.That(site.Provider.GetServices<IFeatureFilterMetadata>().Select(f => f.GetType()), Does.Not.Contain(typeof(TargetingFilter)));
        Assert.That(await site.IsOnAsync("F"), Is.True);
    }

    [Test]
    public async Task A_site_that_added_Microsoft_targeting_after_Feature_Flags_is_told_at_startup()
    {
        var site = new TestSite(Declared, after: services => services.AddFeatureManagement().WithTargeting<SitesOwnAccessor>());
        site.Store.With("F", MemberOnly);
        var logger = new CollectingLogger<StartupChecks>();

        await new StartupChecks(site.Provider.GetRequiredService<IServiceScopeFactory>(), logger).RunAsync(default);

        Assert.That(logger.Entries, Has.Some.Matches<(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception)>(
            entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Message.Contains("same name") && entry.Message.Contains("Microsoft.Targeting")));
    }

    [Test]
    public void The_filters_live_as_long_as_the_feature_manager_does()
    {
        var scoped = new TestSite(Declared, before: services => services.AddScopedFeatureManagement());
        var singleton = new TestSite(Declared);

        Assert.That(scoped.Services.Where(s => s.ImplementationType == typeof(SignedInTargetingFilter)).Select(s => s.Lifetime), Is.EqualTo([ServiceLifetime.Scoped]));
        Assert.That(singleton.Services.Where(s => s.ImplementationType == typeof(SignedInTargetingFilter)).Select(s => s.Lifetime), Is.EqualTo([ServiceLifetime.Singleton]));
    }

    [Test]
    public async Task A_site_with_scoped_feature_management_and_a_scoped_accessor_evaluates_audiences()
    {
        var site = new TestSite(Declared, before: services =>
        {
            services.AddScopedFeatureManagement();
            services.AddScoped<ITargetingContextAccessor, SitesOwnAccessor>();
        });
        site.Store.With("F", MemberOnly);
        using ServiceProvider strict = site.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using AsyncServiceScope scope = strict.CreateAsyncScope();

        Assert.That(await scope.ServiceProvider.GetRequiredService<IFeatureManager>().IsEnabledAsync("F"), Is.True);
    }

    [Test]
    public async Task Code_that_passes_its_own_targeting_context_gets_the_same_rule_about_percentages()
    {
        var site = new TestSite(Declared);
        site.Store.With("F", FlagSettings.ForAudience(true, new FlagAudience([], []) { DefaultPercentage = 100 }));

        Assert.That(await site.Features.IsEnabledAsync("F", new TargetingContext { Groups = [] }), Is.False);
        Assert.That(await site.Features.IsEnabledAsync("F", new TargetingContext { UserId = Member, Groups = [] }), Is.True);
    }

    [Test]
    public async Task The_targeting_context_is_read_once_for_each_check()
    {
        var site = new TestSite(Declared);
        site.Store.With("F", MemberOnly);
        site.Visitor.SignIn(Member);

        await site.IsOnAsync("F");

        Assert.That(site.Visitor.Reads, Is.EqualTo(1));
    }
}
