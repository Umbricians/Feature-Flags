using Microsoft.Extensions.Configuration;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>The flags configuration declares, in either of Microsoft's schemas.</summary>
public class ConfigurationFlagDetailsTests
{
    [Test]
    public void Names_every_flag_from_both_schemas_once_and_skips_flags_without_an_id()
    {
        ConfigurationFlagDetails details = Details("""
            { "feature_management": { "feature_flags": [
                { "id": "Checkout.NewFlow", "enabled": true },
                { "id": "checkout.newflow", "enabled": false },
                { "enabled": true },
                { "id": " ", "enabled": true } ] },
              "FeatureManagement": { "Checkout.NewFlow": true, "DarkMode": false } }
            """);

        Assert.That(details.DeclaredNames(), Is.EqualTo(new[] { "Checkout.NewFlow", "DarkMode" }));
    }

    [Test]
    public void A_site_declaring_nothing_has_no_names()
    {
        Assert.That(Details("{}").DeclaredNames(), Is.Empty);
    }

    [Test]
    public void Reads_public_from_both_schemas_and_flags_are_private_by_default()
    {
        ConfigurationFlagDetails details = Details("""
            { "feature_management": { "feature_flags": [
                { "id": "A", "enabled": true, "public": true },
                { "id": "B", "enabled": true, "public": false },
                { "id": "C", "enabled": true } ] },
              "FeatureManagement": { "D": { "EnabledFor": [ { "Name": "AlwaysOn" } ], "Public": true }, "E": true } }
            """);

        Assert.That(new[] { "A", "B", "C", "D", "E", "Missing" }.Select(name => details.Find(name).IsPublic),
            Is.EqualTo(new[] { true, false, false, true, false, false }));
    }

    private static ConfigurationFlagDetails Details(string json) =>
        new(new ConfigurationBuilder().AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).Build());
}
