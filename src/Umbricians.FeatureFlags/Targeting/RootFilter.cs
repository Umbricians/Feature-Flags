using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;

namespace Umbricians.FeatureFlags.Targeting;

/// <summary>
/// The filter of the Root condition ("Only on these sites"): on when the request is in one of the roots in
/// <c>Roots</c>, a list of root keys. A request in no root, and a root that has been deleted, never match.
/// </summary>
[FilterAlias(Name)]
internal sealed class RootFilter(RequestRoot request) : IFeatureFilter
{
    internal const string Name = "Umbricians.Root";

    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        if (request.Current() is not { } current)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(ReadRoots(context.Parameters).Contains(current));
    }

    /// <summary>The keys in <c>Roots</c>, read from configuration: <c>Roots:0</c>, <c>Roots:1</c>, … Anything that isn't a key is ignored.</summary>
    internal static IReadOnlyList<Guid> ReadRoots(IConfiguration parameters) =>
        parameters.GetSection("Roots").GetChildren()
            .Select(child => Guid.TryParse(child.Value, out Guid key) ? key : (Guid?)null)
            .OfType<Guid>()
            .ToArray();

    /// <summary>The keys in <c>Roots</c> of a condition's parameters; null when it isn't a list of keys.</summary>
    internal static IReadOnlyList<Guid>? ReadRoots(JsonObject parameters)
    {
        JsonNode? node = parameters.FirstOrDefault(pair => string.Equals(pair.Key, "Roots", StringComparison.OrdinalIgnoreCase)).Value;
        if (node is not JsonArray array)
        {
            return null;
        }

        var keys = new List<Guid>();
        foreach (JsonNode? item in array)
        {
            if (item is JsonValue value && value.TryGetValue(out string? text) && Guid.TryParse(text, out Guid key))
            {
                keys.Add(key);
            }
            else
            {
                return null;
            }
        }

        return keys;
    }
}
