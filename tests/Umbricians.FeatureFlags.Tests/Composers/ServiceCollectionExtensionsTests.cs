using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Umbricians.FeatureFlags.Composers;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Controllers;

namespace Umbricians.FeatureFlags.Tests.Composers;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public void Lets_MVC_find_the_package_controllers()
    {
        ServiceCollection services = WithPackageServices();

        var partManager = (ApplicationPartManager)services
            .Single(service => service.ServiceType == typeof(ApplicationPartManager)).ImplementationInstance!;

        Assert.That(partManager.FeatureProviders, Has.Some.InstanceOf<FeatureFlagsControllerFeatureProvider>());
    }

    [Test]
    public void Keeps_a_time_provider_the_site_registered()
    {
        var siteTimeProvider = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(siteTimeProvider);

        services.AddFeatureFlags(new ConfigurationBuilder().Build());

        Assert.That(services.BuildServiceProvider().GetRequiredService<TimeProvider>(), Is.SameAs(siteTimeProvider));
    }

    [Test]
    public void Validates_the_options_from_their_annotations()
    {
        ServiceProvider provider = WithPackageServices().BuildServiceProvider();

        Assert.That(provider.GetServices<IValidateOptions<FeatureFlagsOptions>>(), Is.Not.Empty);
    }

    [Test]
    public void Validates_the_options_when_the_site_starts()
    {
        ServiceCollection services = WithPackageServices();
        services.AddSingleton<IValidateOptions<FeatureFlagsOptions>>(
            new ValidateOptions<FeatureFlagsOptions>(Options.DefaultName, _ => false, "Rejected by the test."));

        IStartupValidator startupValidator = services.BuildServiceProvider().GetRequiredService<IStartupValidator>();

        Assert.That(startupValidator.Validate, Throws.TypeOf<OptionsValidationException>());
    }

    [TestCase("Nonsense")]
    [TestCase("5")]
    public void An_overrides_setting_that_isnt_Enabled_or_ReadOnly_stops_the_site_starting(string value)
    {
        var services = new ServiceCollection();
        services.AddFeatureFlags(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Umbricians:FeatureFlags:Overrides"] = value })
            .Build());

        IStartupValidator startupValidator = services.BuildServiceProvider().GetRequiredService<IStartupValidator>();

        Assert.That(startupValidator.Validate, Throws.InstanceOf<Exception>());
    }

    private static ServiceCollection WithPackageServices()
    {
        var services = new ServiceCollection();
        services.AddFeatureFlags(new ConfigurationBuilder().Build());
        return services;
    }
}
