using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>What a site is told at startup about set-ups that quietly stop flags working as expected.</summary>
public class StartupChecksTests
{
    [Test]
    public async Task A_site_set_up_correctly_is_told_nothing()
    {
        var site = new TestSite("""{ "FeatureManagement": { "BetaSearch": true } }""");

        Assert.That(await Run(site), Is.Empty);
    }

    [Test]
    public async Task A_flag_declared_in_code_and_by_the_flag_source_is_warned_about()
    {
        var site = new TestSite("""{ "FeatureManagement": { "BetaSearch": true } }""", before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("BetaSearch", false), FlagDetails.None)));

        var entries = await Run(site);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entries[0].Message, Does.Contain("'BetaSearch'").And.Contain("both in code and by the site's flag source"));
    }

    [Test]
    public async Task A_flag_source_that_can_not_list_its_flags_is_warned_about_and_its_readable_flags_still_checked()
    {
        var site = new TestSite(before: services =>
        {
            services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource());
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("B", false), FlagDetails.None));
        });

        var entries = await Run(site);

        Assert.That(entries.Select(entry => entry.Message), Has.Some.Contain("couldn't list the flags the site's flag source declares"));
        Assert.That(entries.Select(entry => entry.Message), Has.Some.Contain("'B'"));
        Assert.That(entries.Where(entry => entry.Message.Contains("couldn't list the flags the site's flag source declares")).Single().Exception, Is.Not.Null);
    }

    [Test]
    public async Task A_flag_source_registered_after_Feature_Flags_is_warned_about()
    {
        var site = new TestSite(after: services => services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource()));

        var entries = await Run(site);

        Assert.That(entries.Select(entry => entry.Message), Has.Some.Contain("registered its own flag source after Feature Flags"));
    }

    [Test]
    public async Task Microsoft_FeatureManagement_registered_again_is_an_error()
    {
        var site = new TestSite(after: services => services.AddFeatureManagement());

        var entries = await Run(site);

        Assert.That(entries, Has.Some.Matches<(LogLevel Level, string Message, Exception? Exception)>(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("registered more than once")));
    }

    private static async Task<List<(LogLevel Level, string Message, Exception? Exception)>> Run(TestSite site)
    {
        var logger = new CollectingLogger<StartupChecks>();
        await new StartupChecks(site.Provider, logger).HandleAsync(new UmbracoApplicationStartedNotification(false), default);
        return logger.Entries;
    }
}
