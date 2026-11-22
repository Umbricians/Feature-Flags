using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbricians.FeatureFlags.Composers;
using Umbricians.FeatureFlags.Extensions;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Extensions;

/// <summary>What a site's composer does with <c>AddFeatureFlags</c>, run against Umbraco's own builder.</summary>
public class FeatureFlagsBuilderTests
{
    [Test]
    public void Declared_flags_and_group_providers_are_registered_with_the_site()
    {
        (IUmbracoBuilder builder, IServiceCollection services) = NewBuilder();

        IUmbracoBuilder returned = builder.AddFeatureFlags(flags => flags
            .Declare("Checkout.NewFlow", description: "The redesigned checkout.", tags: ["beta"])
            .Declare(FlagDefinitions.Simple("DarkMode", true), section: "UI")
            .AddGroupProvider<PremiumProvider>()
            .AddGroupProvider<PremiumProvider>());

        Assert.That(returned, Is.SameAs(builder));
        CodeFlagDeclaration[] declared = services.BuildServiceProvider().GetServices<CodeFlagDeclaration>().ToArray();
        Assert.That(declared.Select(flag => flag.Definition.Name), Is.EqualTo(new[] { "Checkout.NewFlow", "DarkMode" }));
        Assert.That(declared[0].Details, Has.Property(nameof(FlagDetails.Description)).EqualTo("The redesigned checkout.")
            .And.Property(nameof(FlagDetails.Tags)).EqualTo(new[] { "beta" }));
        Assert.That(declared[1].Details.Section, Is.EqualTo("UI"));
        Assert.That(services.Count(service => service.ServiceType == typeof(IFeatureFlagGroupProvider)), Is.EqualTo(1));
    }

    [Test]
    public void Flags_are_declared_public_in_code_or_by_name_for_any_flag_source()
    {
        (IUmbracoBuilder builder, IServiceCollection services) = NewBuilder();

        builder.AddFeatureFlags(flags => flags
            .Declare("A", isPublic: true)
            .Declare(FlagDefinitions.Simple("B", true), isPublic: true)
            .Declare("C")
            .MakePublic("fromconfig", "FromSource"));

        IServiceProvider provider = services.BuildServiceProvider();
        CodeFlagDeclaration[] declared = provider.GetServices<CodeFlagDeclaration>().ToArray();
        var publicFlags = new PublicFlags(provider.GetServices<PublicFlagNames>());
        Assert.That(declared.Select(flag => flag.Details.IsPublic), Is.EqualTo(new[] { true, true, false }));
        Assert.That(publicFlags.Contains("FromConfig"), Is.True);
        Assert.That(publicFlags.Contains("fromsource"), Is.True);
        Assert.That(publicFlags.Contains("C"), Is.False);
    }

    [Test]
    public void Declaring_a_flag_needs_a_name_and_a_definition()
    {
        (IUmbracoBuilder builder, _) = NewBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.AddFeatureFlags(null!));
        Assert.Throws<ArgumentException>(() => builder.AddFeatureFlags(flags => flags.Declare(" ")));
        Assert.Throws<ArgumentNullException>(() => builder.AddFeatureFlags(flags => flags.Declare((Microsoft.FeatureManagement.FeatureDefinition)null!)));
    }

    [Test]
    public void The_composer_registers_the_package_with_umbraco()
    {
        (IUmbracoBuilder builder, IServiceCollection services) = NewBuilder();

        new FeatureFlagsComposer().Compose(builder);

        Assert.That(services.Select(service => service.ServiceType), Does.Contain(typeof(OverrideCache)).And.Contain(typeof(OverrideService)));
        Assert.That(services.Select(service => service.ImplementationType), Does.Contain(typeof(MemberDeletedHandler))
            .And.Contain(typeof(MemberGroupDeletedHandler)).And.Contain(typeof(OverrideCacheWarmup)).And.Contain(typeof(StartupChecks)));
        Assert.That(builder.CacheRefreshers().GetType().Name, Does.Contain("CacheRefresherCollectionBuilder"));
    }

    private static (IUmbracoBuilder, IServiceCollection) NewBuilder()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var typeFinder = new TypeFinder(
            NullLogger<TypeFinder>.Instance, new DefaultUmbracoAssemblyProvider(typeof(FeatureFlagsBuilderTests).Assembly, NullLoggerFactory.Instance), null);
        var builder = new UmbracoBuilder(services, configuration, new TypeLoader(typeFinder, NullLogger<TypeLoader>.Instance));
        return (builder, services);
    }

    private sealed class PremiumProvider : IFeatureFlagGroupProvider
    {
        public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<FeatureFlagGroup>>([]);

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<string>>([]);
    }
}
