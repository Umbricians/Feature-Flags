using System.Text.Json.Nodes;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// The package's own condition "Only on these sites": on when the request is in one of the chosen roots. Roots are
/// stored by key. A root that has been deleted stays in the condition and never matches.
/// </summary>
internal sealed class RootCondition(ISiteRoots roots) : IFeatureFlagCondition
{
    internal const string DeletedSite = "a deleted site";

    public string Name => RootFilter.Name;

    public string DisplayName => "Only on these sites";

    // Shape only: whether a root still exists is not checked, so a flag that names a deleted root can still be saved.
    public IEnumerable<string> Validate(JsonObject parameters)
    {
        IReadOnlyList<Guid>? keys = RootFilter.ReadRoots(parameters);
        if (keys is null)
        {
            return ["Roots must be a list of root keys."];
        }

        return keys.Count == 0 ? ["Choose at least one site."] : [];
    }

    public string Describe(JsonObject parameters)
    {
        IReadOnlyList<Guid>? keys = RootFilter.ReadRoots(parameters);
        if (keys is null or { Count: 0 })
        {
            return "Sites that can't be read";
        }

        return "Only on " + string.Join(", ", keys.Select(key => roots.Find(key)?.Name ?? DeletedSite));
    }
}
