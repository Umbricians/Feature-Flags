using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Diagnostics;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.HealthChecks;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Tests.Diagnostics;

/// <summary>Every row of the health check, and that the startup log says the same.</summary>
public class FlagDiagnosticsTests
{
    private sealed class NoRoots : ISiteRoots
    {
        public SiteRoot? Find(Guid key) => null;

        public Task<IReadOnlyList<SiteRoot>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SiteRoot>>([]);

        public Task<IReadOnlyList<string>> GetDomainsAsync(Guid rootKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FailingGroups : IFeatureFlagGroupProvider
    {
        public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("The CRM is down.");

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<string>>([]);
    }

    private sealed class HangingGroups : IFeatureFlagGroupProvider
    {
        public async Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, CancellationToken.None);
            return [];
        }

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<string>>([]);
    }

    private static async Task<IReadOnlyList<DiagnosticResult>> Run(TestSite site) =>
        await site.Provider.GetRequiredService<FlagDiagnostics>().RunAsync(default);

    private static DiagnosticResult Only(IReadOnlyList<DiagnosticResult> results, string anchor) => results.Single(result => result.Anchor == anchor);

    [Test]
    public async Task A_site_set_up_correctly_is_all_clear()
    {
        IReadOnlyList<DiagnosticResult> results = await Run(new TestSite("""{ "FeatureManagement": { "BetaSearch": true } }"""));

        Assert.That(results.Select(result => result.Severity), Is.EqualTo(new[] { DiagnosticSeverity.Success }));
    }

    [Test]
    public async Task Saved_settings_that_can_not_be_read_are_an_error()
    {
        var site = new TestSite();
        site.Store.Fail = true;

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.SettingsUnreadable);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(result.Exception, Is.Not.Null);
    }

    [Test]
    public async Task Microsoft_FeatureManagement_registered_twice_is_an_error()
    {
        IReadOnlyList<DiagnosticResult> results = await Run(new TestSite(after: services => services.AddFeatureManagement()));

        Assert.That(Only(results, FlagDiagnostics.RegisteredTwice).Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [Test]
    public async Task Two_filters_of_the_same_kind_with_one_name_are_an_error()
    {
        var site = new TestSite(after: services => services.AddFeatureManagement().WithTargeting<FixedTargetingContextAccessor>());

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.FiltersShareAName);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(result.Message, Does.Contain("Microsoft.Targeting"));
    }

    [Test]
    public async Task A_flag_source_registered_after_Feature_Flags_is_an_error()
    {
        var site = new TestSite(after: services => services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource()));

        Assert.That(Only(await Run(site), FlagDiagnostics.FlagSourceLate).Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [Test]
    public async Task A_saved_setting_that_can_not_be_read_is_a_warning_naming_the_flag()
    {
        var site = new TestSite("""{ "FeatureManagement": { "Shop": true } }""");
        site.Store.WithJson("Shop", "{ not json");

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.SettingUnreadable);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Flags, Is.EqualTo(new[] { "Shop" }));
        Assert.That(result.Message, Does.Contain("Shop"));
    }

    [Test]
    public async Task A_declared_flag_whose_definition_can_not_be_read_is_a_warning_naming_the_flag()
    {
        var site = new TestSite(before: services =>
        {
            services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource());
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("Broken", false), FlagDetails.None));
        });

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.DefinitionUnreadable);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Flags, Is.EqualTo(new[] { "Broken" }));
    }

    [Test]
    public async Task A_flag_source_that_can_not_list_its_flags_is_a_warning()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource()));

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.ListingFailed);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Exception, Is.Not.Null);
    }

    [Test]
    public async Task A_group_provider_that_fails_is_a_warning()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureFlagGroupProvider>(new FailingGroups()));

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.GroupProviderFailed);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Message, Does.Contain(nameof(FailingGroups)));
    }

    [Test]
    public async Task A_group_provider_that_never_answers_is_a_warning_and_does_not_hang_the_check()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureFlagGroupProvider>(new HangingGroups()));
        IReadOnlyList<string> failing = await site.Provider.GetRequiredService<AudienceGroups>()
            .FindFailingProvidersAsync(TimeSpan.FromMilliseconds(50), default);

        Assert.That(failing, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_group_provider_that_answers_is_not_reported()
    {
        var site = new TestSite(before: services => services.AddSingleton<IFeatureFlagGroupProvider, EmptyGroups>());

        Assert.That((await Run(site)).Select(result => result.Anchor), Does.Not.Contain(FlagDiagnostics.GroupProviderFailed));
    }

    private sealed class EmptyGroups : IFeatureFlagGroupProvider
    {
        public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<FeatureFlagGroup>>([]);

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<string>>([]);
    }

    [Test]
    public async Task A_flag_declared_in_code_and_by_the_flag_source_is_a_warning_naming_the_flag()
    {
        var site = new TestSite("""{ "FeatureManagement": { "BetaSearch": true } }""", before: services =>
            services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("BetaSearch", false), FlagDetails.None)));

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.DeclaredTwice);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Flags, Is.EqualTo(new[] { "BetaSearch" }));
    }

    [Test]
    public async Task A_Root_condition_naming_a_deleted_root_is_a_warning_naming_the_flag()
    {
        string flag = $$"""
            { "feature_management": { "feature_flags": [ { "id": "Shop", "enabled": true,
              "conditions": { "client_filters": [ { "name": "Umbricians.Root", "parameters": { "Roots": [ "{{Guid.NewGuid()}}" ] } } ] } } ] } }
            """;
        var site = new TestSite(flag, before: services => services.AddSingleton<ISiteRoots>(new NoRoots()));

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.DeletedRoot);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Flags, Is.EqualTo(new[] { "Shop" }));
    }

    [Test]
    public async Task The_headless_endpoint_switched_on_with_no_public_flag_is_a_warning()
    {
        var site = new TestSite("""{ "Umbricians": { "FeatureFlags": { "Headless": { "Enabled": true } } }, "FeatureManagement": { "Shop": true } }""");

        Assert.That(Only(await Run(site), FlagDiagnostics.HeadlessNothingPublic).Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    [Test]
    public async Task The_headless_endpoint_with_a_public_flag_or_switched_off_is_all_clear()
    {
        var withPublic = new TestSite("""{ "Umbricians": { "FeatureFlags": { "Headless": { "Enabled": true } } }, "feature_management": { "feature_flags": [ { "id": "Shop", "enabled": true, "public": true } ] } }""");
        var off = new TestSite("""{ "FeatureManagement": { "Shop": true } }""");

        Assert.That((await Run(withPublic)).Select(result => result.Anchor), Does.Not.Contain(FlagDiagnostics.HeadlessNothingPublic));
        Assert.That((await Run(off)).Select(result => result.Anchor), Does.Not.Contain(FlagDiagnostics.HeadlessNothingPublic));
    }

    [Test]
    public async Task Undeclared_flags_are_counted_without_names()
    {
        var site = new TestSite();
        site.Store.With("Gone", FlagSettings.Off);
        site.Store.With("AlsoGone", FlagSettings.Off);

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.Undeclared);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Info));
        Assert.That(result.Message, Does.Contain("2 flags").And.Not.Contain("Gone"));
        Assert.That(result.Flags, Is.Empty);
    }

    [Test]
    public async Task Read_only_overrides_are_info()
    {
        var site = new TestSite("""{ "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } } }""");

        Assert.That(Only(await Run(site), FlagDiagnostics.ReadOnly).Severity, Is.EqualTo(DiagnosticSeverity.Info));
    }

    [Test]
    public async Task Flags_in_a_custom_state_are_counted_without_names()
    {
        var site = new TestSite("""{ "FeatureManagement": { "Odd": { "EnabledFor": [ { "Name": "SomethingNobodyRegistered" } ] } } }""");

        DiagnosticResult result = Only(await Run(site), FlagDiagnostics.Custom);

        Assert.That(result.Severity, Is.EqualTo(DiagnosticSeverity.Info));
        Assert.That(result.Message, Does.Contain("1 flag").And.Not.Contain("Odd"));
    }

    [Test]
    public async Task Info_alone_is_still_a_success()
    {
        var site = new TestSite("""{ "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } } }""");

        Assert.That((await Run(site)).Select(result => result.Severity), Is.EqualTo(new[] { DiagnosticSeverity.Info, DiagnosticSeverity.Success }));
    }

    [Test]
    public async Task A_check_that_throws_is_a_warning_and_the_rest_still_run()
    {
        // A flag with a Root condition makes the roots get asked.
        var withRoot = new TestSite($$"""
            { "feature_management": { "feature_flags": [ { "id": "Shop", "enabled": true,
              "conditions": { "client_filters": [ { "name": "Umbricians.Root", "parameters": { "Roots": [ "{{Guid.NewGuid()}}" ] } } ] } } ] } }
            """, before: services => services.AddSingleton<ISiteRoots>(new ThrowingRoots()));

        IReadOnlyList<DiagnosticResult> results = await Run(withRoot);

        Assert.That(Only(results, FlagDiagnostics.CheckFailed).Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    private sealed class ThrowingRoots : ISiteRoots
    {
        public SiteRoot? Find(Guid key) => throw new InvalidOperationException("No content cache yet.");

        public Task<IReadOnlyList<SiteRoot>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SiteRoot>>([]);

        public Task<IReadOnlyList<string>> GetDomainsAsync(Guid rootKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    [Test]
    public async Task The_health_check_returns_each_result_with_its_type_and_a_link_to_the_docs()
    {
        var site = new TestSite("""{ "Umbricians": { "FeatureFlags": { "Overrides": "ReadOnly" } } }""", after: services => services.AddFeatureManagement());
        var check = new FeatureFlagsHealthCheck(site.Provider.GetRequiredService<FlagDiagnostics>());

        HealthCheckStatus[] statuses = (await check.GetStatusAsync()).ToArray();

        Assert.That(statuses.Select(status => status.ResultType), Does.Contain(StatusResultType.Error).And.Contain(StatusResultType.Info));
        Assert.That(statuses.Select(status => status.ReadMoreLink), Does.Contain(FeatureFlagsHealthCheck.DocsUrl + FlagDiagnostics.RegisteredTwice));
        Assert.That(statuses.SelectMany(status => status.Actions ?? []), Is.Empty);
        Assert.That(check.Group, Is.EqualTo("Feature Flags"));
        Assert.Throws<InvalidOperationException>(() => check.ExecuteAction(new HealthCheckAction("x", Guid.Empty)));
    }

    [Test]
    public async Task The_health_check_of_a_correct_site_is_one_success()
    {
        var check = new FeatureFlagsHealthCheck(new TestSite().Provider.GetRequiredService<FlagDiagnostics>());

        Assert.That((await check.GetStatusAsync()).Select(status => status.ResultType), Is.EqualTo(new[] { StatusResultType.Success }));
    }

    [Test]
    public async Task The_startup_log_says_what_the_health_check_returns_as_errors_and_warnings()
    {
        var site = new TestSite(
            """{ "FeatureManagement": { "BetaSearch": true } }""",
            before: services => services.AddSingleton(new CodeFlagDeclaration(FlagDefinitions.Simple("BetaSearch", false), FlagDetails.None)),
            after: services =>
            {
                services.AddFeatureManagement();
                services.AddSingleton<IFeatureDefinitionProvider>(new FailingListSource());
            });
        site.Store.With("Gone", FlagSettings.Off);
        var logger = new CollectingLogger<StartupChecks>();

        await new StartupChecks(site.Provider, logger).HandleAsync(new UmbracoApplicationStartedNotification(false), default);
        var check = new FeatureFlagsHealthCheck(site.Provider.GetRequiredService<FlagDiagnostics>());
        HealthCheckStatus[] statuses = (await check.GetStatusAsync()).ToArray();

        (LogLevel, string)[] logged = logger.Entries.Select(entry => (entry.Level, entry.Message)).ToArray();
        (LogLevel, string)[] shown = statuses
            .Where(status => status.ResultType is StatusResultType.Error or StatusResultType.Warning)
            .Select(status => (status.ResultType == StatusResultType.Error ? LogLevel.Error : LogLevel.Warning, status.Message))
            .ToArray();
        Assert.That(shown, Is.Not.Empty);
        Assert.That(logged, Is.EqualTo(shown));
        Assert.That(logged.Select(entry => entry.Item1), Does.Contain(LogLevel.Error).And.Contain(LogLevel.Warning));
    }

    [Test]
    public async Task Scoped_feature_management_is_checked_without_resolving_scoped_filters_from_the_root()
    {
        var site = new TestSite(before: services => services.AddScopedFeatureManagement());
        await using ServiceProvider validating = site.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var logger = new CollectingLogger<StartupChecks>();

        await new StartupChecks(validating, logger).HandleAsync(new UmbracoApplicationStartedNotification(false), default);

        Assert.That(logger.Entries, Is.Empty);
    }
}
