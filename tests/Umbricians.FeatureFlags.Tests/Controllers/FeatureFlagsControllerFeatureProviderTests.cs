using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Umbricians.FeatureFlags.Controllers;

namespace Umbricians.FeatureFlags.Tests.Controllers;

/// <summary>How MVC finds the package's internal controllers.</summary>
public class FeatureFlagsControllerFeatureProviderTests
{
    [Test]
    public void Finds_the_packages_internal_controllers_and_nothing_else_in_the_package()
    {
        Type[] found = Populate(typeof(FeatureFlagsControllerBase).Assembly);

        Assert.That(found, Is.Not.Empty);
        Assert.That(found, Has.All.Matches<Type>(type => type!.Assembly == typeof(FeatureFlagsControllerBase).Assembly
            && !type.IsPublic && !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)));
        Assert.That(found, Does.Not.Contain(typeof(FeatureFlagsControllerBase)));
    }

    [Test]
    public void Leaves_other_assemblies_controllers_to_MVC()
    {
        Assert.That(Populate(typeof(FeatureFlagsControllerFeatureProviderTests).Assembly), Is.Empty);
    }

    private static Type[] Populate(System.Reflection.Assembly assembly)
    {
        var feature = new ControllerFeature();
        new FeatureFlagsControllerFeatureProvider().PopulateFeature([new AssemblyPart(assembly)], feature);
        return feature.Controllers.Select(controller => controller.AsType()).ToArray();
    }
}
