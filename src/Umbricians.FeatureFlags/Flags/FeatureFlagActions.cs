namespace Umbricians.FeatureFlags.Flags;

/// <summary>One thing that may or may not be done with a flag, and why not.</summary>
/// <param name="Allowed">Whether it may be done now.</param>
/// <param name="Reason">Why not, when it may not.</param>
public sealed record FeatureFlagAction(bool Allowed, string? Reason)
{
    internal static FeatureFlagAction Allow { get; } = new(true, null);

    internal static FeatureFlagAction Refuse(string reason) => new(false, reason);
}

/// <summary>What may be done with a flag, each with the reason when not. Decided by the flag catalog only.</summary>
/// <param name="Switch">Switch it on or off, keeping its conditions.</param>
/// <param name="Save">Save new settings for it.</param>
/// <param name="Reset">Remove its override, so it goes back to its default.</param>
/// <param name="Remove">Remove the setting of a flag nobody declares any more.</param>
public sealed record FeatureFlagActions(FeatureFlagAction Switch, FeatureFlagAction Save, FeatureFlagAction Reset, FeatureFlagAction Remove);
