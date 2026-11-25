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
        Assert.That(entries[0].Message, Does.Contain("BetaSearch").And.Contain("both in code and by the site's flag source"));
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

        Assert.That(entries.Select(entry => entry.Message), Has.Some.Contain("flag source can't list its flags"));
        Assert.That(entries.Select(entry => entry.Message), Has.Some.Contain("declared both in code and by the site's flag source, and the flag source's declaration is used: B"));
        Assert.That(entries.Where(entry => entry.Message.Contains("flag source can't list its flags")).Single().Exception, Is.Not.Null);
    }

    [Test]
    public async Task A_flag_source_registered_after_Feature_Flags_is_an_error()
    {
        var site = new TestSite(after: services => services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource()));

        var entries = await Run(site);

        Assert.That(entries, Has.Some.Matches<(LogLevel Level, string Message, Exception? Exception)>(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("registered its own flag source after Feature Flags")));
    }

    [Test]
    public async Task Microsoft_FeatureManagement_registered_again_is_an_error()
    {
        var site = new TestSite(after: services => services.AddFeatureManagement());

        var entries = await Run(site);

        Assert.That(entries, Has.Some.Matches<(LogLevel Level, string Message, Exception? Exception)>(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("registered more than once")));
    }

    private static IServiceScopeFactory Scopes(IServiceProvider provider) => provider.GetRequiredService<IServiceScopeFactory>();

    private static async Task<List<(LogLevel Level, string Message, Exception? Exception)>> Run(TestSite site)
    {
        var logger = new CollectingLogger<StartupChecks>();
        await new StartupChecks(Scopes(site.Provider), logger).RunAsync(default);
        return logger.Entries;
    }

    [Test]
    public async Task A_check_that_throws_is_logged_as_a_warning_and_never_thrown()
    {
        var logger = new CollectingLogger<StartupChecks>();

        await new StartupChecks(Scopes(new ServiceCollection().BuildServiceProvider()), logger).RunAsync(default);

        Assert.That(logger.Entries.Single().Level, Is.EqualTo(LogLevel.Warning));
    }

    [Test]
    public async Task Nothing_runs_while_umbraco_is_installing_or_upgrading()
    {
        var site = new TestSite(after: services => services.AddFeatureManagement());
        var logger = new CollectingLogger<StartupChecks>();
        var runtime = Stub.Of<Umbraco.Cms.Core.Services.IRuntimeState>((name, _) => name == "get_Level" ? Umbraco.Cms.Core.RuntimeLevel.Upgrade : null);
        var services = new ServiceCollection().AddSingleton(runtime).AddSingleton(site.Provider.GetRequiredService<Umbricians.FeatureFlags.Diagnostics.FlagDiagnostics>());

        await new StartupChecks(Scopes(services.BuildServiceProvider()), logger).HandleAsync(new UmbracoApplicationStartedNotification(false), default);
        await Task.Delay(200);

        Assert.That(logger.Entries, Is.Empty);
    }

    [Test]
    public async Task Startup_does_not_wait_for_the_checks()
    {
        var site = new TestSite(after: services => services.AddFeatureManagement());
        var logger = new CollectingLogger<StartupChecks>();

        await new StartupChecks(Scopes(site.Provider), logger).HandleAsync(new UmbracoApplicationStartedNotification(false), default);

        // The checks run in the background and log what they find when they finish.
        DateTime giveUp = DateTime.UtcNow.AddSeconds(5);
        while (logger.Entries.Count == 0 && DateTime.UtcNow < giveUp)
        {
            await Task.Delay(20);
        }

        Assert.That(logger.Entries, Has.Some.Matches<(LogLevel Level, string Message, Exception? Exception)>(entry => entry.Level == LogLevel.Error));
    }

    [Test]
    public async Task The_checks_still_run_after_the_scope_the_handler_came_from_has_ended()
    {
        // Umbraco resolves the handler from a scope that ends as soon as the notification is handled, while the checks
        // carry on in the background, so they can start after it has gone: issue #78. Ending the scope before the checks
        // run makes that order certain rather than a race.
        var site = new TestSite(after: services => services.AddFeatureManagement());
        var logger = new CollectingLogger<StartupChecks>();
        StartupChecks checks;
        using (IServiceScope handlerScope = site.Provider.CreateScope())
        {
            checks = new StartupChecks(handlerScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), logger);
        }

        await checks.RunAsync(default);

        Assert.That(logger.Entries, Has.None.Matches<(LogLevel Level, string Message, Exception? Exception)>(entry => entry.Message.Contains("couldn't run its startup checks")));
        Assert.That(logger.Entries, Has.Some.Matches<(LogLevel Level, string Message, Exception? Exception)>(entry => entry.Level == LogLevel.Error && entry.Message.Contains("registered more than once")));
    }
}
