using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbricians.FeatureFlags.Composers;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Tests.Composers;

/// <summary>How Feature Flags fits with however the site set up Microsoft.FeatureManagement itself.</summary>
public class FeatureManagementRegistrationTests
{
    private const string TimeWindowFlag = """
        { "feature_management": { "feature_flags": [ { "id": "Sale", "enabled": true,
          "conditions": { "client_filters": [ { "name": "Microsoft.TimeWindow", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } } ] } }
        """;

    [Test]
    public async Task Works_when_the_site_already_added_Microsoft_FeatureManagement()
    {
        var site = new TestSite(TimeWindowFlag, before: services => services.AddFeatureManagement());
        site.Store.With("Sale", FlagSettings.Off);

        // Adding Microsoft's services twice would register the time window filter twice, and this check would throw.
        Assert.That(await site.IsOnAsync("Sale"), Is.False);
    }

    [Test]
    public void Registering_Feature_Flags_twice_changes_nothing()
    {
        var site = new TestSite(after: services => services.AddFeatureFlags(new ConfigurationBuilder().Build()));

        Assert.That(site.Services.Count(s => s.ServiceType == typeof(IFeatureDefinitionProvider)), Is.EqualTo(1));
        Assert.That(site.Services.Count(s => s.ServiceType == typeof(OverrideCache)), Is.EqualTo(1));
    }

    [Test]
    public async Task Wraps_a_flag_source_the_site_registered_itself()
    {
        var site = new TestSite(before: services =>
        {
            services.AddFeatureManagement();
            services.AddSingleton<IFeatureDefinitionProvider>(new SiteFlagSource("FromTheSite"));
        });
        bool declared = await site.IsOnAsync("FromTheSite");
        site.Store.With("FromTheSite", FlagSettings.Off);
        site.Refresh();
        bool overridden = await site.IsOnAsync("FromTheSite");

        Assert.That((declared, overridden), Is.EqualTo((true, false)));
        Assert.That(site.Provider.GetRequiredService<FlagSource>().DeclaredIn, Is.EqualTo(FeatureFlagDeclaredIn.FlagSource));
    }

    [Test]
    public void Says_flags_from_configuration_are_declared_in_configuration()
    {
        var site = new TestSite();

        Assert.That(site.Provider.GetRequiredService<FlagSource>().DeclaredIn, Is.EqualTo(FeatureFlagDeclaredIn.Configuration));
    }

    [Test]
    public void Identifies_members_unless_the_site_identifies_visitors_itself()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddFeatureFlags(new ConfigurationBuilder().Build());

        var site = new TestSite();

        Assert.That(services.BuildServiceProvider().GetRequiredService<ITargetingContextAccessor>(),
            Is.InstanceOf<MemberTargetingContextAccessor>());
        Assert.That(site.Provider.GetRequiredService<ITargetingContextAccessor>(), Is.SameAs(site.Visitor));
    }

    [Test]
    public void Replaces_Microsofts_targeting_filter_with_its_own_so_the_name_is_used_once()
    {
        var site = new TestSite(before: services => services.AddFeatureManagement().WithTargeting<FixedTargetingContextAccessor>());

        Assert.That(site.Services.Count(s => s.ImplementationType == typeof(TargetingFilter)), Is.Zero);
        Assert.That(site.Services.Count(s => s.ImplementationType == typeof(SignedInTargetingFilter)), Is.EqualTo(1));
    }

    private sealed class SiteFlagSource(string name) : IFeatureDefinitionProvider
    {
        public Task<FeatureDefinition> GetFeatureDefinitionAsync(string featureName) =>
            Task.FromResult(featureName == name ? FlagDefinitions.Simple(name, enabled: true) : null!);

        public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
        {
            await Task.CompletedTask;
            yield return FlagDefinitions.Simple(name, enabled: true);
        }
    }
}
