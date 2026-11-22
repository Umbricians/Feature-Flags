using Umbricians.FeatureFlags.Overrides;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>Finds the flags whose Root condition names a root that has been deleted, for the health check.</summary>
internal sealed class DeletedRootFlags(IFeatureFlagService flags, ISiteRoots roots)
{
    /// <summary>The names of those flags, in the order the flags come.</summary>
    public async Task<IReadOnlyList<string>> FindAsync(CancellationToken cancellationToken) =>
        Find(await flags.GetAllAsync(cancellationToken));

    /// <summary>The names of those flags among flags already listed, so a caller that has listed them doesn't list them again.</summary>
    public IReadOnlyList<string> Find(IEnumerable<FeatureFlag> listed)
    {
        var names = new List<string>();
        foreach (FeatureFlag flag in listed)
        {
            bool deleted = (flag.Settings?.Conditions ?? [])
                .Where(condition => string.Equals(condition.Name, RootFilter.Name, StringComparison.OrdinalIgnoreCase)
                    || (!condition.Name.Contains('.') && RootFilter.Name.EndsWith("." + condition.Name, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(condition => RootFilter.ReadRoots(condition.Parameters) ?? [])
                .Any(key => roots.Find(key) is null);
            if (deleted)
            {
                names.Add(flag.Name);
            }
        }

        return names;
    }
}
