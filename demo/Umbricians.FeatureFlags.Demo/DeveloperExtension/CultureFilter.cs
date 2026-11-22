using System.Globalization;
using Microsoft.FeatureManagement;

namespace Umbricians.FeatureFlags.Demo.DeveloperExtension;

/// <summary>
/// Example developer extension, the half Microsoft runs: on for requests in the chosen cultures. A culture with a region,
/// such as <c>en-GB</c>, matches only that culture; a language alone, such as <c>fr</c>, matches every French culture.
/// </summary>
[FilterAlias(Alias)]
public sealed class CultureFilter : IFeatureFilter
{
    public const string Alias = "Demo.Culture";

    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        CultureFilterSettings settings = context.Parameters.Get<CultureFilterSettings>() ?? new();
        CultureInfo current = CultureInfo.CurrentUICulture;
        return Task.FromResult(settings.Cultures.Any(culture =>
            string.Equals(culture, current.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(culture, current.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>The culture filter's parameters: <c>{ "Cultures": [ "en-GB", "fr" ] }</c>.</summary>
public sealed class CultureFilterSettings
{
    public string[] Cultures { get; set; } = [];
}
