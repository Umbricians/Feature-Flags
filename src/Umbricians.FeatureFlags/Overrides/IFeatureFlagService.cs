using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Overrides;

/// <summary>
/// Reads flags and changes their overrides from code, going through the same path as the backoffice: the same rules,
/// and the new settings are read again before the call returns, so the next check on this server sees them. Other
/// servers see them within seconds. Change overrides through this, not by writing to <see cref="IFeatureFlagOverrideStore"/>,
/// which skips the rules and the refresh on other servers.
/// </summary>
/// <remarks>
/// The backoffice's rules apply (settings from the backoffice may be switched off, a flag nobody declares can't be
/// saved, a custom flag can't be switched), but there are no permission checks: code isn't a backoffice user. Problems
/// come back as a <see cref="FeatureFlagResult"/>; exceptions are for real failures such as the database being down.
/// To change several flags, call it once for each. Each change publishes the notifications in
/// <c>docs/notifications.md</c>; a handler of the Saving or Resetting notification can cancel it, and the result is then
/// <see cref="FeatureFlagResultStatus.Cancelled"/>.
/// </remarks>
public interface IFeatureFlagService
{
    /// <summary>Every flag: each declared flag, and each override left for a flag nobody declares any more. Filter it with LINQ.</summary>
    Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One flag, found ignoring case, or null when nobody declares it and it has no override.</summary>
    Task<FeatureFlag?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Saves settings as the flag's override. Audience groups are written the way targeting compares them.</summary>
    /// <param name="name">The flag's name, ignoring case.</param>
    /// <param name="settings">The new settings.</param>
    /// <param name="userKey">The backoffice user to credit with the change, if any.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FeatureFlagResult> SaveAsync(string name, FlagSettings settings, Guid? userKey = null, CancellationToken cancellationToken = default);

    /// <summary>Switches a flag on or off, keeping its conditions.</summary>
    /// <param name="name">The flag's name, ignoring case.</param>
    /// <param name="on">Whether it is on.</param>
    /// <param name="userKey">The backoffice user to credit with the change, if any.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FeatureFlagResult> SwitchAsync(string name, bool on, Guid? userKey = null, CancellationToken cancellationToken = default);

    /// <summary>Removes the flag's override, so it goes back to its default; for a flag nobody declares any more, removes it.</summary>
    /// <param name="name">The flag's name, ignoring case.</param>
    /// <param name="userKey">The backoffice user doing it, if any.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FeatureFlagResult> ResetAsync(string name, Guid? userKey = null, CancellationToken cancellationToken = default);
}

/// <summary>What happened to a change asked for through <see cref="IFeatureFlagService"/>.</summary>
public enum FeatureFlagResultStatus
{
    /// <summary>The change was made, and is already seen by checks on this server.</summary>
    Saved,

    /// <summary>Nobody declares the flag, or it has no override to remove, and the change needs one.</summary>
    NotFound,

    /// <summary>The rules don't allow the change; <see cref="FeatureFlagResult.Reason"/> says why.</summary>
    NotAllowed,

    /// <summary>A handler cancelled the change; <see cref="FeatureFlagResult.Messages"/> are what it said.</summary>
    Cancelled,
}

/// <summary>What happened to a change asked for through <see cref="IFeatureFlagService"/>.</summary>
/// <param name="Status">Whether it was saved, and if not, why.</param>
/// <param name="Reason">Why the rules don't allow it, for <see cref="FeatureFlagResultStatus.NotAllowed"/>.</param>
public sealed record FeatureFlagResult(FeatureFlagResultStatus Status, string? Reason = null)
{
    /// <summary>Whether the change was made.</summary>
    public bool IsSaved => Status == FeatureFlagResultStatus.Saved;

    /// <summary>What the handlers that cancelled the change said; none for any other result.</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];

    internal static FeatureFlagResult Saved { get; } = new(FeatureFlagResultStatus.Saved);

    internal static FeatureFlagResult NotFound { get; } = new(FeatureFlagResultStatus.NotFound);

    internal static FeatureFlagResult NotAllowed(string? reason) => new(FeatureFlagResultStatus.NotAllowed, reason);

    internal static FeatureFlagResult Cancelled(IReadOnlyList<string> messages) => new(FeatureFlagResultStatus.Cancelled) { Messages = messages };
}

/// <summary>Why a flag's override changed.</summary>
public enum FeatureFlagChangeCause
{
    /// <summary>An editor in the backoffice, through the package's Management API.</summary>
    Editor,

    /// <summary>Code, through <see cref="IFeatureFlagService"/>.</summary>
    Code,

    /// <summary>A deleted member or group was taken out of audiences.</summary>
    MemberCleanup,
}
