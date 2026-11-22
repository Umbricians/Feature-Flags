using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbricians.FeatureFlags.Flags;

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

    [TestCase(50)]
    [TestCase(100)]
    public async Task A_visitor_who_is_not_signed_in_is_off_in_a_group_even_when_the_group_is_in_full(int percentage)
    {
        var audience = new FlagAudience([], ["Beta"]) { GroupPercentages = new Dictionary<string, int> { ["Beta"] = percentage } };

        bool on = await OnAsync(audience, visitor => visitor.Context = new TargetingContext { Groups = ["Beta"] });

        Assert.That(on, Is.False);
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
    }
}
