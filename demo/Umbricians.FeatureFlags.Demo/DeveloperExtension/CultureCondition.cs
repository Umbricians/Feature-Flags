using System.Text.Json.Nodes;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Demo.DeveloperExtension;

/// <summary>
/// Example developer extension, the half the backoffice uses: tells Feature Flags that the culture filter's parameters
/// are understood, checks them on every save and puts them in one line for the list.
/// </summary>
public sealed class CultureCondition : IFeatureFlagCondition
{
    public string Name => CultureFilter.Alias;

    public string DisplayName => "Culture";

    public IEnumerable<string> Validate(JsonObject parameters)
    {
        if (parameters["Cultures"] is not JsonArray { Count: > 0 })
        {
            yield return "Choose at least one culture.";
        }
    }

    public string Describe(JsonObject parameters) =>
        string.Join(" and ", (parameters["Cultures"] as JsonArray ?? []).Select(culture => culture?.ToString()));
}
