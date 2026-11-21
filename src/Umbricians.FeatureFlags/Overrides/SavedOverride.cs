using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// An override as read from the store, once: what Microsoft evaluates, and what the backoffice edits or why it can't.
/// Microsoft drops a disabled flag's conditions, so <see cref="Settings"/> is read from the JSON itself and keeps the
/// audience. The audience holds the group ids exactly as saved.
/// </summary>
/// <param name="Override">The override as stored.</param>
/// <param name="Definition">What Microsoft evaluates; null when its JSON can't be read, so the flag uses its default.</param>
/// <param name="Settings">What the backoffice shows and edits; null when it can't edit this override.</param>
/// <param name="NotEditableReason">Why <see cref="Settings"/> is null, when it is.</param>
internal sealed record SavedOverride(
    FeatureFlagOverride Override,
    FeatureDefinition? Definition,
    FlagSettings? Settings,
    string? NotEditableReason)
{
    /// <summary>Reads every override, keyed by flag name the way Microsoft compares names.</summary>
    /// <param name="overrides">The overrides as stored.</param>
    /// <param name="onInvalid">Told about each override whose JSON can't be read.</param>
    public static async Task<IReadOnlyDictionary<string, SavedOverride>> ReadAllAsync(
        IReadOnlyCollection<FeatureFlagOverride> overrides, Action<string, Exception>? onInvalid = null)
    {
        Dictionary<string, FeatureDefinition> definitions = await FlagDefinitions.ParseAsync(
            overrides.Select(o => (o.FlagName, o.Definition)), onInvalid);

        var saved = new Dictionary<string, SavedOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (FeatureFlagOverride featureFlagOverride in overrides)
        {
            definitions.TryGetValue(featureFlagOverride.FlagName, out FeatureDefinition? definition);
            FlagSettings? settings = FlagDefinitions.ReadOverride(featureFlagOverride.Definition, out string? reason);
            saved[featureFlagOverride.FlagName] = new SavedOverride(featureFlagOverride, definition, settings, reason);
        }

        return saved;
    }
}
