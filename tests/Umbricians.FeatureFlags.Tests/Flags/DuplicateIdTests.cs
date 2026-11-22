using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>When configuration repeats a flag's id, Feature Flags reads the same entry Microsoft's provider does.</summary>
public class DuplicateIdTests
{
    private const string Json = """
        { "feature_management": { "feature_flags": [
            { "id": "Dup", "enabled": false, "description": "first" },
            { "id": "Dup", "enabled": true, "description": "last" } ] } }
        """;

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Json))).Build();

    [Test]
    public async Task Microsoft_s_provider_uses_the_last_entry()
    {
        FeatureDefinition? definition = await new ConfigurationFeatureDefinitionProvider(Configuration()).GetFeatureDefinitionAsync("Dup");

        Assert.That(Enabled(definition), Is.True, "Microsoft's provider reads the last entry");
    }

    [Test]
    public void Raw_and_details_read_the_last_entry_too()
    {
        var details = new ConfigurationFlagDetails(Configuration());

        Assert.That(details.Raw("Dup")!["description"]!.ToString(), Is.EqualTo("last"));
        Assert.That(details.Find("Dup").Description, Is.EqualTo("last"));
    }

    // A flag that is switched on has a filter that is always on; a flag that is off has none.
    private static bool Enabled(FeatureDefinition definition) => definition.EnabledFor.Any();
}
