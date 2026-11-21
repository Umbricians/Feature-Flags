namespace Umbricians.FeatureFlags.Flags;

/// <summary>One thing an editor may or may not do with a flag, and why not.</summary>
internal sealed record FlagAction(bool Allowed, string? Reason)
{
    public static FlagAction Allow { get; } = new(true, null);

    public static FlagAction Refuse(string reason) => new(false, reason);
}

/// <summary>What an editor may do with a flag, each with the reason when not. Decided by the catalog only.</summary>
/// <param name="Switch">Switch it on or off, keeping its audience.</param>
/// <param name="Save">Save new settings for it.</param>
/// <param name="Reset">Remove its override, so it goes back to its default.</param>
/// <param name="Remove">Remove the setting of a flag nobody declares any more.</param>
internal sealed record FlagActions(FlagAction Switch, FlagAction Save, FlagAction Reset, FlagAction Remove);
