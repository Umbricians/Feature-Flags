using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.HealthChecks;

namespace Umbricians.FeatureFlags.IntegrationTests.Overrides;

/// <summary>The health check as Umbraco finds and runs it.</summary>
public class HealthCheckTests : IntegrationTest
{
    [Test]
    public async Task Umbraco_finds_the_Feature_Flags_health_check_and_runs_it()
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        HealthCheckCollection checks = scope.ServiceProvider.GetRequiredService<HealthCheckCollection>();

        HealthCheck check = checks.Single(check => check.Group == "Feature Flags");
        HealthCheckStatus[] statuses = (await check.GetStatusAsync()).ToArray();

        Assert.That(check.Name, Is.EqualTo("Feature Flags"));
        Assert.That(statuses, Is.Not.Empty);
        Assert.That(statuses.Select(status => status.ResultType), Does.Not.Contain(StatusResultType.Error), string.Join("; ", statuses.Select(s => s.Message)));
    }
}
